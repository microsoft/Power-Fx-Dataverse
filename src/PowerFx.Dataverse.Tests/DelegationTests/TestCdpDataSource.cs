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
                typeof(CdpTable).Assembly.GetTypes().First(t => t.Name == "SortRestriction").GetConstructors()[0].Invoke(new object[] { new List<string>() /* unsortableProperties */, new List<string>() /* ascendingOnlyProperties */ }),
                typeof(CdpTable).Assembly.GetTypes().First(t => t.Name == "FilterRestriction").GetConstructors()[0].Invoke(new object[] { new List<string>() /* requiredProperties */, new List<string>() /* nonFilterableProperties */ }),
                typeof(CdpTable).Assembly.GetTypes().First(t => t.Name == "SelectionRestriction").GetConstructors()[0].Invoke(new object[] { true /* isSelectable */ }),
                typeof(CdpTable).Assembly.GetTypes().First(t => t.Name == "GroupRestriction").GetConstructors()[0].Invoke(new object[] { new List<string>() /* ungroupableProperties */ }),
                new string[0], // filterFunctions
                new string[0], // filterSupportedFunctions
                typeof(CdpTable).Assembly.GetTypes().First(t => t.Name == "PagingCapabilities").GetConstructors()[0].Invoke(new object[] { false /* isOnlyServerPagable */, new string[0] /* serverPagingOptions */ }),
                true, // recordPermissionCapabilities
                4, // oDataVersion
                false // supportsDataverseOffline
            });

            cdpTableDescriptorType.GetProperty("TableCapabilities").SetValue(cdpTableDescriptor, serviceCapabilities);

            FormulaType ft = (FormulaType)typeof(ConnectorType).GetMethod("GetRecordTypeWithADS", BindingFlags.Static | BindingFlags.NonPublic).Invoke(
                null,
                new object[]
                {
                    rt,
                    Activator.CreateInstance(typeof(List<>).MakeGenericType(typeof(CdpTable).Assembly.GetTypes().First(t => t.Name == "ReferencedEntity"))),
                    Activator.CreateInstance(typeof(List<>).MakeGenericType(typeof(CdpTable).Assembly.GetTypes().First(t => t.Name == "SqlRelationship"))),
                    typeof(FormulaValue).Assembly.GetTypes().First(t => t.Name == "DName").GetConstructors()[0].Invoke(new object[] { tableName }),
                    dataset,
                    serviceCapabilities,
                    false,
                    null
                });

            typeof(CdpTable).GetField("TabularTableDescriptor", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(cdpTable, cdpTableDescriptor);
            typeof(CdpTable).GetMethod("SetRecordType", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(cdpTable, new object[] { ft });

            CdpTable = cdpTable;
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
