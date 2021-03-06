﻿namespace SoundFingerprinting.Solr
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Globalization;
    using System.Linq;

    using SolrNet;
    using SolrNet.Commands.Parameters;

    using SoundFingerprinting.Configuration;
    using SoundFingerprinting.DAO;
    using SoundFingerprinting.DAO.Data;
    using SoundFingerprinting.Data;
    using SoundFingerprinting.Infrastructure;
    using SoundFingerprinting.Math;
    using SoundFingerprinting.Solr.Config;
    using SoundFingerprinting.Solr.Converters;
    using SoundFingerprinting.Solr.DAO;

    internal class SubFingerprintDao : ISubFingerprintDao
    {
        private readonly int fingerprintLength;
        private readonly ISolrOperations<SubFingerprintDTO> solr;
        private readonly IDictionaryToHashConverter dictionaryToHashConverter;
        private readonly IHashConverter hashConverter;
        private readonly ISolrQueryBuilder solrQueryBuilder;
        private readonly ISoundFingerprintingSolrConfig solrConfig;

        public SubFingerprintDao()
            : this(
                DependencyResolver.Current.Get<ISolrOperations<SubFingerprintDTO>>(),
                DependencyResolver.Current.Get<IDictionaryToHashConverter>(),
                DependencyResolver.Current.Get<IHashConverter>(),
                DependencyResolver.Current.Get<ISolrQueryBuilder>(),
                DependencyResolver.Current.Get<ISoundFingerprintingSolrConfig>())
        {
        }

        internal SubFingerprintDao(ISolrOperations<SubFingerprintDTO> solr, IDictionaryToHashConverter dictionaryToHashConverter, IHashConverter hashConverter, ISolrQueryBuilder solrQueryBuilder, ISoundFingerprintingSolrConfig solrConfig)
        {
            this.solr = solr;
            this.dictionaryToHashConverter = dictionaryToHashConverter;
            this.hashConverter = hashConverter;
            this.solrQueryBuilder = solrQueryBuilder;
            var hashinConfig = new DefaultHashingConfig();
            fingerprintLength = hashinConfig.NumberOfLSHTables * hashinConfig.NumberOfMinHashesPerTable;
            this.solrConfig = solrConfig;
        }

        public void InsertHashDataForTrack(IEnumerable<HashedFingerprint> hashes, IModelReference trackReference)
        {
            var dtos = hashes.Select(hash => GetSubFingerprintDto(trackReference, hash))
                             .ToList();
            solr.AddRange(dtos);
            solr.Commit();
        }

        public IList<HashedFingerprint> ReadHashedFingerprintsByTrackReference(IModelReference trackReference)
        {
            var results = solr.Query(string.Format("trackId:{0}", SolrModelReference.GetId(trackReference)));
            return results.Select(GetHashedFingerprint).ToList();
        }

        public IEnumerable<SubFingerprintData> ReadSubFingerprints(long[] hashBins, int thresholdVotes, IEnumerable<string> clusters)
        {
            string queryString = solrQueryBuilder.BuildReadQueryForHashes(hashBins);
            var results = solr.Query(
                new SolrQuery(queryString),
                new QueryOptions
                    {
                        ExtraParams = GetThresholdVotes(thresholdVotes),
                        FilterQueries = GetFilterQueries(clusters)
                    });

            return ConvertResults(results);
        }
        
        public ISet<SubFingerprintData> ReadSubFingerprints(IEnumerable<long[]> hashes, int threshold, IEnumerable<string> clusters)
        {
            var enumerable = hashes as List<long[]> ?? hashes.ToList();
            int total = enumerable.Count();
            var result = new HashSet<SubFingerprintData>();
            var filterQuery = GetFilterQueries(clusters);
            int batchSize = solrConfig.QueryBatchSize;
            bool preferLocalShards = solrConfig.PreferLocalShards;
            for (int i = 0; i < total; i += batchSize)
            {
                var batch = enumerable.Skip(i).Take(batchSize);
                string queryString = solrQueryBuilder.BuildReadQueryForHashesAndThreshold(batch, threshold);
                var results = solr.Query(
                    new SolrQuery(queryString),
                    new QueryOptions
                        {
                            FilterQueries = filterQuery,
                            ExtraParams = new Dictionary<string, string> { { "preferLocalShards", preferLocalShards.ToString().ToLower() } }
                        });
                result.UnionWith(ConvertResults(results));
            }

            return result;
        }

        private IEnumerable<SubFingerprintData> ConvertResults(IEnumerable<SubFingerprintDTO> results)
        {
            return results.Select(GetSubFingerprintData).ToList();
        }

        private Dictionary<string, string> GetThresholdVotes(int thresholdVotes)
        {
            return new Dictionary<string, string>
                {
                    { "defType", "edismax" },
                    { "mm", thresholdVotes.ToString(CultureInfo.InvariantCulture) }
                };
        }

        private SubFingerprintDTO GetSubFingerprintDto(IModelReference trackReference, HashedFingerprint hash)
        {
            return new SubFingerprintDTO
            {
                SubFingerprintId = Guid.NewGuid().ToString(),
                Hashes = dictionaryToHashConverter.FromHashesToSolrDictionary(hash.HashBins),
                SequenceAt = hash.StartsAt,
                SequenceNumber = hash.SequenceNumber,
                TrackId = SolrModelReference.GetId(trackReference),
                Clusters = hash.Clusters
            };
        }

        private HashedFingerprint GetHashedFingerprint(SubFingerprintDTO subFingerprintDto)
        {
            long[] hashBins = dictionaryToHashConverter.FromSolrDictionaryToHashes(subFingerprintDto.Hashes);
            byte[] signature = hashConverter.ToBytes(hashBins, fingerprintLength);
            return new HashedFingerprint(signature, hashBins, subFingerprintDto.SequenceNumber, subFingerprintDto.SequenceAt, subFingerprintDto.Clusters);
        }

        private SubFingerprintData GetSubFingerprintData(SubFingerprintDTO dto)
        {
            long[] resultHashBins = this.dictionaryToHashConverter.FromSolrDictionaryToHashes(dto.Hashes);
            var sub = new SubFingerprintData(
                resultHashBins,
                dto.SequenceNumber,
                dto.SequenceAt,
                new SolrModelReference(dto.SubFingerprintId),
                new SolrModelReference(dto.TrackId));
            return sub;
        }

        private ICollection<ISolrQuery> GetFilterQueries(IEnumerable<string> clusters)
        {
            var values = clusters as List<string> ?? clusters.ToList();
            if (!values.Any())
            {
                return new Collection<ISolrQuery>();
            }

            return new List<ISolrQuery> 
                {
                    new SolrQuery(solrQueryBuilder.BuildQueryForClusters(values))
                };
        }
    }
}
