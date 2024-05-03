using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using System.Threading;
using Xunit;
using System.Threading.Tasks;
using Xunit.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.IO;
using Azure.Data.Tables;
using Microsoft.PowerFx.AzureStorage;
using System.Text.Json;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Microsoft.PowerFx.Connectors;
using System.Net.Http;
using Microsoft.PowerFx.Connectors.Tabular;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
#if false // $$$ Remove this. 
    public class ConnectorDelegationTests
    {
        [Theory]
        [InlineData("Filter(Test1, score > 200)")]
        public void TestAzure(string expr)
        {
            var sas = new Uri(File.ReadAllText(@"c:\secrets\az_table_Test578.txt"));
            var tableClient = new TableClient(sas); // Azure Storage class 

            // var t3 = AzureTableValue.InferType(tableClient).Result;
            var tableValue = tableClient.NewAsync().Result;

            var recordType = tableValue.Type.ToRecord();

            var st = new SymbolValues("Delegable_1");
            st.Add(tableValue.TableName, tableValue);

            Assert.Equal("Delegable_1", st.SymbolTable.DebugName);

            var engine = new RecalcEngine();
            engine.EnableDelegation();

            // Delegates to Azure Table:
            //  $filter=((Timestamp le datetime'2023-06-01T07:00:00Z') and (score ge 200))
            //expr = "Filter(Test1, Timestamp < Date(2023,6,1) && score > 200)";
            //expr = "Filter(Test1, score > 200)";

            var check = new CheckResult(engine)
                .SetText(expr)
                .SetBindingInfo(st.SymbolTable);

            var errors = check.ApplyErrors().ToArray();
            Assert.Empty(errors);

            // "__retrieveMultiple(t1, __and(__lt(t1, Price, Float(120)), __gt(t1, Price, Float(90))), 1000, False)"
            var ir = check.GetCompactIRString();

            var eval = check.GetEvaluator();
            var result = eval.EvalAsync(CancellationToken.None, st).Result;

            var resutlStr = result.ToExpression();
        }


        // Delegation using direct API. 
        [Theory]
        [InlineData("Filter(t1, Price < 120 And 90 < Price)")]
        public void TestDirectApi(string expr)
        {
            var recordType = RecordType.Empty().Add("Price", FormulaType.Number);
            var t1 = new MyTable(recordType);

            var st = new SymbolValues("Delegable_1");
            st.Add("t1", t1);

            Assert.Equal("Delegable_1", st.SymbolTable.DebugName);

            var engine = new RecalcEngine();
            engine.EnableDelegation();

            var check = new CheckResult(engine)
                .SetText(expr)
                .SetBindingInfo(st.SymbolTable);

            var errors = check.ApplyErrors().ToArray();

            // "__retrieveMultiple(t1, __and(__lt(t1, Price, Float(120)), __gt(t1, Price, Float(90))), 1000, False)"
            var ir = check.GetCompactIRString();

            var eval = check.GetEvaluator();

            var rc = new RuntimeConfig(st)
            {
                ServiceProvider = _runtimeServices
            };
            rc.AddService(new MyService());

            var result = eval.EvalAsync(CancellationToken.None, rc).Result;
        }

        [Theory]
        //[InlineData("Filter(t1, Price < 120 And 90 < Price)")]
        [InlineData("First(MikeTestList).City")]
        public void SharepointTestDirectApi(string expr)
        {
            var ts = AddConnector(@"C:\temp\SharepointList.json").Result;
            TableValue t1 = ts.GetTableValue();

            var st = new SymbolValues("Delegable_1");
            st.Add("MikeTestList", t1);

            Assert.Equal("Delegable_1", st.SymbolTable.DebugName);

            var engine = new RecalcEngine();
            engine.EnableDelegation();

            var check = new CheckResult(engine)
                .SetText(expr)
                .SetBindingInfo(st.SymbolTable);

            var errors = check.ApplyErrors().ToArray();

            // "__retrieveMultiple(t1, __and(__lt(t1, Price, Float(120)), __gt(t1, Price, Float(90))), 1000, False)"
            var ir = check.GetCompactIRString();

            var eval = check.GetEvaluator();


            var rc = new RuntimeConfig(st)
            {
                ServiceProvider = _runtimeServices
            };

            var result = eval.EvalAsync(CancellationToken.None, rc).Result;

        }

        public class ConnectionPoco
        {
            public string endpoint { get; set; }
            public string environmentId { get; set; }

            public string connectionId { get; set; }

            public string urlprefix { get; set; }

            public string dataset { get; set; }
            public string tablename { get; set; }

            public string swaggerFile { get; set; }

            public string jwtFile { get; set; }
        }

        public BasicServiceProvider _runtimeServices = new BasicServiceProvider();

        private readonly HttpClient _httpClient = new HttpClient();

        private PowerPlatformConnectorClient _ppClient;

        private readonly PowerFxConfig _config = new PowerFxConfig();

        private async Task<CdpSwaggerTabularService> AddConnector(string path)
        {
            var json = File.ReadAllText(path);
            var poco = JsonSerializer.Deserialize<ConnectionPoco>(json);

            string jwt = File.ReadAllText(poco.jwtFile);
            var apiDoc = ReadSwagger(poco.swaggerFile);

            _ppClient = new PowerPlatformConnectorClient(
                poco.endpoint,
                poco.environmentId,
                poco.connectionId,
                () => jwt,
                _httpClient)
            {
                SessionId = "8e67ebdc-d402-455a-b33a-304820832384" // doesn't matter
            };

            IReadOnlyDictionary<string, FormulaValue> globals = new Dictionary<string, FormulaValue>()
            {
                { "connectionId", FormulaValue.New(poco.connectionId) },
                { "dataset", FormulaValue.New(poco.dataset) }, // skip for salesforce
                { "table", FormulaValue.New(poco.tablename) },
            };

            var tabularService = new CdpSwaggerTabularService(apiDoc, globals);
            tabularService.InitAsync(_config, _ppClient, default, new ConsoleLogger()).Wait();

            var recordType = tabularService.TableType.ToRecord();


            MyContext ctx = new MyContext
            {
                _client = _ppClient
            };
            _runtimeServices.AddRuntimeContext(ctx);

            //TableValue spTable = tabularService.GetTableValue();
            return tabularService;
        }

        private class MyContext : BaseRuntimeConnectorContext
        {
            public override TimeZoneInfo TimeZoneInfo => TimeZoneInfo.Local;

            public HttpClient _client;

            public override HttpMessageInvoker GetInvoker(string @namespace)
            {
                return _client;
            }
        }

        // Get a swagger file from the embedded resources. 
        public static OpenApiDocument ReadSwagger(string path)
        {
            using var stream = File.OpenRead(path);

            OpenApiReaderSettings oars = new OpenApiReaderSettings() { };
            OpenApiDocument doc = new OpenApiStreamReader(oars).Read(stream, out OpenApiDiagnostic diag);

            if (diag.Warnings.Count + diag.Errors.Count > 0)
            {
                Console.WriteLine("swagger load errro");
            }

            return doc;
        }
        internal class ConsoleLogger : ConnectorLogger
        {
            protected override void Log(ConnectorLog log)
            {
                Console.Write(log.Message);
                Console.Write(log.Exception);
            }
        }
    }
#endif
}
