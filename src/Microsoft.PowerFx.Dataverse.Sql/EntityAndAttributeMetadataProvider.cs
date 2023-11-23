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
        private readonly ConcurrentDictionary<string, Dictionary<string, object>> _entityMetadataCache = new ConcurrentDictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Cache of AttributeMetadata, indexed by entity's logical concatenated with attribute's logical name
        /// </summary>
        private readonly ConcurrentDictionary<string, Dictionary<string, object>> _attributeMetadataCache = new ConcurrentDictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);

        public EntityAndAttributeMetadataProvider(IEntityAndAttributeMetadataProvider metadataProvider)
        {
            _metadataProvider = metadataProvider;
        }

        internal bool TryGetCDSEntityMetadata(string logicalName, out Dictionary<string, object> entityMetadata)
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

        internal bool TryGetCDSAttributeMetadata(string entityLogicalName, string columnLogicalName, out Dictionary<string, object> attributeMetadata)
        {
            var key = entityLogicalName + "_" + columnLogicalName;
            if (_attributeMetadataCache.TryGetValue(key, out attributeMetadata))
            {
                return true;
            }

            if (_metadataProvider != null && _metadataProvider.TryGetAdditionalAttributeMetadata(entityLogicalName, columnLogicalName, out attributeMetadata))
            {
                _attributeMetadataCache[key] = attributeMetadata;
                return true;
            }

            return false;
        }

        internal bool TryGetBaseTableName(string logicalName, out string baseTableName)
        {
            if (TryGetCDSEntityMetadata(logicalName, out var entityMetadata) &&
                entityMetadata.TryGetValue("basetablename", out var name))
            {
                baseTableName = (string)name;
                return true;
            }

            baseTableName = null;
            return false;
        }
    }
}
