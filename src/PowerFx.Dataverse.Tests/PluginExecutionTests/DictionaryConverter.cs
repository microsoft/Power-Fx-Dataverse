//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class DictionaryConverter : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Dictionary<,>);
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            Type keyType = typeToConvert.GetGenericArguments()[0];
            Type valueType = typeToConvert.GetGenericArguments()[1];
            Type converterType = typeof(DictionaryConverterInner<,>).MakeGenericType(new Type[] { keyType, valueType });
            return (JsonConverter)Activator.CreateInstance(converterType);
        }

        private class DictionaryConverterInner<K, V> : JsonConverter<Dictionary<K, V>>
        {
            public override Dictionary<K, V> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotImplementedException();
            }

            public override void Write(Utf8JsonWriter writer, Dictionary<K, V> value, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                foreach (KeyValuePair<K, V> kvp in value)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("Key");
                    ((JsonConverter<K>)options.GetConverter(typeof(K))).Write(writer, kvp.Key, options);
                    writer.WritePropertyName("Value");
                    ((JsonConverter<V>)options.GetConverter(typeof(V))).Write(writer, kvp.Value, options);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
        }
    }
}
