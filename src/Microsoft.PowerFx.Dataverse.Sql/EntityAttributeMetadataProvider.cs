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

        /// <summary>
        /// Cache of AttributeMetadata - attribute's additional properties like isStoredOnPrimaryTable, etc., indexed by entity's logical name concatenated with attribute's logical name.
        /// </summary>
        private readonly ConcurrentDictionary<string, SecondaryAttributeMetadata> _attributeMetadataCache = new ConcurrentDictionary<string, SecondaryAttributeMetadata>(StringComparer.Ordinal);

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

        internal bool TryGetAttributeMetadata(string entityLogicalName, string columnLogicalName, out SecondaryAttributeMetadata attributeMetadata)
        {
            var key = entityLogicalName + "_" + columnLogicalName;
            if (_attributeMetadataCache.TryGetValue(key, out attributeMetadata))
            {
                return true;
            }

            if (_metadataProvider != null && _metadataProvider.TryGetSecondaryAttributeMetadata(entityLogicalName, columnLogicalName, out attributeMetadata))
            {
                _attributeMetadataCache[key] = attributeMetadata;
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

        internal bool TryGetExtensionTableName(string logicalName, out string extensionTableName)
        {
            if (TryGetEntityMetadata(logicalName, out var entityMetadata))
            {
                extensionTableName = entityMetadata.ExtensionTableName;
                return true;
            }

            extensionTableName = null;
            return false;
        }

        /// <summary>
        /// Returns true if field from an inherited entity requires reference.
        /// For current entity, we check if entity is inherited from another entity and if the dependent field is stored on primary table. If entity's 
        /// inheritsFrom is not null(false) and if field is stored on primary table(true), even if this field is a simple/rollup field, it requires reference.
        /// For related entity's field, as they anyways require reference and if entity's inheritsfrom is null, field's IsStoredOnPrimaryTable will always be true 
        /// so, we only check if field is stored on primary table and refer the dependent field from extension table name if it is not stored on primary table 
        /// else refer from base/primary table.
        /// </summary>
        internal bool IsFieldOnInheritedEntityRequiresReference(string entityLogicalName, string columnLogicalName, bool isRelatedEntityField)
        {
            if (TryGetEntityMetadata(entityLogicalName, out var entityMetadata) && TryGetAttributeMetadata(entityLogicalName, columnLogicalName, out var attributeMetadata))
            {
                return isRelatedEntityField ? !attributeMetadata.IsStoredOnPrimaryTable : entityMetadata.IsInheritsFromNull != attributeMetadata.IsStoredOnPrimaryTable;
            }

            return false;
        }

        /// <summary>
        /// Returns true if TableColumnName and SchemaName of a field are different and returns TableColumnName, else returns false.
        /// For an entity which is inherited from another entity and a dependent field is stored on primary table, there are cases where the schema name
        /// of this field is different on the primary table. For e.g., Category field on Task entity has name as 'TaskCategory' on the primary table. 
        /// So, we use TableColumnName property for referring such fields from primary table.
        /// </summary>
        internal bool IsSchemaNameDifferentFromTableColumnName(SqlVisitor.Context.VarDetails varDetails, out string tableColumnName)
        {
            tableColumnName = null;
            if (varDetails == null) { return false; }

            var entityLogicalName = varDetails.Table;
            var columnLogicalName = varDetails.Column.LogicalName;
            var columnPhysicalName = varDetails.Column.SchemaName;

            if (TryGetEntityMetadata(entityLogicalName, out var entityMetadata) && TryGetAttributeMetadata(entityLogicalName, columnLogicalName, out var attributeMetadata))
            {
                if (!entityMetadata.IsInheritsFromNull && attributeMetadata.IsStoredOnPrimaryTable && 
                    !columnPhysicalName.Equals(attributeMetadata.TableColumnName, StringComparison.OrdinalIgnoreCase) &&
                    attributeMetadata.TableColumnName != null)
                {
                    tableColumnName = attributeMetadata.TableColumnName;
                    return true;
                }
            }

            return false;
        }
    }

    public class SecondaryEntityMetadata
    {
        public string BaseTableName { get; set; }
        public string ExtensionTableName { get; set; }
        public bool IsInheritsFromNull { get; set; }
    }

    public class SecondaryAttributeMetadata
    {
        public bool IsStoredOnPrimaryTable { get; set; }
        public string TableColumnName { get; set; }
    }
}
