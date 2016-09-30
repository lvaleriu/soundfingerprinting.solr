﻿using System;

namespace SoundFingerprinting.Solr
{
    using SoundFingerprinting.DAO;

    public class SolrModelReference : ModelReference<string>
    {
        public SolrModelReference(string id)
            : base(id)
        {
        }
    }
}