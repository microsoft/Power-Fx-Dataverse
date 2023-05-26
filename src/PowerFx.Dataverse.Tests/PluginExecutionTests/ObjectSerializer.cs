//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public static class ObjectSerializer
    {
        private static MethodInfo _deserializeGeneric = typeof(ObjectSerializer).GetMethod("Deserialize", 1 /* genericParameterCount */, BindingFlags.Static | BindingFlags.Public, null, new Type[] { typeof(string), typeof(bool) }, null);
        private static MethodInfo _deserializeJsonGeneric = typeof(ObjectSerializer).GetMethod("DeserializeJson", BindingFlags.Static | BindingFlags.Public);
        private static MethodInfo _getArrayGeneric = typeof(ObjectSerializer).GetMethod("GetArray", BindingFlags.Static | BindingFlags.Public);
        private static MethodInfo _getCollectionGeneric = typeof(ObjectSerializer).GetMethod("GetCollection", BindingFlags.Static | BindingFlags.Public);
        private static MethodInfo _getDictionaryGeneric = typeof(ObjectSerializer).GetMethod("GetDictionary", BindingFlags.Static | BindingFlags.Public);
     
        internal static Dictionary<Type, FieldInfo> _fieldInfoCache = new Dictionary<Type, FieldInfo>();
        internal static Dictionary<Type, MethodInfo> _collectionCache = new Dictionary<Type, MethodInfo>();        
        internal static Dictionary<Type, MethodInfo> _dictionaryCache = new Dictionary<Type, MethodInfo>();        
        internal static Dictionary<Type, ConstructorInfo> _constructor0Cache = new Dictionary<Type, ConstructorInfo>();        
        internal static Dictionary<Type, ConstructorInfo> _constructor1Cache = new Dictionary<Type, ConstructorInfo>();
        internal static Dictionary<string, MethodInfo> _deserializeJsonCache = new Dictionary<string, MethodInfo>();

        public static JsonSerializerOptions GetJsonSerializerOptions(bool writeIndented)
        {
            JsonSerializerOptions jsonSerializerOptions = new() { WriteIndented = writeIndented };
            jsonSerializerOptions.Converters.Add(new ObjectConverter());
            jsonSerializerOptions.Converters.Add(new ArrayConverter());
            jsonSerializerOptions.Converters.Add(new DictionaryConverter());
            jsonSerializerOptions.Converters.Add(new ExceptionConverter());
            jsonSerializerOptions.Converters.Add(new OrganizationResponseConverter());
            return jsonSerializerOptions;
        }

        public static string Serialize(object obj)
        {
            return JsonSerializer.Serialize(obj, obj.GetType(), GetJsonSerializerOptions(true));
        }

#if RELEASE
        const bool _release = true;
#else
        const bool _release = false;
#endif

        public static object Deserialize(string jsonString, Type type)
        {
            MethodInfo deserializeMethod = _deserializeGeneric.MakeGenericMethod(type);
            return deserializeMethod.Invoke(null, new object[] { jsonString, _release });
        }

        public static T Deserialize<T>(string str, bool ignoreObjects = false)
        {
            return DeserializeJson<T>(JsonDocument.Parse(str).RootElement, ignoreObjects);
        }

        public static long _ctr = 0;

        public static T DeserializeJson<T>(JsonElement jsonElement, bool ignoreObjects = false)
        {
            if (jsonElement.ValueKind == JsonValueKind.Null)
            {
                return default;
            }

            if (typeof(T) == typeof(string))
            {
                return (T)(object)jsonElement.GetString();
            }

            if (typeof(T) == typeof(bool))
            {
                return (T)(object)bool.Parse(jsonElement.GetString());
            }

            if (typeof(T) == typeof(int) || typeof(T).IsEnum)
            {
                return (T)(object)int.Parse(jsonElement.GetString());
            }

            if (typeof(T) == typeof(DateTime))
            {
                return (T)(object)DateTime.Parse(jsonElement.GetString());
            }

            if (typeof(T) == typeof(decimal))
            {
                return (T)(object)decimal.Parse(jsonElement.GetString());
            }

            if (typeof(T) == typeof(Guid))
            {
                return (T)(object)Guid.Parse(jsonElement.GetString());
            }

            T obj;

            if (!_constructor0Cache.TryGetValue(typeof(T), out ConstructorInfo constructorInfo))
            {
                // 0-parameter constructor
                constructorInfo = typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(ci => !ci.GetParameters().Any());
                _constructor0Cache[typeof(T)] = constructorInfo; // Could be null
            }

            if (constructorInfo != null)
            {                
                obj = (T)constructorInfo.Invoke(new object[] { });
            }
            else
            {
                if (!_constructor1Cache.TryGetValue(typeof(T), out constructorInfo))
                {
                    // get first constructor (with parameters this time)
                    constructorInfo = typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.NonPublic).First();
                    _constructor1Cache[typeof(T)] = constructorInfo;
                }

                JsonElement.ObjectEnumerator jElements = jsonElement.EnumerateObject();

                // identify constructor parameters based on the properties available in jsonElement (using 'EndsWith' equality logic as some names might not be fully identical)
                object[] constructorParameters = constructorInfo.GetParameters().Select((ParameterInfo paramInfo) =>
                {
                    JsonProperty jsonProperty = jElements.First((JsonProperty jsonProp) => jsonProp.Name.EndsWith(paramInfo.Name, StringComparison.OrdinalIgnoreCase));
                    return GetPropertyValue(typeof(T).GetProperty(jsonProperty.Name), jsonProperty, ignoreObjects);
                }).ToArray();

                obj = (T)constructorInfo.Invoke(constructorParameters);
            }

            // ObjectConverter $object content, when not a primitive type
            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                return Deserialize<T>(jsonElement.GetString());
            }

            JsonElement _object = default; // ObjectConverter $object content
            foreach (JsonProperty jsonProperty in jsonElement.EnumerateObject())
            {
                // ObjectConverter elements ($object and $type)
                if (jsonProperty.Name == "$object")
                {
                    // this property is always serialized first (see ObjectConverter) so we can safely cache this value
                    _object = jsonProperty.Value;
                }
                else if (jsonProperty.Name == "$type")
                {                    
                    // here, we can reuse _object as it always comes first
                    string type = jsonProperty.Value.GetString();

                    if (!_deserializeJsonCache.TryGetValue(type, out MethodInfo deserializeJson))
                    {
                        deserializeJson = _deserializeJsonGeneric.MakeGenericMethod(Type.GetType(type));
                        _deserializeJsonCache[type] = deserializeJson;
                    }

                    return (T)deserializeJson.Invoke(null, new object[] { _object, ignoreObjects });
                }
                else
                {
                    // 'Normal' object properties
                    PropertyInfo propertyInfo = obj.GetType().GetProperty(jsonProperty.Name);
                    object propertyValue = GetPropertyValue(propertyInfo, jsonProperty, ignoreObjects);
                    Type propType = propertyInfo.PropertyType;

                    // When types do not match, some adjustment is necessary
                    if (propertyValue != null && propertyValue.GetType() != propType)
                    {

                        ConstructorInfo[] cis = null;

                        if (!_constructor1Cache.TryGetValue(propType, out constructorInfo))
                        {
                            cis ??= propType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                            // 1-parameter constructor
                            constructorInfo = cis.FirstOrDefault(ci => ci.GetParameters().Length == 1);
                            _constructor1Cache[propType] = constructorInfo;
                        }

                        if (constructorInfo != null)
                        {
                            propertyValue = constructorInfo.Invoke(new object[] { propertyValue });
                        }
                        else
                        {
                            if (!_constructor0Cache.TryGetValue(propType, out constructorInfo))
                            {
                                cis ??= propType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                                // Used for example with RelatedEntityCollection, we access inner dictionary private field to set the value
                                // 0-parameter constructor
                                constructorInfo = cis.FirstOrDefault(ci => ci.GetParameters().Length == 0);
                                _constructor0Cache[propType] = constructorInfo;
                            }

                            object innerObject = constructorInfo.Invoke(new object[] { });

                            FieldInfo fieldInfo = propType.BaseType.GetField("_innerDictionary", BindingFlags.Instance | BindingFlags.NonPublic);
                            fieldInfo.SetValue(innerObject, propertyValue);
                            propertyValue = innerObject;
                        }
                    }

                    // Check is a setter is available, otherwise use private field
                    if (propertyInfo.CanWrite)
                    {
                        propertyInfo.SetValue(obj, propertyValue);
                    }
                    else
                    {
                        // Xrm classes use this common pattern when setters aren't available (field startting with _ and first letter of property name in lower case)
                        // Use a cache for performance
                        if (!_fieldInfoCache.TryGetValue(propertyValue.GetType(), out FieldInfo fi))
                        {
                            fi = obj.GetType().GetField($"_{propertyInfo.Name.Substring(0, 1).ToLower() + propertyInfo.Name.Substring(1)}", BindingFlags.Instance | BindingFlags.NonPublic);

                            if (fi != null)
                                _fieldInfoCache.Add(propertyValue.GetType(), fi);
                        }

                        if (fi != null)
                            fi.SetValue(obj, propertyValue);
                        else
                        {
                            // Used for OrganizationResponse items, use Results indexer instead
                            PropertyInfo propertyInfo2 = obj.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(wi => wi.Name == "Results");
                            MethodInfo indexerSetter = propertyInfo2.PropertyType.GetMethod("set_Item");
                            object innerObject = propertyInfo2.GetValue(obj, null);

                            // Call indexer
                            indexerSetter.Invoke(innerObject, new object[] { propertyInfo.Name, propertyValue });
                        }
                    }
                }
            }

            return obj;
        }

        public static T[] GetArray<T>(JsonElement ja, bool ignoreObjects)
            where T : class
        {
            // Homogeneous arrays
            if (ja.ValueKind == JsonValueKind.Array)
            {
                return ja.EnumerateArray().Select(je => DeserializeJson<T>(je, ignoreObjects)).ToArray();
            }

            Type[] at = null;

            // Heterogeneous arrays
            foreach (JsonProperty jp in ja.EnumerateObject())
            {
                if (jp.Name == "$arrayTypes")
                {
                    at = jp.Value.EnumerateArray().Select(je => Type.GetType(je.ToString())).ToArray();
                }
                else if (jp.Name == "$values")
                {
                    return jp.Value.EnumerateArray().Select((JsonElement je, int i) => Deserialize(je.ToString(), at[i])).Cast<T>().ToArray();
                }
            }

            return default;
        }

        // Collection C of elements of type T.
        public static C GetCollection<C, T>(JsonElement ja, bool ignoreObjects)
            where C : class, ICollection<T>
        {
            // Create collection instance
            C collection = typeof(C).GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { }, null).Invoke(new object[] { }) as C;

            // Add elements to collection            
            foreach (JsonElement je in ja.EnumerateArray())
            {
                collection.Add(DeserializeJson<T>(je, ignoreObjects));
            }

            return collection;
        }

        public static Dictionary<K, V> GetDictionary<K, V>(JsonElement ja, bool ignoreObjects)
        {
            Dictionary<K, V> dictionary = new();
            Type kvpt = typeof(KeyValuePair<K, V>);
            PropertyInfo kpi = kvpt.GetProperty("Key");
            PropertyInfo vpi = kvpt.GetProperty("Value");

            foreach (JsonElement je in ja.EnumerateArray())
            {
                JsonElement.ObjectEnumerator t = je.EnumerateObject();

                K key = (K)GetPropertyValue(kpi, t.First(jp => jp.Name == "Key"), ignoreObjects);
                V val = (V)GetPropertyValue(vpi, t.First(jp => jp.Name == "Value"), ignoreObjects);

                dictionary[key] = val;
            }

            return dictionary;
        }

        private static object GetPropertyValue(PropertyInfo propertyInfo, JsonProperty jsonProperty, bool ignoreObjects)
        {
            Type propertyType = propertyInfo.PropertyType;

            if (jsonProperty.Value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (!propertyType.IsClass && (propertyType == typeof(bool?) || propertyType == typeof(bool)))
            {
                return jsonProperty.Value.GetBoolean();
            }

            if (propertyType == typeof(string))
            {
                return jsonProperty.Value.GetString();
            }

            if (!propertyType.IsClass)
            {
                if (propertyType == typeof(int?) || propertyType == typeof(int))
                {
                    return jsonProperty.Value.GetInt32();
                }

                if (propertyType == typeof(Guid?) || propertyType == typeof(Guid))
                {
                    return new Guid(jsonProperty.Value.GetString());
                }

                if (propertyType == typeof(DateTime?))
                {
                    return jsonProperty.Value.GetDateTime();
                }

                if (propertyType == typeof(double?) || propertyType == typeof(double))
                {
                    return jsonProperty.Value.GetDouble();
                }

                if (propertyType == typeof(long?) || propertyType == typeof(long))
                {
                    return jsonProperty.Value.GetInt64();
                }

                if (propertyType == typeof(decimal?) || propertyType == typeof(decimal))
                {
                    return jsonProperty.Value.GetDecimal();
                }

                if (propertyType == typeof(short?) || propertyType == typeof(short))
                {
                    return jsonProperty.Value.GetInt16();
                }

                Type underlyingNullablePropertyType = Nullable.GetUnderlyingType(propertyType);
                bool isNullable = underlyingNullablePropertyType != null;

                if (propertyType.IsEnum || (isNullable && underlyingNullablePropertyType.IsEnum))
                {
                    string enumDisplayName = isNullable ? $"{underlyingNullablePropertyType.Name}?" : propertyType.Name;
#if !RELEASE
                    if (!ignoreObjects && !AllowedEnums.Contains(enumDisplayName))
                        throw new Exception($"Invalid Enum {enumDisplayName}");
#endif

                    return Enum.ToObject(isNullable ? underlyingNullablePropertyType : propertyType, jsonProperty.Value.GetInt32());
                }
            }

            if (propertyType.IsArray)
            {
                Type arrayElementType = propertyType.GetElementType();
#if !RELEASE
                if (!AllowedArrays.Contains(arrayElementType.Name))
                    throw new Exception($"Invalid Array {arrayElementType.Name}");
#endif

                return _getArrayGeneric.MakeGenericMethod(arrayElementType).Invoke(null, new object[] { jsonProperty.Value, ignoreObjects });
            }

            // ICollection<T>
            if (!_collectionCache.TryGetValue(propertyType, out MethodInfo getCollection))
            {               
                Type iCollectionType = propertyType.GetInterfaces().FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ICollection<>));

                if (iCollectionType != null)
                {
#if !RELEASE
                    string typeDisplayName = ObjectConverter.GetTypeDisplayName(propertyType);
                    if (!ignoreObjects && !AllowedCollections.Contains(typeDisplayName))
                        throw new Exception($"Invalid Collection {typeDisplayName}");
#endif

                    Type elementType = iCollectionType.GetGenericArguments()[0];
                    getCollection = _getCollectionGeneric.MakeGenericMethod(propertyType, elementType);
                    _collectionCache[propertyType] = getCollection;

                    return getCollection.Invoke(null, new object[] { jsonProperty.Value, ignoreObjects });
                }

                _collectionCache[propertyType] = null;
            }
            else if (getCollection != null)
            {
                return getCollection.Invoke(null, new object[] { jsonProperty.Value, ignoreObjects });
            }

            // IEnumerable<KeyValuePair<TKey,TValue>> like ParameterCollection : DataCollection<string, object> : IEnumerable<KeyValuePair<string, object>>
            if (!_dictionaryCache.TryGetValue(propertyType, out MethodInfo getDictionary))
            {                
                Type iEnumerableType = propertyType.GetInterfaces().FirstOrDefault(t =>
                {
                    if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    {
                        Type[] gArgs = t.GetGenericArguments();

                        if (gArgs.Length == 1)
                        {
                            Type g0 = gArgs[0];
                            return g0.IsGenericType && g0.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);
                        }
                    }
                    return false;
                });

                if (iEnumerableType != null)
                {
#if !RELEASE
                    string typeDisplayName = ObjectConverter.GetTypeDisplayName(propertyType);
                    if (!ignoreObjects && !AllowedDictionaries.Contains(typeDisplayName))
                        throw new Exception($"Invalid Dictionary {typeDisplayName}");
#endif

                    Type keyValuePairType = iEnumerableType.GetGenericArguments()[0];
                    Type[] keyAndValueTypes = keyValuePairType.GetGenericArguments();
                    getDictionary = _getDictionaryGeneric.MakeGenericMethod(keyAndValueTypes);
                    _dictionaryCache[propertyType] = getDictionary;

                    return getDictionary.Invoke(null, new object[] { jsonProperty.Value, ignoreObjects });
                }

                _dictionaryCache[propertyType] = null;
            }
            else if (getDictionary != null)
            {
                return getDictionary.Invoke(null, new object[] { jsonProperty.Value, ignoreObjects });
            }

            if (propertyType.IsClass)
            {
#if !RELEASE
                if (!ignoreObjects && !AllowedClasses.Contains(propertyType.Name))
                    throw new Exception($"Invalid Class {propertyType.Name}");
#endif

                return _deserializeJsonGeneric.MakeGenericMethod(propertyType).Invoke(null, new object[] { jsonProperty.Value, ignoreObjects });
            }

            throw new Exception($"Unknown type {propertyType.Name}");
        }

