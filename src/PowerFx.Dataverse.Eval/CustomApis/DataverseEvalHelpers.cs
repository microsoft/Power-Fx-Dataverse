//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse
{
    internal static class DataverseEvalHelpers
    {
        // Convert individual input to Power Fx value. 
        // Handle Entity,EntityRef which may require marshalling. 
        private static FormulaValue ToPowerFxValue(object obj, DataverseConnection dvc)
        {
            if (obj == null)
            {
                // $$$ Specify type
                return FormulaValue.NewBlank();
            }

            if (obj is EntityReference er)
            {
                // For entity Reference, the lookup could be deferred.
                var record = dvc.RetrieveAsync(er.LogicalName, er.Id).GetAwaiter().GetResult();
                return record;
            }
            else if (obj is Entity entity)
            {
                var record = dvc.Marshal(entity);
                return record;
            }
            else if (obj is EntityCollection inputEntityCollection)
            {
                var records = new List<RecordValue>();
                foreach (Entity input in inputEntityCollection.Entities)
                {
                    records.Add(dvc.Marshal(input));
                }
                // Handle empty input entityCollection
                var tableValue = (records.Count != 0) ?
                                    FormulaValue.NewTable(records[0].Type, records.ToArray()) :
                                    FormulaValue.NewTable(RecordType.Empty(), new RecordValue[0]);
                return tableValue;
            }

            return PrimitiveValueConversions.Marshal(obj, obj.GetType());
        }



        /// <summary>
        /// Returns a RecordValue where each field matches an output parameter. 
        /// Unless there is 1 output parameter, named "Value" - in which case we return just that. 
        /// </summary>
        /// <param name="outputMetadata"></param>
        /// <returns></returns>
        public static bool IsOutputTypeSingle(params CustomApiResponse[] outputMetadata)
        {
            return outputMetadata.Length == 1 && outputMetadata[0].name == TableValue.ValueName;
        }



        internal static object ToCustomApiObject(this FormulaValue fxValue, IParameterType parameterType)
        {
            //ThrowIfErrorValue(hintName, fxValue);

            if (fxValue is BlankValue)
            {
                return null;
            }

            // Use Power Fx coercions to handle the corner cases like Decimal / Number /Float. 
            // Don't use 'var' here, clearly specify .Net type we get back and are returning to Dataverse.
            var typeCode = parameterType.type;
            switch (typeCode)
            {
                case CustomApiParamType.Bool:
                    return ((BooleanValue)fxValue).Value;

                case CustomApiParamType.DateTime:
                    if (fxValue.TryCoerceTo(out DateTimeValue dtv))
                    {

                        DateTime result = dtv.GetConvertedValue(TimeZoneInfo.Utc);
                        return result;
                    }
                    break;

                case CustomApiParamType.Decimal:
                    if (fxValue.TryCoerceTo(out DecimalValue dec))
                    {
                        decimal result = dec.Value;
                        return result;
                    }
                    break;

                case CustomApiParamType.Float:
                    if (fxValue.TryCoerceTo(out NumberValue num))
                    {
                        double result = num.Value;
                        return result;
                    }
                    break;

                case CustomApiParamType.Integer:
                    if (fxValue.TryCoerceTo(out DecimalValue num2))
                    {
                        int result = (int)num2.Value;
                        return result;
                    }
                    break;

                case CustomApiParamType.String:
                    if (fxValue.TryCoerceTo(out StringValue str))
                    {
                        string result = str.Value;
                        return result;
                    }
                    break;

                case CustomApiParamType.Guid:
                    return ((GuidValue)fxValue).Value;

                case CustomApiParamType.Entity:
                    {
                        return ToCdsEntity(fxValue, parameterType);
                    }

                case CustomApiParamType.EntityReference:
                    {
                        return ToCdsEntity(fxValue, parameterType).ToEntityReference();
                    }

                case CustomApiParamType.EntityCollection:
                    {
                        EntityCollection outputEntityCollection = new EntityCollection();
                        TableValue outputTable = fxValue as TableValue;
                        foreach (DValue<RecordValue> rows in outputTable?.Rows)
                        {
                            if (rows.Value is RecordValue recordVal)
                            {
                                Entity entityVal = ToCdsEntity(recordVal, parameterType);
                                outputEntityCollection.Entities.Add(entityVal);
                            }
                        }

                        return outputEntityCollection;
                    }

                    // TODO: Picklist, StringArray
            }

            // We shouldn't land here at runtime, since this 
            // should have been caught by intellisense at design time. 
            throw new NotSupportedException($"Unsupported param type: {typeCode}. Fx type {fxValue.GetType().Name}");
        }

        /// <summary>
        /// Converts output formulaValue to cds entity
        /// </summary>
        private static Entity ToCdsEntity(FormulaValue fxValue, IParameterType paramType)
        {
            var fxOutputObject = fxValue.ToObject();
            if (fxOutputObject is Entity entity)
            {
                return entity;
            }
            var fxOutputs = (IDictionary<string, object>)(fxOutputObject);
            if (!fxOutputs.TryGetValue(paramType.name, out var outputValue))
            {
                throw new InvalidPluginExecutionException($"Unable to extract value of output from pfx result");
            }

            return (Entity)outputValue;
        }

        public static async Task<T> GetDataverseObjectAsync<T>(this IDataverseReader dvReader, string filterName, string filterValue, CancellationToken cancellationToken)
            where T : class, new()
        {
            cancellationToken.ThrowIfCancellationRequested();

            int i = 0;
            string entityName = typeof(T).GetEntityName();
            
            if (string.IsNullOrEmpty(entityName) && typeof(T).GetProperties().Any())
            {
                entityName = typeof(T).GetProperties().First().GetEntityName();

                if (string.IsNullOrEmpty(entityName))
                {
                    throw new ArgumentException($"Cannot find DataverseEntity attribute on first property of {typeof(T).Name} object.");
                }

                T t = new T();
                string parentId = null;

                foreach (PropertyInfo pi in typeof(T).GetProperties())
                {
                    if (i > 0)
                    {
                        entityName = pi.PropertyType.GetElementTypeOrType().GetEntityName();
                        filterName = pi.PropertyType.GetEntityReferenceName();
                        filterValue = parentId;
                    }

                    Entity[] ea = await dvReader.GetEntitiesAsync(entityName, filterName, filterValue).ConfigureAwait(false);

                    if (ea.Length == 0)
                    {
                        return null;
                    }

                    object[] properties = ea.Select(e => e.ToObject(pi.PropertyType.GetElementTypeOrType())).ToArray();

                    if (pi.PropertyType.IsArray)
                    {
                        Array newArray = Array.CreateInstance(pi.PropertyType.GetElementTypeOrType(), properties.Length);
                        Array.Copy(properties, newArray, properties.Length);

                        pi.SetValue(t, newArray);
                    }
                    else
                    {
                        pi.SetValue(t, properties[0]);
                    }


                    if (i++ == 0)
                    {
                        parentId = ea.First().Id.ToString();
                    }
                }

                return t;
            }

            if (string.IsNullOrEmpty(entityName) && typeof(T).GetFields().Any())
            {
                entityName = typeof(T).GetFields().First().GetEntityName();

                if (string.IsNullOrEmpty(entityName))
                {
                    throw new ArgumentException($"Cannot find DataverseEntity attribute on first field of {typeof(T).Name} object.");
                }

                T t = new T();
                string parentId = null;

                foreach (FieldInfo fi in typeof(T).GetFields())
                {
                    if (i > 0)
                    {
                        entityName = fi.FieldType.GetElementTypeOrType().GetEntityName();
                        filterName = fi.FieldType.GetEntityReferenceName();
                        filterValue = parentId;
                    }

                    Entity[] ea = await dvReader.GetEntitiesAsync(entityName, filterName, filterValue).ConfigureAwait(false);

                    if (ea.Length == 0)
                    {
                        return null;
                    }

                    object[] properties = ea.Select(e => e.ToObject(fi.FieldType.GetElementTypeOrType())).ToArray();

                    if (fi.FieldType.IsArray)
                    {
                        Array newArray = Array.CreateInstance(fi.FieldType.GetElementTypeOrType(), properties.Length);
                        Array.Copy(properties, newArray, properties.Length);

                        fi.SetValue(t, newArray);
                    }
                    else
                    {
                        fi.SetValue(t, properties[0]);
                    }

                    
                    if (i++ == 0)
                    {
                        parentId = ea.First().Id.ToString();
                    }
                }

                return t;
            }

            return null;
        }        

        private static string GetEntityName(this ICustomAttributeProvider type)
        {
            return ((DataverseEntityAttribute)type.GetCustomAttributes(true).FirstOrDefault(ca => ca.GetType() == typeof(DataverseEntityAttribute)))?.LogicalName;
        }

        private static Type GetElementTypeOrType(this Type t)
        {
            return t.IsArray ? t.GetElementType() : t;
        }

        private static string GetEntityReferenceName(this Type type)
        {
            type = GetElementTypeOrType(type);

            return type.GetFields().FirstOrDefault(fi => fi.FieldType == typeof(EntityReference))?.Name 
                ?? type.GetProperties().FirstOrDefault(pi => pi.PropertyType == typeof(EntityReference))?.Name
                ?? throw new ArgumentException($"Cannot find EntityReference property or field on {type.Name} object.");
        }

        private static async Task<Entity[]> GetEntitiesAsync(this IDataverseReader dvReader, string entityName, string filterName, string filterValue)
        {
            FilterExpression filter = new FilterExpression();
            filter.AddCondition(filterName, ConditionOperator.Equal, filterValue);

            QueryExpression query = new QueryExpression(entityName) { ColumnSet = new ColumnSet(true), Criteria = filter };
            DataverseResponse<EntityCollection> ec = await dvReader.RetrieveMultipleAsync(query).ConfigureAwait(false);

            ec.ThrowEvalExOnError();
            
            return ec.Response.Entities.ToArray();
        }

        public static T ToObject<T>(this Entity entity)
            where T : class, new()
        {
            return (T)ToObject(entity, typeof(T));
        }

        public static object ToObject(this Entity entity, Type targetType)
        {
            object obj = Activator.CreateInstance(targetType);

            foreach (var prop in targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (entity.Attributes.TryGetValue(prop.Name, out object val))
                {
                    object val2 = Translate(val, prop.PropertyType);

                    if (val2 != null)
                    {
                        prop.SetValue(obj, val2);
                    }
                }
            }

            return obj;
        }

        private static object Translate(object cdsVal, Type targetType)
        {
            if (cdsVal is EntityCollection collection)
            {
                if (targetType.IsArray)
                {
                    Type elementType = targetType.GetElementType();
                    int count = collection.Entities.Count;
                    Array array = Array.CreateInstance(elementType, count);

                    for (int i = 0; i < count; i++)
                    {
                        Entity item = collection.Entities[i];
                        object obj2 = ToObject(item, elementType);

                        array.SetValue(obj2, i);
                    }

                    return array;
                }
            }

            if (cdsVal is Entity entity)
            {
                return ToObject(entity, targetType);
            }

            if (cdsVal is Microsoft.Xrm.Sdk.OptionSetValue opt && targetType.IsEnum)
            {
                return Enum.ToObject(targetType, opt.Value);
            }

            if (cdsVal is string || cdsVal is int || cdsVal is long || cdsVal is bool || cdsVal is Guid || cdsVal is EntityReference || cdsVal is Microsoft.Xrm.Sdk.OptionSetValue)
            {
                return cdsVal;
            }

            return null;
        }
    }
}
