//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    internal class JsonNormalizer
    {
        public static string Normalize(string jsonStr, (string, Type)[] arrayKeys)
        {
            JsonElement je = JsonDocument.Parse(jsonStr).RootElement;
            return new JsonNormalizer(arrayKeys).Normalize(je);
        }

        private (string, Type)[] _arrayKeys;

        internal JsonNormalizer((string, Type)[] arrayKeys)
        {
            _arrayKeys = arrayKeys;
        }

        public string Normalize(JsonElement je)
        {
            var ms = new MemoryStream();
            JsonWriterOptions opts = new JsonWriterOptions
            {
                Indented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            using (var writer = new Utf8JsonWriter(ms, opts))
            {
                Write(je, writer);
            }

            var bytes = ms.ToArray();
            var str = Encoding.UTF8.GetString(bytes);
            return str;
        }

        private void Write(JsonElement je, Utf8JsonWriter writer)
        {
            switch (je.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();

                    foreach (JsonProperty x in je.EnumerateObject().OrderBy(prop => prop.Name))
                    {
                        writer.WritePropertyName(x.Name);
                        Write(x.Value, writer);
                    }

                    writer.WriteEndObject();
                    break;

                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    JsonElement[] array = je.EnumerateArray().ToArray();
                    if (array.Count() != 0)
                    {
                        JsonElement jf = array.First();
                        bool ordered = false;

                        foreach ((string propName, Type type) ak in _arrayKeys)
                        {
                            if (ordered |= jf.TryGetProperty(ak.propName, out JsonElement _))
                            {
                                array = ak.type == typeof(int) ? array.OrderBy(j => j.GetProperty(ak.propName).GetInt32()).ToArray()
                                                               : array.OrderBy(j => j.GetProperty(ak.propName).GetString()).ToArray();
                                break;
                            }
                        }

                        if (!ordered)
                            throw new Exception();
                    }

                    foreach (JsonElement x in array)
                    {
                        Write(x, writer);
                    }
                    writer.WriteEndArray();
                    break;

                case JsonValueKind.Number:
                    writer.WriteNumberValue(je.GetDouble());
                    break;

                case JsonValueKind.String:
                    writer.WriteStringValue(je.GetString());
                    break;

                case JsonValueKind.Null:
                    writer.WriteNullValue();
                    break;

                case JsonValueKind.True:
                    writer.WriteBooleanValue(true);
                    break;

                case JsonValueKind.False:
                    writer.WriteBooleanValue(false);
                    break;

                default:
                    throw new NotImplementedException($"Kind: {je.ValueKind}");
            }
        }
    }
}
