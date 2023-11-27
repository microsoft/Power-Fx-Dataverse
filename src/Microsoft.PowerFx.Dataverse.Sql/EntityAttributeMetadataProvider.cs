using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.PowerFx.Dataverse
{
    public class EntityAttributeMetadataProvider
    {
        private readonly IEntityAttributeMetadataProvider _metadataProvider;

        /// <summary>
        /// Cache of EntityMetadata - entity's additional properties like base table names, etc., indexed by entity's logical name
        /// </summary>
        private readonly ConcurrentDictionary<string, SecondaryEntityMetadata> _entityMetadataCache = new ConcurrentDictionary<string, SecondaryEntityMetadata>(StringComparer.Ordinal);

        public EntityAttributeMetadataProvider(IEntityAttributeMetadataProvider metadataProvider)
        {
            _metadataProvider = metadataProvider;
        }

        internal bool TryGetEntityMetadata(string logicalName, out SecondaryEntityMetadata entityMetadata)
        {
            if (_entityMetadataCache.TryGetValue(logicalName, out entityMetadata))
            {
                return true;
            }

            if (_metadataProvider != null && _metadataProvider.TryGetSecondaryEntityMetadata(logicalName, out entityMetadata))
            {
                _entityMetadataCache[logicalName] = entityMetadata;
                return true;
            }

            return false;
        }

        internal bool TryGetBaseTableName(string logicalName, out string baseTableName)
        {
            if (TryGetEntityMetadata(logicalName, out var entityMetadata))
            {
                baseTableName = entityMetadata.BaseTableName;
                return true;
            }

            baseTableName = null;
            return false;
        }
    }

    public class SecondaryEntityMetadata
    {
        public string BaseTableName { get; set; }
        public bool IsInheritsFromNull { get; set; }
    }

    public class SecondaryAttributeMetadata
    {
        public bool IsStoredOnPrimaryTable { get; set; }
    }
}