#if !RELEASE
        private static HashSet<string> AllowedArrays = new()
        {
            "AttributeMetadata",
            "EntityKeyMetadata",
            "EntitySetting",
            "ManyToManyRelationshipMetadata",
            "OneToManyRelationshipMetadata",
            "SecurityPrivilegeMetadata",
            "String"
        };

        private static HashSet<string> AllowedCollections = new()
        {
            "Collection<RelationshipAttribute>",
            "DataCollection<ConditionExpression>",
            "DataCollection<Entity>",
            "DataCollection<FilterExpression>",
            "DataCollection<LinkEntity>",
            "DataCollection<OrderExpression>",
            "DataCollection<String>",
            "DataCollection<XrmAttributeExpression>",
            "LocalizedLabelCollection",
            "OptionMetadataCollection"
        };

        private static HashSet<string> AllowedDictionaries = new()
        {
            "AttributeCollection",
            "FormattedValueCollection",
            "KeyAttributeCollection",
            "ParameterCollection",
            "RelatedEntityCollection"
        };

        private static HashSet<string> AllowedClasses = new()
        {
            "AssociatedMenuConfiguration",
            "AttributeCollection",
            "AttributeRequiredLevelManagedProperty",
            "AttributeTypeDisplayName",
            "BooleanManagedProperty",
            "BooleanOptionSetMetadata",
            "CascadeConfiguration",
            "ColumnSet",
            "DateTimeBehavior",
            "EntityMetadata",
            "EntityReference",
            "ExtensionDataObject",
            "FilterExpression",
            "Label",
            "LocalizedLabel",
            "MemoFormatName",
            "Object",
            "OptionMetadata",
            "OptionSetMetadata",
            "PagingInfo",
            "StringFormatName"
        };

        private static HashSet<string> AllowedEnums = new()
        {
            "AssociatedMenuBehavior?",
            "AssociatedMenuGroup?",
            "AttributeRequiredLevel",
            "AttributeTypeCode",
            "AttributeTypeCode?",
            "CascadeType?",
            "DateTimeFormat?",
            "EntityFilters",
            "EntityKeyIndexStatus",
            "ImeMode?",
            "IntegerFormat?",
            "LogicalOperator",
            "LookupFormat?",
            "OptionSetType?",
            "OwnershipTypes?",
            "PrivilegeType",
            "RelationshipType",
            "SecurityTypes?",
            "StringFormat?",
        };
#endif
    }
}
