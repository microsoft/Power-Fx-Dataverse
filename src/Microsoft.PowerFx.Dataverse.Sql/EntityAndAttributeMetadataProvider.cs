using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.PowerFx.Dataverse
{
    public class EntityAndAttributeMetadataProvider
    {
        private readonly IEntityAndAttributeMetadataProvider _metadataProvider;

        /// <summary>
        /// Cache of EntityMetadata - entity's additional properties like base table names, etc., indexed by entity's logical name
        /// </summary>
        private readonly ConcurrentDictionary<string, AddtionalEntityMetadata> _entityMetadataCache = new ConcurrentDictionary<string, AddtionalEntityMetadata>(StringComparer.Ordinal);

        public EntityAndAttributeMetadataProvider(IEntityAndAttributeMetadataProvider metadataProvider)
        {
            _metadataProvider = metadataProvider;
        }

        internal bool TryGetCDSEntityMetadata(string logicalName, out AddtionalEntityMetadata entityMetadata)
        {
            if (_entityMetadataCache.TryGetValue(logicalName, out entityMetadata))
            {
                return true;
            }

            if (_metadataProvider != null && _metadataProvider.TryGetAdditionalEntityMetadata(logicalName, out entityMetadata))
            {
                _entityMetadataCache[logicalName] = entityMetadata;
                return true;
            }

            return false;
        }

        internal bool TryGetBaseTableName(string logicalName, out string baseTableName)
        {
            if (TryGetCDSEntityMetadata(logicalName, out var entityMetadata))
            {
                baseTableName = entityMetadata.BaseTableName;
                return true;
            }

            baseTableName = null;
            return false;
        }
    }

    public class AddtionalEntityMetadata
    {
        public string BaseTableName { get; set; }
        public bool IsInheritsFromNull { get; set; }
    }

    public class AddtionalAttributeMetadata
    {
        public bool IsStoredOnPrimaryTable { get; set; }
    }
}
