//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.Xrm.Sdk;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class OrganizationResponseConverter : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsSubclassOf(typeof(OrganizationResponse));
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            return new OrganizationResponseConverterInner();
        }

        private class OrganizationResponseConverterInner : JsonConverter<OrganizationResponse>
        {
            public override OrganizationResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotImplementedException();
            }
            public override void Write(Utf8JsonWriter writer, OrganizationResponse organizationResponse, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                
                writer.WriteEndObject();
            }
        }
    }
}
