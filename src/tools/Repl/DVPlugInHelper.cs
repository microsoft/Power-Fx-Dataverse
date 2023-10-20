// <copyright file="Program.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Interfaces;
using Microsoft.OpenApi.Models;
using Microsoft.PowerFx.Types;

namespace Repl
{
    internal static class DVPlugInHelper
    {
        public static string ToOpenApiSchemaType(this CustomApiParamType typeCode)
        {
            return typeCode switch
            {
                CustomApiParamType.Float => "number",
                CustomApiParamType.Integer => "number",
                CustomApiParamType.Decimal => "number",
                CustomApiParamType.Bool => "boolean",
                CustomApiParamType.String => "string",
                CustomApiParamType.DateTime => "string",
                CustomApiParamType.Guid => "string",
                _ => throw new NotSupportedException($"Unsupported param type: {typeCode}"),
            };
        }

        public static string ToOpenApiSchemaFormat(this CustomApiParamType typeCode)
        {
            return typeCode switch
            {
                CustomApiParamType.Float => "float",
                CustomApiParamType.Integer => "int32",
                CustomApiParamType.Decimal => "number",
                CustomApiParamType.Bool => null,
                CustomApiParamType.String => null,
                CustomApiParamType.DateTime => "date-time",
                CustomApiParamType.Guid => null,
                _ => throw new NotSupportedException($"Unsupported param type: {typeCode}"),
            };
        }        

        public static OpenApiDocument GetSwagger(this CustomApiSignature plugin)
        {
            return new OpenApiDocument
            {
                Info = new OpenApiInfo()
                {
                    Title = "OData Service for namespace Microsoft.Dynamics.CRM",
                    Description = "This OData service is located at http://localhost",
                    Version = "1.0.1"
                },
                Servers = new List<OpenApiServer>()
                {
                    new OpenApiServer()
                    {
                        Url = "https://localhost"
                    }
                },
                Paths = new OpenApiPaths()
                {
                    [$"/{plugin.Api.uniquename}"] = new OpenApiPathItem()
                    {
                        Operations = new Dictionary<OperationType, OpenApiOperation>()
                        {
                            [OperationType.Post] = new OpenApiOperation()
                            {
                                Tags = new List<OpenApiTag>() { new OpenApiTag() { Name = plugin.Api.uniquename } },
                                Summary = $"Invoke actionImport {plugin.Api.uniquename}",
                                OperationId = plugin.Api.uniquename, // $$$ should probably be name
                                RequestBody = new OpenApiRequestBody()
                                {
                                    Required = true,
                                    Description = "Action parameters",                                    
                                    Content = new Dictionary<string, OpenApiMediaType>()
                                    {
                                        ["application/json"] = new OpenApiMediaType()
                                        {                                           
                                            Schema = new OpenApiSchema()
                                            {
                                                Type = "object",
                                                Properties = plugin.Inputs.Select((CustomApiRequestParam param)
                                                    => new KeyValuePair<string, OpenApiSchema>(param.name, new OpenApiSchema()
                                                    {
                                                        Description = param.description,                                                        
                                                        Type = ToOpenApiSchemaType(param.type),
                                                        Format = ToOpenApiSchemaFormat(param.type),
                                                    })).ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                                            }
                                        }
                                    },                                    
                                },
                                Extensions = new Dictionary<string, IOpenApiExtension>()
                                {
                                    ["x-ms-dynamics-metadata"] = new OpenApiString("actionImport")
                                },
                                Responses = new OpenApiResponses()
                                {
                                    ["200"] = new OpenApiResponse()
                                    {
                                        Description = "Success",
                                        Content = new Dictionary<string, OpenApiMediaType>()
                                        {
                                            ["application/json"] = new OpenApiMediaType()
                                            {
                                                Schema = new OpenApiSchema()
                                                {
                                                    Type = "object",
                                                    Properties = plugin.Outputs.ToDictionary(param => param.name, param => new OpenApiSchema()
                                                    {
                                                        Type = ToOpenApiSchemaType(param.type),
                                                        Format = ToOpenApiSchemaFormat(param.type)
                                                    })
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }
    }
}
