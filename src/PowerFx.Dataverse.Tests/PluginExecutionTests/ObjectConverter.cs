//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class ObjectConverter : JsonConverter<object>
    {
        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            string str = value is Guid || value is int || value is bool || value is double || value is float || value is decimal || value is long || value is short ||
                         value is byte || value is sbyte || value is uint || value is ulong || value is ushort
                       ? value.ToString()
                       : value is string s
                       ? s
                       : value is DateTime dt
                       ? dt.ToString("o")
                       : value.GetType().IsEnum
                       ? ((int)value).ToString()
                       : $"{SerializeObject(value, options)}";

            writer.WriteStartObject();
            writer.WriteString("$object", str);
            writer.WriteString("$type", value.GetType().AssemblyQualifiedName); // Will be used to recreate the object
            writer.WriteEndObject();
        }

        public static string SerializeObject(object value, JsonSerializerOptions options)
        {
            // Equivalent of ((JsonConverter<T>)options.GetConverter(typeof(T))).Write(writer, item, options)            
            using MemoryStream ms = new();
            using Utf8JsonWriter writer = new(ms);
            JsonConverter conv = options.GetConverter(value.GetType());
            MethodInfo mi = conv.GetType().GetMethod("Write");
            mi.Invoke(conv, new object[] { writer, value, options });
            writer.Flush();
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        public static string GetTypeDisplayName(Type type)
        {
            return $"{type.Name.Split('`')[0]}{(type.IsGenericType ? "<" : "")}{string.Join(", ", type.GenericTypeArguments.Select(f => f.Name))}{(type.IsGenericType ? ">" : "")}";
        }
    }
}
