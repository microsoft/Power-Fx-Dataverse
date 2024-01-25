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
        // Lookup, expect exactly one. 
        public static async Task<T> RetrieveAsync<T>(this IDataverseReader reader, string filterName, string filterValue, CancellationToken cancel)
              where T : class, new()
        {
            var filter = new FilterExpression();
            filter.AddCondition(filterName, ConditionOperator.Equal, filterValue);

            var tableName = typeof(T).GetEntityName();
            var query = new QueryExpression(tableName)
            {
                Criteria = filter,
                ColumnSet = new ColumnSet(true) // Could infer from T
            };

            var list = await reader.RetrieveMultipleAsync(query, cancel);
            list.ThrowEvalExOnError();
            
            var entities = list.Response.Entities;
            if (entities.Count != 1)
            {
                throw new InvalidOperationException($"{entities.Count} entities in {tableName} with {filterName} equal to {filterValue}.");
            }
            var entity = entities[0];
            return entity.ToObject<T>();
        }

        // Lookup, can return 0, 1, or many. 
        public static Task<T[]> RetrieveMultipleAsync<T>(this IDataverseReader reader, string filterName, object filterValue, CancellationToken cancel)
              where T : class, new()
        {
            var filter = new FilterExpression();
            filter.AddCondition(filterName, ConditionOperator.Equal, filterValue);

            return reader.RetrieveMultipleAsync<T>(filter, cancel);
        }

        // Lookup, can return 0, 1, or many. 
        public static async Task<T[]> RetrieveMultipleAsync<T>(this IDataverseReader reader, FilterExpression filter, CancellationToken cancel)
              where T : class, new()
        {
            var tableName = typeof(T).GetEntityName();
            var query = new QueryExpression(tableName)
            {
                Criteria = filter,
                ColumnSet = new ColumnSet(true) // could infer from T
            };

            var list = await reader.RetrieveMultipleAsync(query, cancel);
            list.ThrowEvalExOnError();

            List<T> items = new List<T>();

            var all = list.Response;
            foreach (var x in all.Entities)
            {
                var param = x.ToObject<T>();
                items.Add(param);
            }

            return items.ToArray();
        }


        // Lookup the entity name via the DataverseEntityAttribute on a type. 
        private static string GetEntityName(this ICustomAttributeProvider type)
        {
            var attrs = type.GetCustomAttributes(typeof(DataverseEntityAttribute), true);
            if (attrs.Length == 1)
            {
                return ((DataverseEntityAttribute)attrs[0]).LogicalName;
            }

            // This is a bug, since only calls here are determined at compile time.  
            throw new InvalidOperationException($"Type {type} does not have a {nameof(DataverseEntityAttribute)}");
        }

        private static string GetEntityPrimeryIdFieldName(this ICustomAttributeProvider type)
        {
            var attrs = type.GetCustomAttributes(typeof(DataverseEntityPrimaryIdAttribute), true);

            // All entity does not need to populate their primary id field name.
            if(attrs.Length == 0)
            {
                return string.Empty;
            }

            if (attrs.Length == 1)
            {
                return ((DataverseEntityPrimaryIdAttribute)attrs[0]).PrimeryIdFieldName;
            }

            // This is a bug, since only calls here are determined at compile time.  
            throw new InvalidOperationException($"Type {type} does not have a {nameof(DataverseEntityAttribute)}");
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
                if (entity.Attributes.TryGetValue(prop.Name, out object val) || TryGetPrimaryIdField(prop, targetType, entity, out val))
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

        private static bool TryGetPrimaryIdField(PropertyInfo prop, Type entityType, Entity entity, out object val)
        {
            if (prop.PropertyType == typeof(Guid))
            {
                string primaryIdFieldName = entityType.GetEntityPrimeryIdFieldName();
                if(primaryIdFieldName == prop.Name)
                {
                    val = entity.Id;
                    return true;
                }
            }

            val = null;
            return false;
        }

        public static T[] ToArray<T>(this EntityCollection entityCollection)
            where T : class, new()
        {
            var entities = entityCollection.Entities;
            T[] result = new T[entities.Count];
            for(int i = 0; i < entities.Count; i++) {
                Entity entity = entities[i];
                T obj = entity.ToObject<T>();
                result[i] = obj;
            }
            return result;
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
