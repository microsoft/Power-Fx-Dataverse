﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

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
            var key = entityLogicalName + "-" + columnLogicalName;
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

        /// <summary>
        /// Returns extensiontablename of an entity, if dependent field is from an inherited entity and are not stored on primary table.
        /// For e.g., custom fields on Task Entity are not stored on BaseTable - ActivityPointerBase, they are stored on ExtensionTable - TaskBase.
        /// </summary>
        internal bool ShouldReferFieldFromExtensionTable(string entityLogicalName, string columnLogicalName, out string extensionTableName)
        {
            if (TryGetEntityMetadata(entityLogicalName, out var entityMetadata) && TryGetAttributeMetadata(entityLogicalName, columnLogicalName, out var attributeMetadata) &&
                !entityMetadata.IsInheritsFromNull && !attributeMetadata.IsStoredOnPrimaryTable && !string.IsNullOrEmpty(entityMetadata.ExtensionTableName))
            {
                extensionTableName = entityMetadata.ExtensionTableName;
                return true;
            }

            extensionTableName = null;
            return false;
        }

        /// <summary>
        /// Returns true if dependent field from an inherited entity requires reference.
        /// If dependent field is from an inherited entity and if the field is stored on primary table, even if the dependent field
        /// is a simple/rollup field, it requires reference and cannot be passed as a parameter to the UDF.
        /// For e.g., subject field on Task entity is a simple field, but when it is referred from current entity's formula field, it cannot be
        /// passed as parameter to UDF, instead it should be referred from basetable - activitypointerbase.
        /// </summary>
        internal bool IsInheritedEntityFieldStoredOnPrimaryTable(string entityLogicalName, string columnLogicalName)
        {
            if (TryGetEntityMetadata(entityLogicalName, out var entityMetadata) && TryGetAttributeMetadata(entityLogicalName, columnLogicalName, out var attributeMetadata))
            {
                return !entityMetadata.IsInheritsFromNull && attributeMetadata.IsStoredOnPrimaryTable;
            }

            return false;
        }

        /// <summary>
        /// Returns TableColumnName if TableColumnName and SchemaName of a field are different, else returns SchemaName
        /// For an inherited entity if dependent field is stored on primary table, there are cases where the schema name
        /// of this field is different on the primary table. For e.g., Category field on Task entity has name as 'TaskCategory'
        /// on the primary table. So, we use TableColumnName property for referring such fields from primary table.
        /// </summary>
        internal string GetColumnNameOnPrimaryTable(SqlVisitor.Context.VarDetails field)
        {
            var entityLogicalName = field.Table;
            var columnLogicalName = field.Column.LogicalName;
            var columnPhysicalName = field.Column.SchemaName;

            if (TryGetEntityMetadata(entityLogicalName, out var entityMetadata) && TryGetAttributeMetadata(entityLogicalName, columnLogicalName, out var attributeMetadata))
            {
                if (!entityMetadata.IsInheritsFromNull && attributeMetadata.IsStoredOnPrimaryTable && !string.IsNullOrEmpty(attributeMetadata.TableColumnName) &&
                    !columnPhysicalName.Equals(attributeMetadata.TableColumnName, StringComparison.OrdinalIgnoreCase))
                {
                    return attributeMetadata.TableColumnName;
                }
            }

            return columnPhysicalName;
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
