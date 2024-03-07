//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using FxOptionSetValue = Microsoft.PowerFx.Types.OptionSetValue;
using XrmOptionSetValue = Microsoft.Xrm.Sdk.OptionSetValue;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Operations on dataverse Attributes. 
    /// Takes <see cref="AttributeMetadata"/> and switches on <see cref="AttributeTypeCode"/>.
    /// </summary>
    public static class AttributeUtility
    {
        // Lookup by attribute logical name or by relationship name. 
        public static bool TryGetAttribute(this EntityMetadata entityMetadata, string fieldName, out AttributeMetadata amd)
        {
            if (entityMetadata == null)
            {
                throw new ArgumentNullException(nameof(entityMetadata));
            }

            amd = entityMetadata.Attributes.FirstOrDefault(amd => amd.LogicalName == fieldName);
            return amd != null;
        }

        public static bool TryGetOneToManyRelationship(this EntityMetadata entityMetadata, string fieldName, out OneToManyRelationshipMetadata relationship)
        {
            if(entityMetadata?.OneToManyRelationships == null)
            {
                relationship = default;
                return false;
            }

            foreach (var relation in entityMetadata.OneToManyRelationships ?? Enumerable.Empty<OneToManyRelationshipMetadata>())
            {
                if (relation.ReferencedEntityNavigationPropertyName == fieldName)
                {
                    relationship = relation;
                    return true;
                }
            }

            relationship = default;
            return false;
        }

        public static bool TryGetRelationship(this EntityMetadata entityMetadata, string fieldName, out string realAttributeName)
        {
            if (entityMetadata == null)
            {
                throw new ArgumentNullException(nameof(entityMetadata));
            }

            if (entityMetadata.ManyToOneRelationships != null)
            {
                foreach (var relationships in entityMetadata.ManyToOneRelationships)
                {
                    if (relationships.ReferencingEntityNavigationPropertyName == fieldName)
                    {
                        realAttributeName = relationships.ReferencingAttribute;
                        return realAttributeName != null;
                    }
                }
            }

            if (TryGetLogicalNameFromOdataName(fieldName, out realAttributeName))
            {
                return true;
            }
          
            realAttributeName = null;
            return false;
        }

        public static bool TryGetManyToOneRelationship(this EntityMetadata entityMetadata, string fieldName, out OneToManyRelationshipMetadata relation)
        {
            if (entityMetadata == null)
            {
                throw new ArgumentNullException(nameof(entityMetadata));
            }
            
            if (entityMetadata.ManyToOneRelationships != null)
            {
                foreach (var relationships in entityMetadata.ManyToOneRelationships)
                {
                    if (relationships.ReferencingEntityNavigationPropertyName == fieldName)
                    {
                        relation = relationships;
                        return true;
                    }
                }
            }

            relation = default;
            return false;
        }

        public static bool TryGetManyToOneRelationship(this EntityMetadata entityMetadata, string fieldName, string targetEntityName, out OneToManyRelationshipMetadata relation)
        {
            if (entityMetadata == null)
            {
                throw new ArgumentNullException(nameof(entityMetadata));
            }

            if (entityMetadata.ManyToOneRelationships != null)
            {
                foreach (var relationship in entityMetadata.ManyToOneRelationships)
                {
                    if (relationship.ReferencingAttribute == fieldName && relationship.ReferencedEntity == targetEntityName && relationship.ReferencingEntity == entityMetadata.LogicalName)
                    {
                        relation = relationship;
                        return true;
                    }
                }
            }

            relation = default;
            return false;
        }

        // OData polymorphic case. fieldname is mangled by convention, and not reflected in metadata. 
        // The IR is passing the Odata name (not logical name), and we need to extract the logical name from it. 
        // The odata name is: $"_{logicalName}_value"
        internal static bool TryGetLogicalNameFromOdataName(string fieldName, out string realAttributeName)
        {
            var start = "_";
            var end = "_value";
            if (fieldName.StartsWith(start, StringComparison.Ordinal) && fieldName.EndsWith(end, StringComparison.Ordinal))
            {
                int len = fieldName.Length - start.Length - end.Length;
                if (len > 0)
                {
                    realAttributeName = fieldName.Substring(start.Length, len);
                    return true;
                }
            }
            realAttributeName = null;
            return false;
        }

        public static bool TryConvertBooleanOptionSetOptionToBool(string opt, out bool result)
        {
            result = false;

            if (opt == "0")
            {
                return true;
            }
            else if (opt == "1")
            {
                result = true;
                return true;
            }
            else
            {
                return false;
            }
        }
        public static void ConvertBoolToBooleanOptionSetOption(bool value, out string opt)
        {
            opt = "0";

            if (value)
            {
                opt = "1";
            }
        }

        public static FxOptionSetValue ConvertXrmOptionSetValueToFormulaValue(TableType type, XrmOptionSetValue optionSetValue)
        {
            var fxOptionSetValueType = FormulaType.Build(type.SingleColumnFieldType._type) as OptionSetValueType;

            if (fxOptionSetValueType._type.IsOptionSetBackedByBoolean)
            {
                // Dataverse registers boolean option sets with "1" and "0" as the field names for true and false values
                return new FxOptionSetValue(optionSetValue.Value.ToString(), fxOptionSetValueType, optionSetValue.Value == 1);
            }
            else if (fxOptionSetValueType._type.IsOptionSetBackedByNumber)
            {

                return new FxOptionSetValue(optionSetValue.Value.ToString(), fxOptionSetValueType, (double)optionSetValue.Value);
            }
            else
            {
                throw new InvalidOperationException("Attempted to construct DV option set backed by neither boolean nor number");
            }
        }
    }
}
