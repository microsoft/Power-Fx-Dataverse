// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
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
            return ToAttributeObject(amd, fxValue, false);
        }

        internal static object ToAttributeObject(this AttributeMetadata amd, FormulaValue fxValue, bool isUsedinQueryExpression)
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
                case AttributeTypeCode.Status:
                case AttributeTypeCode.State:
                    if (isUsedinQueryExpression)
                    {
                        return int.Parse(((FxOptionSetValue)fxValue).Option);
                    }

                    return new XrmOptionSetValue(int.Parse(((FxOptionSetValue)fxValue).Option));

                case AttributeTypeCode.Money:
                    if (fxValue is DecimalValue dvm)
                    {
                        if (isUsedinQueryExpression)
                        {
                            return dvm.Value;
                        }

                        return new Money(dvm.Value);
                    }
                    else
                    {
                        if (isUsedinQueryExpression)
                        {
                            return (decimal)((NumberValue)fxValue).Value;
                        }

                        return new Money((decimal)((NumberValue)fxValue).Value);
                    }

                case AttributeTypeCode.Virtual:
                    if (amd is MultiSelectPicklistAttributeMetadata)
                    {
                        var tableValue = (TableValue)fxValue;
                        var optionSetValueCollection = new OptionSetValueCollection();
                        var optionSetValueSet = new HashSet<XrmOptionSetValue>();

                        foreach (var row in tableValue.Rows)
                        {
                            if (row.IsError)
                            {
                                throw new InvalidOperationException($"The requested operation for {amd.LogicalName} field is invalid.");
                            }

                            var fieldValue = row.Value.GetField("Value");

                            // Errors and blanks are ignored
                            if (fieldValue is FxOptionSetValue fxOptionSetValue)
                            {
                                optionSetValueSet.Add(new XrmOptionSetValue(int.Parse(fxOptionSetValue.Option)));
                            }
                        }

                        optionSetValueCollection.AddRange(optionSetValueSet);

                        return optionSetValueCollection;
                    }

                    throw new NotImplementedException($"FieldType {amd.AttributeTypeName.Value} not supported");

                case AttributeTypeCode.Lookup: // EntityReference
                    if (fxValue is DataverseColumnMapRecordValue cmrv)
                    {
                        if (isUsedinQueryExpression)
                        {
                            return cmrv.RecordValue.EntityReference.Id;
                        }

                        return cmrv.RecordValue.EntityReference;
                    }

                    if (fxValue is DataverseRecordValue dv)
                    {
                        if (isUsedinQueryExpression)
                        {
                            return dv.EntityReference.Id;
                        }

                        return dv.EntityReference;
                    }

                    goto default;

                case AttributeTypeCode.CalendarRules:
                case AttributeTypeCode.Customer:
                case AttributeTypeCode.EntityName:
                case AttributeTypeCode.ManagedProperty:
                case AttributeTypeCode.PartyList:
                default:
                    throw new NotImplementedException($"FieldType {amd.AttributeType.Value} not supported");
            }
        }
    }
}
