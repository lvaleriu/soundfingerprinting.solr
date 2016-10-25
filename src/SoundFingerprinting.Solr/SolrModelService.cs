﻿namespace SoundFingerprinting.Solr
{
    using System.Collections.Generic;

    using SoundFingerprinting.DAO;
    using SoundFingerprinting.Data;

    public class SolrModelService : ModelService
    {
        public SolrModelService(
            ITrackDao trackDao,
            IHashBinDao hashBinDao,
            ISubFingerprintDao subFingerprintDao, 
            IFingerprintDao fingerprintDao, 
            ISpectralImageDao spectralImageDao)
            : base(trackDao, hashBinDao, subFingerprintDao, fingerprintDao, spectralImageDao)
        {
        }
    }
}
