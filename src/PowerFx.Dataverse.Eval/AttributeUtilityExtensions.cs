//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using FxOptionSetValue = Microsoft.PowerFx.Types.OptionSetValue;
using XrmOptionSetValue = Microsoft.Xrm.Sdk.OptionSetValue;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Operations on dataverse Attributes. 
    /// These can use FormulaValues like records and Tables.
    /// See <see cref="AttributeUtility"/> for core methods that don't require Fx.
    /// </summary>
    public static class AttributeUtilityExtensions
    {
        /// <summary>
        /// Convert an Power Fx FormulaValue to an entity Object value. 
        /// </summary>
        /// <param name="amd"></param>
        /// <param name="fxValue"></param>
        /// <returns>object that can be assigned to Entity.Attributes.</returns>
        public static object ToAttributeObject(this AttributeMetadata amd, FormulaValue fxValue)
        {
            if (fxValue is BlankValue)
            {
                return null;
            }
            else if (fxValue is ErrorValue)
            {
                throw new InvalidOperationException($"ErrorValue can not be serialized for : {amd.DisplayName} with Type: {amd.AttributeTypeName}");
            }


            switch (amd.AttributeType.Value)
            {
                case AttributeTypeCode.Boolean:
                    if (fxValue is FxOptionSetValue optionSetValue)
                    {
                        if (AttributeUtility.TryConvertBooleanOptionSetOptionToBool(optionSetValue.Option, out bool result))
                        {
                            return result;
                        }

                        throw new NotImplementedException($"BooleanOptionSet {optionSetValue.Option} not supported");
                    }

                    return ((BooleanValue)fxValue).Value;

                case AttributeTypeCode.DateTime:
                    return (DateTime)fxValue.ToObject();

                case AttributeTypeCode.Decimal:
                    if (fxValue is DecimalValue dvd)
                    {
                        return dvd.Value;
                    }
                    else
                    {
                        return (decimal)((NumberValue)fxValue).Value;
                    }

                case AttributeTypeCode.Double:
                    return ((NumberValue)fxValue).Value;

                case AttributeTypeCode.Integer:
                    if (fxValue is DecimalValue dvi)
                    {
                        return (int)dvi.Value;
                    }
                    else
                    {
                        return (int)((NumberValue)fxValue).Value;
                    }

                case AttributeTypeCode.Memo:
                case AttributeTypeCode.String:
                    return ((StringValue)fxValue).Value;

                case AttributeTypeCode.BigInt:
                    if (fxValue is DecimalValue dvb)
                    {
                        return (long)dvb.Value;
                    }
                    else
                    {
                        return (long)((NumberValue)fxValue).Value;
                    }
                    
                case AttributeTypeCode.Uniqueidentifier:
                    return ((GuidValue)fxValue).Value;

                case AttributeTypeCode.Picklist:
                    return new XrmOptionSetValue(int.Parse(((FxOptionSetValue)fxValue).Option));

                case AttributeTypeCode.Money:
                    if (fxValue is DecimalValue dvm)
                    {
                        return new Money(dvm.Value);
                    }
                    else
                    {
                        return new Money((decimal)((NumberValue)fxValue).Value);
                    }

                case AttributeTypeCode.Lookup: // EntityReference
                    if (fxValue is DataverseRecordValue dv)
                    {
                        return dv.EntityReference;
                    }
                    goto default;

                case AttributeTypeCode.CalendarRules:
                case AttributeTypeCode.Customer:
                case AttributeTypeCode.EntityName:
                case AttributeTypeCode.Virtual:
                case AttributeTypeCode.ManagedProperty:
                case AttributeTypeCode.PartyList:
                case AttributeTypeCode.State:
                case AttributeTypeCode.Status:
                default:
                    throw new NotImplementedException($"FieldType {amd.AttributeType.Value} not supported");
            }
        }
    }
}
