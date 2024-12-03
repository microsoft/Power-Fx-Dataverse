// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Connectors;
using Microsoft.PowerFx.Dataverse;
using Microsoft.PowerFx.Types;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable SA1116

namespace PowerFx.Dataverse.Cdp.Tests
{
    public class CdpTests
    {
        private readonly ITestOutputHelper _output;

        public CdpTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Cdp_Sql()
        {
            using var testConnector = new LoggingTestServer(null /* no swagger */, _output);
            var config = new PowerFxConfig(Features.PowerFxV1);
            var engine = new RecalcEngine(config);

            // Enable delegation for CDP connector
            engine.EnableDelegation();

            ConsoleLogger logger = new ConsoleLogger(_output);
            using var httpClient = new HttpClient(testConnector);
            string connectionId = "2cc03a388d38465fba53f05cd2c76181";
            string jwt = "eyJ0eXAiOiJKSuA...";
            using var client = new PowerPlatformConnectorClient("dac64a92-df6a-ee6e-a6a2-be41a923e371.15.common.tip1002.azure-apihub.net", "dac64a92-df6a-ee6e-a6a2-be41a923e371", connectionId, () => jwt, httpClient) { SessionId = "8e67ebdc-d402-455a-b33a-304820832383" };

            string realTableName = "Product";
            string fxTableName = "Products";

            testConnector.SetResponseFromFile(@"Responses\SQL GetDatasetsMetadata.json");
            DatasetMetadata dm = await CdpDataSource.GetDatasetsMetadataAsync(client, $"/apim/sql/{connectionId}", CancellationToken.None, logger);

            Assert.NotNull(dm);
            Assert.Null(dm.Blob);

            CdpDataSource cds = new CdpDataSource("default,default");

            testConnector.SetResponseFromFiles(@"Responses\SQL GetDatasetsMetadata.json", @"Responses\SQL GetTables SampleDB.json");
            IEnumerable<CdpTable> tables = await cds.GetTablesAsync(client, $"/apim/sql/{connectionId}", CancellationToken.None, logger);

            Assert.NotNull(tables);
            Assert.Equal(17, tables.Count());
           
            CdpTable connectorTable = tables.First(t => t.DisplayName == realTableName);

            Assert.False(connectorTable.IsInitialized);
            Assert.Equal(realTableName, connectorTable.DisplayName);

            testConnector.SetResponseFromFile(@"Responses\SQL GetTables SampleDB.json");
            CdpTable table2 = await cds.GetTableAsync(client, $"/apim/sql/{connectionId}", realTableName, null /* logical or display name */, CancellationToken.None, logger);
            Assert.False(table2.IsInitialized);
            Assert.Equal(realTableName, table2.DisplayName);
            Assert.Equal("[SalesLT].[Product]", table2.TableName); // Logical Name          

            testConnector.SetResponseFromFiles(@"Responses\SQL GetSchema Products.json", @"Responses\SQL GetRelationships SampleDB.json");
            await connectorTable.InitAsync(client, $"/apim/sql/{connectionId}", CancellationToken.None, logger);
            Assert.True(connectorTable.IsInitialized);

            CdpTableValue sqlTable = connectorTable.GetTableValue();
            Assert.True(sqlTable.IsDelegable);            
            
            SymbolValues symbolValues = new SymbolValues().Add(fxTableName, sqlTable);
            RuntimeConfig rc = new RuntimeConfig(symbolValues).AddService<ConnectorLogger>(logger);

            // Expression with tabular connector
            string expr = @$"First(FirstN(Filter({fxTableName}, ProductID = 680), 2)).Name";
            CheckResult check = engine.Check(expr, options: new ParserOptions() { AllowsSideEffects = true }, symbolTable: symbolValues.SymbolTable);
            Assert.True(check.IsSuccess, string.Join(", ", check.Errors.Select(er => er.Message)));

            // Verify delegation is working (__retrieveSingle)
            string ir = Regex.Replace(check.PrintIR(), "RuntimeValues_[0-9]+", "RuntimeValues_XXXX");
            Assert.Equal<object>(@"FieldAccess(__retrieveSingle:r!(Delegable(ResolvedObject('Products:RuntimeValues_XXXX'), __eq:r*, Scope 1(Delegable(ResolvedObject('Products:RuntimeValues_XXXX'), {fieldName: ""ProductID"":s}, {fieldFunctions: Table:*[Value:n]()}, 680:w), __noop:N(), ResolvedObject(Microsoft.PowerFx.Dataverse.Eval.Delegation.GroupByObjectFormulaValue), """":s), Name)", ir);

            // Use tabular connector. Internally we'll call CdpTableValue.GetRowsInternal to get the data
            testConnector.SetResponseFromFile(@"Responses\SQL GetItems Products.json");
            FormulaValue result = await check.GetEvaluator().EvalAsync(CancellationToken.None, rc);

            StringValue address = Assert.IsType<StringValue>(result);
            Assert.Equal("HL Road Frame - Black, 58", address.Value);

            // SQL relationships are not available
            bool b = sqlTable.RecordType.TryGetFieldExternalTableName("ProductModelID", out string tableName, out string foreignKey);
            Assert.False(b);
            testConnector.SetResponseFromFiles(@"Responses\SQL GetSchema ProductModel.json", @"Responses\SQL GetRelationships SampleDB.json");
            b = sqlTable.RecordType.TryGetFieldType("ProductModelID", out FormulaType productModelID);

            Assert.True(b);
            Assert.IsType<DecimalType>(productModelID);

            IEnumerable<string> actual = testConnector._log.ToString().Split("\r\n").Where(x => x.Contains("/items?"));
            string query = Assert.Single(actual);

            // Validate OData query is present
            Assert.Equal(@" x-ms-request-url: /apim/sql/2cc03a388d38465fba53f05cd2c76181/v2/datasets/default,default/tables/%5BSalesLT%5D.%5BProduct%5D/items?api-version=2015-09-01&$filter=%28ProductID%20eq%20680%29&$top=1", query);
        }

        [Fact]
        public async Task Cdp_Sql_NonSortable()
        {
            using var testConnector = new LoggingTestServer(null /* no swagger */, _output);
            var config = new PowerFxConfig(Features.PowerFxV1);
            var engine = new RecalcEngine(config);

            // Enable delegation for CDP connector
            engine.EnableDelegation();

            ConsoleLogger logger = new ConsoleLogger(_output);
            using var httpClient = new HttpClient(testConnector);
            string connectionId = "2cc03a388d38465fba53f05cd2c76181";
            string jwt = "eyJ0eXAiOiJKSuA...";
            using var client = new PowerPlatformConnectorClient("dac64a92-df6a-ee6e-a6a2-be41a923e371.15.common.tip1002.azure-apihub.net", "dac64a92-df6a-ee6e-a6a2-be41a923e371", connectionId, () => jwt, httpClient) { SessionId = "8e67ebdc-d402-455a-b33a-304820832383" };

            string realTableName = "Product";
            string fxTableName = "Products";

            testConnector.SetResponseFromFile(@"Responses\SQL GetDatasetsMetadata.json");
            DatasetMetadata dm = await CdpDataSource.GetDatasetsMetadataAsync(client, $"/apim/sql/{connectionId}", CancellationToken.None, logger);            
            CdpDataSource cds = new CdpDataSource("default,default");

            testConnector.SetResponseFromFiles(@"Responses\SQL GetDatasetsMetadata.json", @"Responses\SQL GetTables SampleDB.json");
            IEnumerable<CdpTable> tables = await cds.GetTablesAsync(client, $"/apim/sql/{connectionId}", CancellationToken.None, logger);
          
            CdpTable connectorTable = tables.First(t => t.DisplayName == realTableName);    

            testConnector.SetResponseFromFiles(@"Responses\SQL GetSchema Products NonSortable.json", @"Responses\SQL GetRelationships SampleDB.json");
            await connectorTable.InitAsync(client, $"/apim/sql/{connectionId}", CancellationToken.None, logger);            

            CdpTableValue sqlTable = connectorTable.GetTableValue();            
            SymbolValues symbolValues = new SymbolValues().Add(fxTableName, sqlTable);
            RuntimeConfig rc = new RuntimeConfig(symbolValues).AddService<ConnectorLogger>(logger);

            // Expression with tabular connector
            string expr = @$"First(FirstN(Filter({fxTableName}, ProductID = 680), 2)).Name";
            CheckResult check = engine.Check(expr, options: new ParserOptions() { AllowsSideEffects = true }, symbolTable: symbolValues.SymbolTable);
            Assert.True(check.IsSuccess, string.Join(", ", check.Errors.Select(er => er.Message)));

            // Table is not sortable but we can delegate the inner Filter part
            string ir = Regex.Replace(check.PrintIR(), "RuntimeValues_[0-9]+", "RuntimeValues_XXXX");
            Assert.Equal<object>(@"FieldAccess(First:r!(FirstN:r*(__retrieveMultiple:r*, Scope 1(Delegable(ResolvedObject('Products:RuntimeValues_XXXX'), __eq:r*, Scope 1(Delegable(ResolvedObject('Products:RuntimeValues_XXXX'), {fieldName: ""ProductID"":s}, {fieldFunctions: Table:*[Value:n]()}, 680:w), __noop:N(), 1000:n, ResolvedObject(Microsoft.PowerFx.Dataverse.Eval.Delegation.GroupByObjectFormulaValue), """":s), Float:n(2:w))), Name)", ir);
            
            testConnector.SetResponseFromFile(@"Responses\SQL GetItems Products.json");
            FormulaValue result = await check.GetEvaluator().EvalAsync(CancellationToken.None, rc);

            StringValue address = Assert.IsType<StringValue>(result);
            Assert.Equal("HL Road Frame - Black, 58", address.Value);             

            IEnumerable<string> actual = testConnector._log.ToString().Split("\r\n").Where(x => x.Contains("/items?"));
            string query = Assert.Single(actual);

            // OData query - note $filter is present but $top=1000
            Assert.Equal<object>(@" x-ms-request-url: /apim/sql/2cc03a388d38465fba53f05cd2c76181/v2/datasets/default,default/tables/%5BSalesLT%5D.%5BProduct%5D/items?api-version=2015-09-01&$filter=%28ProductID%20eq%20680%29&$top=1000", query);
        }
    }
}
