// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Connectors;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Xunit;

#pragma warning disable SA1118

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class TestCdpDataSource : CdpDataSource
    {
        public CdpTable CdpTable;

        public static TableType GetCDPTableType(string tableName, RecordType rt)
        {
            return new TestCdpDataSource("dataset", tableName, rt).CdpTable.GetTableValue().Type;
        }

        public TestCdpDataSource(string dataset, string tableName, RecordType rt)
            : base(dataset)
        {                       
            Type rawTableType = typeof(CdpTable).Assembly.GetTypes().First(t => t.Name == "RawTable");
            object rawTable = Activator.CreateInstance(rawTableType);
            rawTableType.GetProperty("Name").SetValue(rawTable, tableName);
            rawTableType.GetProperty("DisplayName").SetValue(rawTable, "tableDisplayName");

            Type listRawTableType = typeof(List<>).MakeGenericType(rawTableType);
            object listRawTable = listRawTableType.GetConstructors().First(c => !c.GetParameters().Any()).Invoke(new object[] { });
            listRawTableType.GetMethod("Add").Invoke(listRawTable, new object[] { rawTable });

            ConstructorInfo cdpConstructor = typeof(CdpTable).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).First(c => c.GetParameters().Any(p => p.Name == "datasetMetadata"));
            CdpTable cdpTable = (CdpTable)cdpConstructor.Invoke(new object[] { dataset, tableName, new DatasetMetadata(), listRawTable });

            Type cdpTableDescriptorType = typeof(CdpTable).Assembly.GetTypes().First(t => t.Name == "CdpTableDescriptor");
            object cdpTableDescriptor = Activator.CreateInstance(cdpTableDescriptorType);
            cdpTableDescriptorType.GetProperty("Name").SetValue(cdpTableDescriptor, tableName);
            cdpTableDescriptorType.GetProperty("DisplayName").SetValue(cdpTableDescriptor, "tableDisplayName");

            Type serviceCapabilitiesType = typeof(CdpTable).Assembly.GetTypes().First(t => t.Name == "ServiceCapabilities");
            ConstructorInfo serviceCapabilitiesConstructor = serviceCapabilitiesType.GetConstructors()[0];
            object serviceCapabilities = serviceCapabilitiesConstructor.Invoke(new object[]
            {
                    typeof(CdpTable).Assembly.GetTypes().First(t => t.Name == "SortRestriction").GetConstructors()[0].Invoke(new object[] { new List<string>(), new List<string>() }),
                    typeof(CdpTable).Assembly.GetTypes().First(t => t.Name == "FilterRestriction").GetConstructors()[0].Invoke(new object[] { new List<string>(), new List<string>() }),
                    typeof(CdpTable).Assembly.GetTypes().First(t => t.Name == "SelectionRestriction").GetConstructors()[0].Invoke(new object[] { true }),
                    typeof(CdpTable).Assembly.GetTypes().First(t => t.Name == "GroupRestriction").GetConstructors()[0].Invoke(new object[] { new List<string>() }),
                    new string[0], // filterFunctions
                    new string[0], // filterSupportedFunctions
                    typeof(CdpTable).Assembly.GetTypes().First(t => t.Name == "PagingCapabilities").GetConstructors()[0].Invoke(new object[] { false, new string[0] }),
                    true, // recordPermissionCapabilities
                    4, // oDataVersion
                    false // supportsDataverseOffline
            });

            cdpTableDescriptorType.GetProperty("TableCapabilities").SetValue(cdpTableDescriptor, serviceCapabilities);
            
            ConnectorType ct = (ConnectorType)typeof(ConnectorFunction).GetMethod("GetConnectorType", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] 
            { 
                "schema/items", 
                FormulaValue.New(GetSchema(tableName, rt)), 
                ConnectorCompatibility.CdpCompatibility 
            });

            Type cdpTableResolverType = typeof(CdpTable).Assembly.GetTypes().First(t => t.Name == "CdpTableResolver");
            object cdpTableResolver = Activator.CreateInstance(cdpTableResolverType, new object[] { cdpTable, null /* HttpClient */, string.Empty /* uriPrefix*/, true /* double encoding */, null /* logger */ });

            typeof(ConnectorType).GetMethod("AddTabularDataSource", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(
                ct,
                new object[]
                {
                        cdpTableResolver,
                        Activator.CreateInstance(typeof(List<>).MakeGenericType(typeof(CdpTable).Assembly.GetTypes().First(t => t.Name == "ReferencedEntity"))),
                        Activator.CreateInstance(typeof(List<>).MakeGenericType(typeof(CdpTable).Assembly.GetTypes().First(t => t.Name == "SqlRelationship"))),
                        typeof(FormulaValue).Assembly.GetTypes().First(t => t.Name == "DName").GetConstructors()[0].Invoke(new object[] { tableName }),
                        dataset,
                        ct,
                        serviceCapabilities,
                        false,
                        null
                });

            cdpTableDescriptorType.GetProperty("ConnectorType").SetValue(cdpTableDescriptor, ct);

            typeof(CdpTable).GetField("TabularTableDescriptor", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(cdpTable, cdpTableDescriptor);
            typeof(CdpTable).GetMethod("SetRecordType", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(cdpTable, new object[] { ct.FormulaType });

            CdpTable = cdpTable;
        }

        private static string GetSchema(string tableName, RecordType rt)
        {
            string schema = @"{ ""schema"": { ""type"": ""array"", ""items"": { ""type"": ""object"", ""title"": """ + tableName + @""", ""properties"": { ";
            schema += GetSchemaFromRecordType(rt);
            schema += "} } } }";
            return schema;
        }

        private static string GetSchemaFromRecordType(RecordType rt)
        {
            StringBuilder sb = new StringBuilder();
            bool first = true;

            foreach (string fieldName in rt.FieldNames)
            {
                FormulaType ft = rt.GetFieldType(fieldName);
                string ftType = ft.GetType().Name;

                if (!first)
                {
                    sb.Append(", ");                    
                }

                sb.Append('\"');
                sb.Append(fieldName);
                sb.Append(@""": { ""title"": """);
                sb.Append(fieldName);
                sb.Append(@""", ""type"": """);                

                string typ = ftType switch
                {
                    "StringType" => "string",
                    "DateType" => "string",
                    "DateTimeType" => "string",
                    "BlobType" => "string",
                    "DecimalType" => "number",
                    "NumberType" => "number",
                    "OptionSetValueType" => "string",
                    _ => throw new NotImplementedException($"Unknown type {ftType}")
                };

                sb.Append(typ);

                string fmt = ft.GetType().Name switch
                {
                    "StringType" => "string",
                    "DateType" => "date",
                    "DateTimeType" => "date-time",
                    "BlobType" => "binary",
                    "DecimalType" => "decimal",
                    "NumberType" => "fxnumber",
                    "OptionSetValueType" => "enum",
                    _ => throw new NotImplementedException($"Unknown type {ftType}")
                };

                if (fmt == "enum")
                {
                    sb.Append(@""", ""enum"": [ ");

                    bool firstEnum = true;

                    foreach (KeyValuePair<DName, DName> kvp in ft._type.DisplayNameProvider.LogicalToDisplayPairs)
                    {
                        if (!firstEnum)
                        {
                            sb.Append(", ");                            
                        }

                        sb.Append(@"""");
                        sb.Append(kvp.Key.Value);
                        sb.Append(@"""");

                        firstEnum = false;
                    }

                    sb.Append(@" ], ""x-ms-enum-display-name"": [ ");

                    firstEnum = true;

                    foreach (KeyValuePair<DName, DName> kvp in ft._type.DisplayNameProvider.LogicalToDisplayPairs)
                    {
                        if (!firstEnum)
                        {
                            sb.Append(", ");                            
                        }

                        sb.Append(@"""");
                        sb.Append(kvp.Value.Value);
                        sb.Append(@"""");

                        firstEnum = false;
                    }

                    sb.Append(@" ], ""x-ms-enum"": { ""name"": """);
                    sb.Append(((OptionSetValueType)ft).OptionSetName);                    
                    sb.Append(@""" } }");
                }
                else
                {
                    sb.Append(@""", ""format"": """);
                    sb.Append(fmt);
                    sb.Append(@""" }");
                }

                first = false;
            }

            return sb.ToString();
        }

        public override Task<CdpTable> GetTableAsync(HttpClient httpClient, string uriPrefix, string tableName, bool? logicalOrDisplay, CancellationToken cancellationToken, ConnectorLogger logger = null)
        {
            return Task.FromResult(CdpTable);
        }

        public override Task<IEnumerable<CdpTable>> GetTablesAsync(HttpClient httpClient, string uriPrefix, CancellationToken cancellationToken, ConnectorLogger logger = null)
        {
            return Task.FromResult<IEnumerable<CdpTable>>(new List<CdpTable>() { CdpTable });
        }
    }
}
