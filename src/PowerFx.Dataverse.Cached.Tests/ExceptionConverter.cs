//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.PowerFx.Dataverse.Cached.Tests
{ 
    public class ExceptionConverter : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsSubclassOf(typeof(Exception));
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            return new ExceptionConverterInner();
        }

        private class ExceptionConverterInner : JsonConverter<Exception>
        {
            public override Exception Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotImplementedException();
            }

            public override void Write(Utf8JsonWriter writer, Exception exception, JsonSerializerOptions options)
            {                
                string exceptionTypeDisplayName = ObjectConverter.GetTypeDisplayName(exception.GetType());

                writer.WriteStartObject();
                writer.WriteString("ExceptionType", exceptionTypeDisplayName);
                writer.WriteString("Message", exception.Message);
                writer.WriteString("StackTrace", exception.StackTrace);
                writer.WriteEndObject();
            }
        }
    }
}
