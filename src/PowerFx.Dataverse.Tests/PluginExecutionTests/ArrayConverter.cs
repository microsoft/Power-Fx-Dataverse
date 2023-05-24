//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class ArrayConverter : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsArray;            
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            Type elementType = typeToConvert.GetElementType();            
            Type converterType = typeof(ArrayConverterInner<>).MakeGenericType(new Type[] { elementType });
            return (JsonConverter)Activator.CreateInstance(converterType);
        }

        private class ArrayConverterInner<T> : JsonConverter<T[]>
        {
            public override T[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotImplementedException();
            }

            public override void Write(Utf8JsonWriter writer, T[] value, JsonSerializerOptions options)
            {
                if (value.All(v => v.GetType() == typeof(T)))
                {
                    writer.WriteStartArray();

                    foreach (T item in value)                                            
                        writer.WriteRawValue(ObjectConverter.SerializeObject(item, options));

                    writer.WriteEndArray();
                }
                else
                {
                    // Heterogeneous Array
                    writer.WriteStartObject();
                    writer.WriteStartArray("$arrayTypes");

                    foreach (T item in value)                    
                        writer.WriteStringValue(item.GetType().AssemblyQualifiedName);
                    
                    writer.WriteEndArray();
                    writer.WriteStartArray("$values");
                    
                    foreach (T item in value)                                            
                        writer.WriteRawValue(ObjectConverter.SerializeObject(item, options));                     
                    
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
            }
        }
    }
}

