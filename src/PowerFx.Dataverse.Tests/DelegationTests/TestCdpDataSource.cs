// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Connectors;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Xunit;

#pragma warning disable SA1118, SA1117

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class TestCdpDataSource : CdpDataSource
    {
        public TestCdpTable CdpTable;

        public static TestCdpDataSource GetCDPDataSource(string tableName, RecordType recordType, RecordValue recordValue, Action<TableParameters> tableParametersUpdater = null)
        {
            return new TestCdpDataSource("dataset", tableName, recordType, recordValue, tableParametersUpdater);
        }

        public TestCdpDataSource(string dataset, string tableName, RecordType recordType, RecordValue recordValue, Action<TableParameters> tableParametersUpdater = null)
            : base(dataset)
        {           
            TableParameters tableParameters = TableParameters.Default(tableName, false, recordType, dataset, recordType.FieldNames);

            if (tableParametersUpdater != null)
            {
                tableParametersUpdater(tableParameters);
            }

            TestCdpTable cdpTable = new TestCdpTable(dataset, tableName, new DatasetMetadata(), recordType, "tableDisplayName", tableParameters, recordValue);
            cdpTable.Init();

            CdpTable = cdpTable;
        }

        public override Task<CdpTable> GetTableAsync(HttpClient httpClient, string uriPrefix, string tableName, bool? logicalOrDisplay, CancellationToken cancellationToken, ConnectorLogger logger = null)
        {
            return Task.FromResult(CdpTable as CdpTable);
        }

        public override Task<IEnumerable<CdpTable>> GetTablesAsync(HttpClient httpClient, string uriPrefix, CancellationToken cancellationToken, ConnectorLogger logger = null)
        {
            return Task.FromResult<IEnumerable<CdpTable>>(new List<CdpTable>() { CdpTable });
        }
    }

    public class TestCdpTable : CdpTable
    {
        private RecordType _recordType;

        private string _tableName;

        private RecordValue _row1;

        private TableParameters _tableParameters;

        public ODataParameters ODataParameters { get; private set; }

        public TestCdpTable(string dataset, string tableName, DatasetMetadata datasetMetadata, FormulaType formulaType, string displayName, TableParameters tableParameters, RecordValue recordValue)
            : base(dataset, tableName, datasetMetadata, formulaType, displayName, tableParameters)
        {
            _tableName = tableName;
            _recordType = formulaType as RecordType;
            _row1 = recordValue;
            _tableParameters = tableParameters;
        }

        public void Init()
        {
            using HttpClient httpClient = new TestHttpClient(_tableName, _recordType);
            base.InitAsync(httpClient, "uri/connector", CancellationToken.None).Wait();

            // replace service capabilities
            TableType._type.AssociatedDataSources.Clear();
            TableType._type.AssociatedDataSources.Add(new InternalTableParameters(_tableParameters));
        }

        public override Task InitAsync(HttpClient httpClient, string uriPrefix, CancellationToken cancellationToken, ConnectorLogger logger = null)
        {
            TabularRecordType = _recordType;
            return Task.CompletedTask;
        }

        protected override Task<IReadOnlyCollection<DValue<RecordValue>>> GetItemsInternalAsync(IServiceProvider serviceProvider, ODataParameters odataParameters, CancellationToken cancellationToken)
        {
            ODataParameters = odataParameters;
            IReadOnlyCollection<DValue<RecordValue>> result = new DValue<RecordValue>[] { DValue<RecordValue>.Of(_row1) };

            return Task.FromResult(result);
        }
    }

    public class TestHttpClient : HttpClient
    {
        private RecordType _recordType;

        private string _tableName;

        public TestHttpClient(string tableName, RecordType recordType)
            : base()
        {
            _tableName = tableName;
            _recordType = recordType;
        }

        public override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);

            response.RequestMessage = request;

            // $$$ Generate "standard" capabiliites

            string text = @$"{{
  ""name"": ""{_tableName}"",
  ""title"": ""{_tableName}"",
  ""x-ms-permission"": ""read-write"",
  ""x-ms-capabilities"": {{
    ""sortRestrictions"": {{ ""sortable"": true }},
    ""filterRestrictions"": {{ ""filterable"": true }},
    ""selectRestrictions"": {{ ""selectable"": true }},
    ""filterFunctionSupport"": [ ""lt"", ""le"", ""eq"", ""ne"", ""gt"", ""ge"", ""min"", ""max"", ""countdistinct"", ""add"", ""sub"", ""mul"", ""div"", ""mod"", ""negate"", ""now"", ""not"", ""and"", ""or"", ""day"", ""month"", ""year"", ""hour"", ""minute"", ""second"", ""date"", ""time"", ""totaloffsetminutes"", ""totalseconds"", ""round"", ""floor"", ""ceiling"", ""contains"", ""startswith"", ""endswith"", ""length"", ""indexof"", ""replace"", ""substring"", ""substringof"", ""tolower"", ""toupper"", ""trim"", ""concat"", ""sum"", ""min"", ""max"", ""average"", ""countdistinct"", ""null"" ]
  }},
  ""schema"": {{
    ""type"": ""array"",
    ""items"": {{
      ""type"": ""object"",
      ""required"": [ ""Name"", ""ProductNumber"", ""StandardCost"", ""ListPrice"", ""SellStartDate"", ""rowguid"", ""ModifiedDate"" ],
      ""properties"": {{";

            bool first = true;

            foreach (NamedFormulaType nft in _recordType.GetFieldTypes())
            {
                string name = nft.Name;
                string displayName = nft.DisplayName;
                FormulaType formulaType = nft.Type;

                string type = formulaType._type.Kind switch
                {
                    DKind.String => "string",
                    DKind.Boolean => "boolean",
                    DKind.Decimal => "number",

                    // Make sure this will generate Fx Number type
                    DKind.Number => "fxnumber",
                    DKind.OptionSetValue => "string",
                    DKind.Date => "string",
                    _ => throw new NotImplementedException($"Unknown kind {formulaType._type.Kind}")
                };

                if (first)
                {
                    first = false;
                }
                else
                {
                    text += ",";
                }

                text += @$"""{name}"": {{
          ""title"": ""{displayName}"",
          ""x-ms-capabilities"": {{ ""filterFunctions"": [ ""lt"", ""le"", ""eq"", ""ne"", ""gt"", ""ge"", ""min"", ""max"", ""countdistinct"", ""add"", ""sub"", ""mul"", ""div"", ""mod"", ""negate"", ""sum"", ""average"" ] }},
          ""type"": ""{type}""";

                if (formulaType._type.Kind == DKind.Date)
                {
                    text += $@", ""format"": ""date""";
                }
                else if (formulaType._type.Kind == DKind.OptionSetValue)
                {
                    OptionSetValueType osvt = (OptionSetValueType)formulaType;

                    text += $@", ""format"": ""enum""";
                    text += $@", ""x-ms-enum"": {{ ""name"": ""{osvt.OptionSetName.Value}"" }}";

                    text += $@", ""enum"": [";
                    bool f2 = true;
                    foreach (KeyValuePair<DName, DName> kvp in osvt._type.DisplayNameProvider.LogicalToDisplayPairs)
                    {
                        if (f2)
                        {
                            f2 = false;
                        }
                        else
                        {
                            text += ",";
                        }

                        text += $@"""{kvp.Key.Value}""";
                    }

                    text += @"]";

                    // $$$ OptionSet display names not implemented yet (x-ms-enum-displayname in extensions)
                }

                text += "}";
            }

            text += @"}
    },
    ""x-ms-permission"": ""read-write""
  }
}";

            response.Content = new StringContent(text);

            return response;
        }
    }
}
