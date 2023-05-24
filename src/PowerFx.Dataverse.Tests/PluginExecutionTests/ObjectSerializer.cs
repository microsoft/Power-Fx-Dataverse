//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public static class ObjectSerializer
    {
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

        public static object Deserialize(string jsonString, Type type)
        {
            MethodInfo genericDeserializeMethod = typeof(ObjectSerializer).GetMethod("Deserialize", 1 /* genericParameterCount */, BindingFlags.Static | BindingFlags.Public, null, new Type[] { typeof(string), typeof(bool) }, null);
            MethodInfo deserializeMethod = genericDeserializeMethod.MakeGenericMethod(type);

            return deserializeMethod.Invoke(null, new object[] { jsonString, false });
        }

        public static T Deserialize<T>(string str, bool ignoreObjects = false)
        {
            return DeserializeJson<T>(JsonDocument.Parse(str).RootElement, ignoreObjects);
        }

        public static T DeserializeJson<T>(JsonElement jsonElement, bool ignoreObjects = false)
        {
            if (jsonElement.ValueKind == JsonValueKind.Null)
                return default;

            if (typeof(T) == typeof(int) || typeof(T).IsEnum)
                return (T)(object)int.Parse(jsonElement.GetString());

            if (typeof(T) == typeof(bool))
                return (T)(object)bool.Parse(jsonElement.GetString());

            if (typeof(T) == typeof(string))
                return (T)(object)jsonElement.GetString();

            if (typeof(T) == typeof(decimal))
                return (T)(object)decimal.Parse(jsonElement.GetString());

            if (typeof(T) == typeof(Guid))
                return (T)(object)Guid.Parse(jsonElement.GetString());

            if (typeof(T) == typeof(DateTime))
                return (T)(object)DateTime.Parse(jsonElement.GetString());

            T obj;

            // 0-parameter constructor
            ConstructorInfo constructorInfo = typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(ci => !ci.GetParameters().Any());

            if (constructorInfo != null)
                obj = (T)constructorInfo.Invoke(new object[] { });
            else
            {
                // get first constructor (with parameters)
                constructorInfo = typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).First();

                // identify constructor parameters based on the properties available in jsonElement (using 'EndsWith' equality logic as some names might not be fully identical)
                object[] constructorParameters = constructorInfo.GetParameters().Select((ParameterInfo paramInfo) =>
                {
                    JsonProperty jsonProperty = jsonElement.EnumerateObject().First((JsonProperty jsonProp) => jsonProp.Name.EndsWith(paramInfo.Name, StringComparison.OrdinalIgnoreCase));
                    return GetPropertyValue(typeof(T).GetProperty(jsonProperty.Name), jsonProperty, ignoreObjects);
                }).ToArray();

                obj = (T)constructorInfo.Invoke(constructorParameters);
            }

            // ObjectConverter $object content, when not a primitive type
            if (jsonElement.ValueKind == JsonValueKind.String)
                return Deserialize<T>(jsonElement.GetString());

            JsonElement _object = default; // ObjectConverter $object content
            foreach (JsonProperty jsonProperty in jsonElement.EnumerateObject())
            {
                // ObjectConverter elemments ($object and $type)
                if (jsonProperty.Name == "$object")
                    _object = jsonProperty.Value;
                else if (jsonProperty.Name == "$type")
                {
                    var getObject = typeof(ObjectSerializer).GetMethod("DeserializeJson", BindingFlags.Static | BindingFlags.Public);
                    return (T)getObject.MakeGenericMethod(Type.GetType(jsonProperty.Value.GetString())).Invoke(null, new object[] { _object, ignoreObjects });
                }
                else
                {
                    // 'Normal' object properties
                    PropertyInfo propertyInfo = obj.GetType().GetProperty(jsonProperty.Name);
                    object propertyValue = GetPropertyValue(propertyInfo, jsonProperty, ignoreObjects);

                    // When types do not match, some adjustment is necessary
                    if (propertyValue != null && propertyValue.GetType() != propertyInfo.PropertyType)
                    {
                        // Get all constructors
                        ConstructorInfo[] cis = propertyInfo.PropertyType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                        // 1-parameter constructor
                        constructorInfo = cis.FirstOrDefault(ci => ci.GetParameters().Length == 1);

                        if (constructorInfo != null)
                            propertyValue = constructorInfo.Invoke(new object[] { propertyValue });
                        else
                        {
                            // Used for example with RelatedEntityCollection, we access inner dictionary private field to set the value
                            // 0-parameter constructor
                            constructorInfo = cis.FirstOrDefault(ci => ci.GetParameters().Length == 0);
                            object innerObject = constructorInfo.Invoke(new object[] { });

                            FieldInfo fieldInfo = propertyInfo.PropertyType.BaseType.GetField("_innerDictionary", BindingFlags.Instance | BindingFlags.NonPublic);
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
                        FieldInfo fi = obj.GetType().GetField($"_{propertyInfo.Name.Substring(0, 1).ToLower() + propertyInfo.Name.Substring(1)}", BindingFlags.Instance | BindingFlags.NonPublic);

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
                return ja.EnumerateArray().Select(je => DeserializeJson<T>(je, ignoreObjects)).ToArray();

            Type[] at = null;

            // Heterogeneous arrays
            foreach (JsonProperty jp in ja.EnumerateObject())
                if (jp.Name == "$arrayTypes")
                    at = jp.Value.EnumerateArray().Select(je => Type.GetType(je.ToString())).ToArray();
                else if (jp.Name == "$values")
                    return jp.Value.EnumerateArray().Select((JsonElement je, int i) => Deserialize(je.ToString(), at[i])).Cast<T>().ToArray();

            return default;
        }

        // Collection C of elements of type T.
        public static C GetCollection<C, T>(JsonElement ja, bool ignoreObjects)
            where C : class, ICollection<T>
        {
            // Create collection instance
            C collection = typeof(C).GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { }, null).Invoke(new object[] { }) as C;

            // Add elements to collection
            Array.ForEach(ja.EnumerateArray().ToArray(), (JsonElement je) => collection.Add(DeserializeJson<T>(je, ignoreObjects)));

            return collection;
        }

        public static Dictionary<K, V> GetDictionary<K, V>(JsonElement ja, bool ignoreObjects)
        {
            Dictionary<K, V> dictionary = new();
            Array.ForEach(ja.EnumerateArray().ToArray(), (JsonElement je) => dictionary.Add((K)GetPropertyValue(typeof(KeyValuePair<K, V>).GetProperty("Key"), je.EnumerateObject().First(jp => jp.Name == "Key"), ignoreObjects),
                                                                                            (V)GetPropertyValue(typeof(KeyValuePair<K, V>).GetProperty("Value"), je.EnumerateObject().First(jp => jp.Name == "Value"), ignoreObjects)));
            return dictionary;
        }

        private static object GetPropertyValue(PropertyInfo propertyInfo, JsonProperty jsonProperty, bool ignoreObjects)
        {
            Type propertyType = propertyInfo.PropertyType;

            if (jsonProperty.Value.ValueKind == JsonValueKind.Null)
                return null;

            if (propertyType == typeof(string))
                return jsonProperty.Value.GetString();

            if (propertyType == typeof(Guid?) || propertyType == typeof(Guid))
                return new Guid(jsonProperty.Value.GetString());

            if (propertyType == typeof(bool?) || propertyType == typeof(bool))
                return jsonProperty.Value.GetBoolean();

            if (propertyType == typeof(int?) || propertyType == typeof(int))
                return jsonProperty.Value.GetInt32();

            if (propertyType == typeof(double?) || propertyType == typeof(double))
                return jsonProperty.Value.GetDouble();

            if (propertyType == typeof(short?) || propertyType == typeof(short))
                return jsonProperty.Value.GetInt16();

            if (propertyType == typeof(long?) || propertyType == typeof(long))
                return jsonProperty.Value.GetInt64();

            if (propertyType == typeof(decimal?) || propertyType == typeof(decimal))
                return jsonProperty.Value.GetDecimal();

            if (propertyType == typeof(DateTime?))
                return jsonProperty.Value.GetDateTime();

            if (propertyType.IsArray)
            {
                Type arrayElementType = propertyType.GetElementType();
                if (!AllowedArrays.Contains(arrayElementType.Name))
                    throw new Exception($"Invalid Array {arrayElementType.Name}");

                var getArray = typeof(ObjectSerializer).GetMethod("GetArray", BindingFlags.Static | BindingFlags.Public);
                return getArray.MakeGenericMethod(arrayElementType).Invoke(null, new object[] { jsonProperty.Value, ignoreObjects });
            }

            // IEnumerable<KeyValuePair<TKey,TValue>> like ParameterCollection : DataCollection<string, object> : IEnumerable<KeyValuePair<string, object>>
            if (propertyType.GetInterfaces().Any(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>) && t.GetGenericArguments().Length == 1 &&
                                                      t.GetGenericArguments().First().IsGenericType && t.GetGenericArguments().First().GetGenericTypeDefinition() == typeof(KeyValuePair<,>)))
            {
                string typeDisplayName = ObjectConverter.GetTypeDisplayName(propertyType);
                if (!ignoreObjects && !AllowedDictionaries.Contains(typeDisplayName))
                    throw new Exception($"Invalid Dictionary {typeDisplayName}");

                Type iEnumerableType = propertyType.GetInterfaces().FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>));
                Type keyValuePairType = iEnumerableType.GetGenericArguments().First();
                Type[] keyAndValueTypes = keyValuePairType.GetGenericArguments();

                MethodInfo getDictionary = typeof(ObjectSerializer).GetMethod("GetDictionary", BindingFlags.Static | BindingFlags.Public);
                return getDictionary.MakeGenericMethod(keyAndValueTypes).Invoke(null, new object[] { jsonProperty.Value, ignoreObjects });
            }

            // ICollection<T>
            if (propertyType.GetInterfaces().Any(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ICollection<>)))
            {
                string typeDisplayName = ObjectConverter.GetTypeDisplayName(propertyType);
                if (!ignoreObjects && !AllowedCollections.Contains(typeDisplayName))
                    throw new Exception($"Invalid Collection {typeDisplayName}");

                Type iCollectionType = propertyType.GetInterfaces().FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ICollection<>));
                Type elementType = iCollectionType.GetGenericArguments().First();

                MethodInfo getCollection = typeof(ObjectSerializer).GetMethod("GetCollection", BindingFlags.Static | BindingFlags.Public);
                return getCollection.MakeGenericMethod(propertyType, elementType).Invoke(null, new object[] { jsonProperty.Value, ignoreObjects });
            }

            if (propertyType.IsClass)
            {
                if (!ignoreObjects && !AllowedClasses.Contains(propertyType.Name))
                    throw new Exception($"Invalid Class {propertyType.Name}");

                var getObject = typeof(ObjectSerializer).GetMethod("DeserializeJson", BindingFlags.Static | BindingFlags.Public);
                return getObject.MakeGenericMethod(propertyType).Invoke(null, new object[] { jsonProperty.Value, ignoreObjects });
            }

            Type underlyingNullablePropertyType = null;
            bool isNullable = (underlyingNullablePropertyType = Nullable.GetUnderlyingType(propertyType)) != null;

            if (propertyType.IsEnum || (isNullable && underlyingNullablePropertyType.IsEnum))
            {
                string enumDisplayName = isNullable ? $"{underlyingNullablePropertyType.Name}?" : propertyType.Name;
                if (!ignoreObjects && !AllowedEnums.Contains(enumDisplayName))
                    throw new Exception($"Invalid Enum {enumDisplayName}");

                return Enum.ToObject(isNullable ? underlyingNullablePropertyType : propertyType, jsonProperty.Value.GetInt32());
            }

            throw new Exception($"Unknown type {propertyType.Name}");
        }
    }
}
