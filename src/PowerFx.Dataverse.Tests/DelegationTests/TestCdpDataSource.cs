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

        public static TableType GetCDPTableType(string tableName, RecordType recordType)
        {
            return new TestCdpDataSource("dataset", tableName, recordType).CdpTable.GetTableValue().Type;
        }

        public TestCdpDataSource(string dataset, string tableName, RecordType recordType)
            : base(dataset)
        {
            RawTable rawTable = new RawTable() { Name = tableName, DisplayName = "tableDisplayName" };           
            List<RawTable> listRawTable = new List<RawTable>() { rawTable };            
            ServiceCapabilities serviceCapabilities = ServiceCapabilities.Default(recordType);
            CdpTableDescriptor cdpTableDescriptor = new CdpTableDescriptor() { Name = tableName, DisplayName = "tableDisplayName", TableCapabilities = serviceCapabilities };           
            RecordType recordTypeWithAds = ConnectorType.GetRecordTypeWithADS(recordType, new DName(tableName), dataset, serviceCapabilities, false);
            CdpTable cdpTable = new CdpTable(dataset, tableName, new DatasetMetadata(), listRawTable, cdpTableDescriptor /* TableCapabilities */, recordTypeWithAds /* SetRecordType */);                      
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
