//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
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
            realAttributeName = null;
            return false;
        }

        /// <summary>
        /// Convert an Power Fx FormulaValue to an entity Object value. 
        /// </summary>
        /// <param name="amd"></param>
        /// <param name="fxValue"></param>
        /// <returns>object that can be assigned to Entity.Attributes.</returns>
        public static object ToAttributeObject(AttributeMetadata amd, FormulaValue fxValue)
        {
            if (fxValue is BlankValue)
            {
                return null;
            }

            switch (amd.AttributeType.Value)
            {
                case AttributeTypeCode.Boolean:
                    return ((BooleanValue)fxValue).Value;

                case AttributeTypeCode.DateTime:
                    return (DateTime) fxValue.ToObject();

                case AttributeTypeCode.Decimal:
                    return (Decimal)((NumberValue)fxValue).Value;

                case AttributeTypeCode.Double:
                    return ((NumberValue)fxValue).Value;

                case AttributeTypeCode.Integer:
                    return (int)((NumberValue)fxValue).Value;

                case AttributeTypeCode.String:
                    return ((StringValue)fxValue).Value;

                case AttributeTypeCode.BigInt:
                case AttributeTypeCode.Uniqueidentifier:
                    return fxValue.ToObject();

                case AttributeTypeCode.Picklist:
                    return new XrmOptionSetValue(int.Parse(((FxOptionSetValue) fxValue).Option));
                    
                case AttributeTypeCode.Money:
                    return new Money((decimal)((NumberValue)fxValue).Value);
                                    
                case AttributeTypeCode.CalendarRules:
                case AttributeTypeCode.Customer:
                case AttributeTypeCode.EntityName:
                case AttributeTypeCode.Virtual:
                case AttributeTypeCode.Lookup: // EntityReference
                case AttributeTypeCode.ManagedProperty:
                case AttributeTypeCode.Memo:
                case AttributeTypeCode.PartyList:
                case AttributeTypeCode.State:
                case AttributeTypeCode.Status:
                default:
                    throw new NotImplementedException($"FieldType {amd.AttributeType.Value} not supported");
            }
        }
    }
}