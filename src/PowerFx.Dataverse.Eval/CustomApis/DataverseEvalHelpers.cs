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
