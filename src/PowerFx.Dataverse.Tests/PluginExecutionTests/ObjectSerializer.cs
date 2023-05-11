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
        private static HashSet<string> AllowedArrays = new HashSet<string>() { "AttributeMetadata", "EntityKeyMetadata", "ManyToManyRelationshipMetadata", "OneToManyRelationshipMetadata", "SecurityPrivilegeMetadata", "EntitySetting", "String" };
        private static HashSet<string> AllowedCollections = new HashSet<string>() { "LocalizedLabelCollection", "Collection<RelationshipAttribute>", "DataCollection<LinkEntity>", "DataCollection<ConditionExpression>", "DataCollection<FilterExpression>",
            "DataCollection<OrderExpression>", "DataCollection<String>", "DataCollection<XrmAttributeExpression>", "OptionMetadataCollection", "DataCollection<Entity>" };
        private static HashSet<string> AllowedDictionaries = new HashSet<string>() { "AttributeCollection", "ParameterCollection", "KeyAttributeCollection", "FormattedValueCollection", "RelatedEntityCollection" };
        private static HashSet<string> AllowedClasses = new HashSet<string>() { "Label", "LocalizedLabel", "BooleanManagedProperty", "ExtensionDataObject", "AttributeTypeDisplayName", "AttributeRequiredLevelManagedProperty", "AssociatedMenuConfiguration",
            "CascadeConfiguration", "AttributeCollection", "Object", "PagingInfo", "FilterExpression", "ColumnSet", "EntityMetadata", "OptionSetMetadata", "StringFormatName", "MemoFormatName", "DateTimeBehavior", "BooleanOptionSetMetadata",
            "OptionMetadata", "EntityReference" };
        private static HashSet<string> AllowedEnums = new HashSet<string>() { "AttributeTypeCode?", "OwnershipTypes?", "RelationshipType", "SecurityTypes?", "PrivilegeType", "AttributeRequiredLevel", "AssociatedMenuBehavior?", "AssociatedMenuGroup?",
            "CascadeType?", "EntityFilters", "LogicalOperator", "OptionSetType?", "StringFormat?", "ImeMode?", "IntegerFormat?", "LookupFormat?", "DateTimeFormat?", "EntityKeyIndexStatus", "AttributeTypeCode" };

        public static JsonSerializerOptions GetJsonSerializerOptions(bool writeIndented)
        {
            JsonSerializerOptions jso = new JsonSerializerOptions() { WriteIndented = writeIndented };
            jso.Converters.Add(new ObjectConverter());
            jso.Converters.Add(new ArrayConverter());
            jso.Converters.Add(new DictionaryConverter());
            jso.Converters.Add(new ExceptionConverter());
            return jso;
        }

        public static string Serialize(object obj)
        {
            return JsonSerializer.Serialize(obj, obj.GetType(), GetJsonSerializerOptions(true));
        }

        public static object Deserialize(string str, Type type)
        {
            MethodInfo genericDeserializeMethod = typeof(ObjectSerializer).GetMethod("Deserialize", 1 /* genericParameterCount */, BindingFlags.Static | BindingFlags.Public, null, new Type[] { typeof(string), typeof(bool) }, null);
            MethodInfo deserializeMethod = genericDeserializeMethod.MakeGenericMethod(type);

            return deserializeMethod.Invoke(null, new object[] { str, false });
        }

        public static T Deserialize<T>(string str, bool ignoreObjects = false)
        {
            return DeserializeJson<T>(JsonDocument.Parse(str).RootElement, ignoreObjects);
        }

        public static T DeserializeJson<T>(JsonElement je, bool ignoreObjects = false)
        {
            if (je.ValueKind == JsonValueKind.Null)
                return default;
            if (typeof(T) == typeof(int) || typeof(T).IsEnum)
                return (T)(object)int.Parse(je.GetString());
            if (typeof(T) == typeof(bool))
                return (T)(object)bool.Parse(je.GetString());
            if (typeof(T) == typeof(string))
                return (T)(object)je.GetString();
            if (typeof(T) == typeof(decimal))
                return (T)(object)decimal.Parse(je.GetString());
            if (typeof(T) == typeof(Guid))
                return (T)(object)Guid.Parse(je.GetString());
            if (typeof(T) == typeof(DateTime))
                return (T)(object)DateTime.Parse(je.GetString());

            T o;
            ConstructorInfo ci = typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(ci => !ci.GetParameters().Any());
            if (ci != null)
                o = (T)ci.Invoke(new object[] { });
            else
            {
                ci = typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).First();
                object[] prms = ci.GetParameters().Select((ParameterInfo pi2) =>
                {
                    JsonProperty jp3 = je.EnumerateObject().First((JsonProperty jp2) => jp2.Name.EndsWith(pi2.Name, StringComparison.OrdinalIgnoreCase));
                    return GetValue(typeof(T).GetProperty(jp3.Name), jp3, ignoreObjects);
                }).ToArray();
                o = (T)ci.Invoke(prms);
            }

            JsonElement _object = default;
            if (je.ValueKind == JsonValueKind.String)
            {
                string[] strParts = je.GetString().Split("|");
                //return (T)Deserialize(strParts[1], Type.GetType(strParts[0]));
                return Deserialize<T>(strParts[0]);
            }
            foreach (JsonProperty jp in je.EnumerateObject())
            {
                if (jp.Name == "$object")
                    _object = jp.Value;
                else if (jp.Name == "$type")
                {
                    Type t = Type.GetType(jp.Value.GetString());
                    var getObject = typeof(ObjectSerializer).GetMethod("DeserializeJson", BindingFlags.Static | BindingFlags.Public);
                    return (dynamic)getObject.MakeGenericMethod(t).Invoke(null, new object[] { _object, ignoreObjects });
                }
                else
                {
                    PropertyInfo pi = o.GetType().GetProperty(jp.Name);
                    object val = GetValue(pi, jp, ignoreObjects);
                    //object v0 = val;
                    RelatedEntityCollection rec = null;

                    if (val != null && val.GetType() != pi.PropertyType)
                    {
                        ci = pi.PropertyType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(ci => ci.GetParameters().Length == 1);
                        val = ci.Invoke(new object[] { val });
                    }

                    if (pi.CanWrite)
                    {
                        pi.SetValue(o, val);
                    }
                    else
                    {
                        FieldInfo fi = o.GetType().GetField($"_{pi.Name.Substring(0, 1).ToLower() + pi.Name.Substring(1)}", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (fi != null)
                        {
                            fi.SetValue(o, val);
                        }
                        else
                        {
                            PropertyInfo pi2 = o.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(wi => wi.Name == "Results");
                            MethodInfo mi2 = pi2.PropertyType.GetMethod("set_Item");

                            object o2 = pi2.GetValue(o, null);
                            mi2.Invoke(o2, new object[] { pi.Name, val });
                        }
                    }
                }
            }

            return o;
        }

        public static T[] GetArray<T>(JsonElement ja, bool ignoreObjects)
            where T : class
        {
            if (ja.ValueKind == JsonValueKind.Array)
                return ja.EnumerateArray().Select(je => DeserializeJson<T>(je, ignoreObjects)).ToArray();

            Type[] at = null;
            foreach (JsonProperty jp in ja.EnumerateObject())
            {
                if (jp.Name == "$arrayTypes")
                {
                    at = jp.Value.EnumerateArray().Select(je => Type.GetType(je.ToString())).ToArray();
                }
                if (jp.Name == "$values")
                {
                    return jp.Value.EnumerateArray().Select((JsonElement je, int i) => Deserialize(je.ToString(), at[i])).Cast<T>().ToArray();
                }
            }

            return null;
        }

        // Collection C of elements of type T.
        public static C GetCollection<C, T>(JsonElement ja, bool ignoreObjects)
            where C : class, ICollection<T>
        {
            var col = typeof(C).GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { }, null).Invoke(new object[] { }) as C;
            Array.ForEach(ja.EnumerateArray().ToArray(), (JsonElement je) => col.Add(DeserializeJson<T>(je, ignoreObjects)));
            return col;
        }

        public static Dictionary<K, V> GetDictionary<K, V>(JsonElement ja, bool ignoreObjects)
        {
            var dic = new Dictionary<K, V>();
            Array.ForEach(ja.EnumerateArray().ToArray(), (JsonElement je) => dic.Add((K)GetValue(typeof(KeyValuePair<K, V>).GetProperty("Key"), je.EnumerateObject().First(jp => jp.Name == "Key"), ignoreObjects),
                                                                                     (V)GetValue(typeof(KeyValuePair<K, V>).GetProperty("Value"), je.EnumerateObject().First(jp => jp.Name == "Value"), ignoreObjects)));
            return dic;
        }

        private static object GetValue(PropertyInfo pi, JsonProperty jp, bool ignoreObjects)
        {
            bool b = jp.Value.ValueKind == JsonValueKind.Null;
            Type t = pi.PropertyType;
            Type ti = null;
            bool isNullable = (ti = Nullable.GetUnderlyingType(t)) != null;

            if (b) return null;

            if (t == typeof(int?) || t == typeof(int)) return jp.Value.GetInt32();
            else if (t == typeof(bool?) || t == typeof(bool)) return jp.Value.GetBoolean();
            else if (t == typeof(double?) || t == typeof(double)) return jp.Value.GetDouble();
            else if (t == typeof(short?) || t == typeof(short)) return jp.Value.GetInt16();
            else if (t == typeof(long?) || t == typeof(long)) return jp.Value.GetInt64();
            else if (t == typeof(decimal?) || t == typeof(decimal)) return jp.Value.GetDecimal();
            else if (t == typeof(Guid?) || t == typeof(Guid)) return new Guid(jp.Value.GetString());
            else if (t == typeof(DateTime?)) return jp.Value.GetDateTime();
            else if (t == typeof(string)) return jp.Value.GetString();
            else if (t.IsArray)
            {
                Type at = t.GetElementType();
                if (!AllowedArrays.Contains(at.Name)) throw new Exception($"Invalid Array {at.Name}");
                var getArray = typeof(ObjectSerializer).GetMethod("GetArray", BindingFlags.Static | BindingFlags.Public);
                return getArray.MakeGenericMethod(at).Invoke(null, new object[] { jp.Value, ignoreObjects });
            }
            else if (t.GetInterfaces().Any(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>) && t.GetGenericArguments().Length == 1 &&
                                                t.GetGenericArguments().First().IsGenericType && t.GetGenericArguments().First().GetGenericTypeDefinition() == typeof(KeyValuePair<,>)))
            {
                string tName = $"{t.Name}{(t.IsGenericType ? "<" : "")}{string.Join(", ", t.GenericTypeArguments.Select(f => f.Name))}{(t.IsGenericType ? ">" : "")}";
                if (!ignoreObjects && !AllowedDictionaries.Contains(tName)) throw new Exception($"Invalid Dictionary {tName}");

                Type iEnumerable = t.GetInterfaces().FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>));
                Type elementType = iEnumerable.GetGenericArguments().First();
                Type[] innerTypes = elementType.GetGenericArguments();
                var getDictionary = typeof(ObjectSerializer).GetMethod("GetDictionary", BindingFlags.Static | BindingFlags.Public);
                return getDictionary.MakeGenericMethod(innerTypes).Invoke(null, new object[] { jp.Value, ignoreObjects });
            }
            else if (t.GetInterfaces().Any(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ICollection<>)))
            {
                string tName = ObjectConverter.GetTypeName(t);
                if (!ignoreObjects && !AllowedCollections.Contains(tName)) throw new Exception($"Invalid Collection {tName}");

                Type iCollection = t.GetInterfaces().FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ICollection<>));
                Type elementType = iCollection.GetGenericArguments().First();
                var getCollection = typeof(ObjectSerializer).GetMethod("GetCollection", BindingFlags.Static | BindingFlags.Public);
                return getCollection.MakeGenericMethod(t, elementType).Invoke(null, new object[] { jp.Value, ignoreObjects });
            }
            else if (t.IsClass)
            {
                if (!ignoreObjects && !AllowedClasses.Contains(t.Name)) throw new Exception($"Invalid Class {t.Name}");

                var getObject = typeof(ObjectSerializer).GetMethod("DeserializeJson", BindingFlags.Static | BindingFlags.Public);
                return getObject.MakeGenericMethod(t).Invoke(null, new object[] { jp.Value, ignoreObjects });
            }
            else if (t.IsEnum || (isNullable && ti.IsEnum))
            {
                string eName = isNullable ? $"{ti.Name}?" : t.Name;
                if (!ignoreObjects && !AllowedEnums.Contains(eName)) throw new Exception($"Invalid Enum {eName}");

                return Enum.ToObject(isNullable ? ti : t, jp.Value.GetInt32());
            }
            else throw new Exception($"Unknown type {t.Name}");
        }
    }
}
