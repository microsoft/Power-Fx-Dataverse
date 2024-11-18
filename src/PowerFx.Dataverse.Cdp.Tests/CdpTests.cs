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
            Assert.Equal<object>(@"FieldAccess(__retrieveSingle:r!(Delegable(ResolvedObject('Products:RuntimeValues_XXXX'), __eq:r*, Scope 1(Delegable(ResolvedObject('Products:RuntimeValues_XXXX'), ""ProductID"":s, 680:w), __noop:N(), """":s), Name)", ir);

            // Use tabular connector. Internally we'll call CdpTableValue.GetRowsInternal to get the data
            testConnector.SetResponseFromFile(@"Responses\SQL GetItems Products.json");
            FormulaValue result = await check.GetEvaluator().EvalAsync(CancellationToken.None, rc);

            StringValue address = Assert.IsType<StringValue>(result);
            Assert.Equal("HL Road Frame - Black, 58", address.Value);

            bool b = sqlTable.RecordType.TryGetFieldExternalTableName("ProductModelID", out string externalTableName, out string foreignKey);
            Assert.True(b);
            Assert.Equal("[SalesLT].[ProductModel]", externalTableName); // Logical Name
            Assert.Equal("ProductModelID", foreignKey);

            testConnector.SetResponseFromFiles(@"Responses\SQL GetSchema ProductModel.json", @"Responses\SQL GetRelationships SampleDB.json");
            b = sqlTable.RecordType.TryGetFieldType("ProductModelID", out FormulaType productModelID);

            Assert.True(b);

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
            Assert.Equal<object>(@"FieldAccess(First:r!(FirstN:r*(__retrieveMultiple:r*, Scope 1(Delegable(ResolvedObject('Products:RuntimeValues_XXXX'), __eq:r*, Scope 1(Delegable(ResolvedObject('Products:RuntimeValues_XXXX'), ""ProductID"":s, 680:w), __noop:N(), 1000:n, """":s), Float:n(2:w))), Name)", ir);
            
            testConnector.SetResponseFromFile(@"Responses\SQL GetItems Products.json");
            FormulaValue result = await check.GetEvaluator().EvalAsync(CancellationToken.None, rc);

            StringValue address = Assert.IsType<StringValue>(result);
            Assert.Equal("HL Road Frame - Black, 58", address.Value);

            bool b = sqlTable.RecordType.TryGetFieldExternalTableName("ProductModelID", out string externalTableName, out string foreignKey);
            Assert.True(b);
            Assert.Equal("[SalesLT].[ProductModel]", externalTableName); // Logical Name
            Assert.Equal("ProductModelID", foreignKey);         

            IEnumerable<string> actual = testConnector._log.ToString().Split("\r\n").Where(x => x.Contains("/items?"));
            string query = Assert.Single(actual);

            // OData query - note $filter is present but $top=1000
            Assert.Equal<object>(@" x-ms-request-url: /apim/sql/2cc03a388d38465fba53f05cd2c76181/v2/datasets/default,default/tables/%5BSalesLT%5D.%5BProduct%5D/items?api-version=2015-09-01&$filter=%28ProductID%20eq%20680%29&$top=1000", query);
        }

        [Fact]
        public async Task Cdp_Sql_Test()
        {
            using var testConnector = new LoggingTestServer(null /* no swagger */, _output);
            var config = new PowerFxConfig(Features.PowerFxV1);
            var engine = new RecalcEngine(config);

            // Enable delegation for CDP connector
            engine.EnableDelegation();

            ConsoleLogger logger = new ConsoleLogger(_output);
            using var httpClient = new HttpClient(); // testConnector);
            string connectionId = "29941b77eb0a40fe925cd7a03cb85b40";
            string jwt = "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsIng1dCI6Inp4ZWcyV09OcFRrd041R21lWWN1VGR0QzZKMCIsImtpZCI6Inp4ZWcyV09OcFRrd041R21lWWN1VGR0QzZKMCJ9.eyJhdWQiOiJodHRwczovL2FwaWh1Yi5henVyZS5jb20iLCJpc3MiOiJodHRwczovL3N0cy53aW5kb3dzLm5ldC83MmY5ODhiZi04NmYxLTQxYWYtOTFhYi0yZDdjZDAxMWRiNDcvIiwiaWF0IjoxNzMxOTE3MDE2LCJuYmYiOjE3MzE5MTcwMTYsImV4cCI6MTczMTkyMjUyOSwiYWNyIjoiMSIsImFpbyI6IkFZUUFlLzhZQUFBQW1CYzNCVyt4RjJxZWdpK2orS3VHUVRzT1ZGZ0Vma1QzaVFFWElFVlBVOGhZbnlPVHZXemdUSVlicS9aSkZCYnZVNHNhcmVUdWJvM0N2Y0twcUQxVU4rWGwzUFhFSzBvVDVhcG1hWTNIaDZ5VGQyKzAyc0Q0OG1Vbk81enEwaWZaeTdZeTB3eVdzVTRrUHpWc0lTNzdNZVJVSGU5dkkwY1BQQVN5SHNGOFl1TT0iLCJhbXIiOlsicHdkIiwicnNhIiwibWZhIl0sImFwcGlkIjoiYThmN2E2NWMtZjViYS00ODU5LWIyZDYtZGY3NzJjMjY0ZTlkIiwiYXBwaWRhY3IiOiIwIiwiZGV2aWNlaWQiOiI4YmYzOGFmMi1jNTk5LTRiMGYtOWE4OC1lZDJlMGM0ZmIxZDAiLCJmYW1pbHlfbmFtZSI6IkdlbmV0aWVyIiwiZ2l2ZW5fbmFtZSI6Ikx1YyIsImlkdHlwIjoidXNlciIsImlwYWRkciI6IjkwLjEwNC43My4yMDMiLCJuYW1lIjoiTHVjIEdlbmV0aWVyIiwib2lkIjoiMTUwODcxM2ItOGZjYi00OTUxLTlhZGQtZTExYmJiZDYwMmMzIiwib25wcmVtX3NpZCI6IlMtMS01LTIxLTE3MjEyNTQ3NjMtNDYyNjk1ODA2LTE1Mzg4ODIyODEtMzcyNDkiLCJwdWlkIjoiMTAwMzNGRkY4MDFCREZCOCIsInJoIjoiMS5BUm9BdjRqNWN2R0dyMEdScXkxODBCSGJSMTg4QmY2U05oUlBydkx1TlB3SUhLNGFBTDRhQUEuIiwic2NwIjoiUnVudGltZS5BbGwiLCJzdWIiOiJ1MlRoZTc0VG9TUkItRmFPbm5sNGh5ZFNNMWhtdVp1bVVra0tWc19xMlkwIiwidGlkIjoiNzJmOTg4YmYtODZmMS00MWFmLTkxYWItMmQ3Y2QwMTFkYjQ3IiwidW5pcXVlX25hbWUiOiJsdWNnZW5AbWljcm9zb2Z0LmNvbSIsInVwbiI6Imx1Y2dlbkBtaWNyb3NvZnQuY29tIiwidXRpIjoiOXBzcnhic1lKMHFPY1VZZXFza0RBQSIsInZlciI6IjEuMCIsInhtc19pZHJlbCI6IjEwIDEifQ.FzXW4A1lgIjpLfnVjWkL7znoavNr_Z92x6JwdGc0VhNe71V8GEoFVlDt-mZ0vH7clT7obJXkZSpBn7WMgNxZmkoLxyHQxSDiPeaKxMfBHQJ95MRxFozhkxVGO9sBLF3JiYHe-qi7pUwrY1QJYAXvYSPneSol5Kx7W8GgwEW5GrvM7x9c6UuPMcMMaDEhq1pT3KNo2Xp8kzvqL-TXAZ4-SVGEKEwJqkAC0BzIAhZ5ZdgDahz_Qy8f03hZSR1roGyzsm0BZ1jjV-xYSjAirQvrOTDWtC5044Si8o3_-AeHWvlj9ZxW1sWkY7nUyh-OEfC2ACkgXSCqlC4Oxd5R3I09Bw";
            using var client = new PowerPlatformConnectorClient("49970107-0806-e5a7-be5e-7c60e2750f01.12.common.firstrelease.azure-apihub.net", "49970107-0806-e5a7-be5e-7c60e2750f01", connectionId, () => jwt, httpClient) { SessionId = "8e67ebdc-d402-455a-b33a-304820832383" };

            string realTableName1 = "SalesOrderDetail";
            string fxTableName1 = "[SalesLT].[SalesOrderDetail]";
            string realTableName2 = "Product";
            string fxTableName2 = "[SalesLT].[Product]";

            DatasetMetadata dm = await CdpDataSource.GetDatasetsMetadataAsync(client, $"/apim/sql/{connectionId}", CancellationToken.None, logger);
            CdpDataSource cds = new CdpDataSource("pfxdev-sql.database.windows.net,SampleDB");
            
            IEnumerable<CdpTable> tables = await cds.GetTablesAsync(client, $"/apim/sql/{connectionId}", CancellationToken.None, logger);

            CdpTable connectorTable1 = tables.First(t => t.DisplayName == realTableName1);
            CdpTable connectorTable2 = tables.First(t => t.DisplayName == realTableName2);

            await connectorTable1.InitAsync(client, $"/apim/sql/{connectionId}", CancellationToken.None, logger);
            await connectorTable2.InitAsync(client, $"/apim/sql/{connectionId}", CancellationToken.None, logger);

            CdpTableValue sqlTable1 = connectorTable1.GetTableValue();
            CdpTableValue sqlTable2 = connectorTable2.GetTableValue();

            SymbolValues symbolValues = new SymbolValues()
                                            .Add(fxTableName1, sqlTable1)
                                            .Add(fxTableName2, sqlTable2);

            RuntimeConfig rc = new RuntimeConfig(symbolValues).AddService<ConnectorLogger>(logger);

            // Expression with tabular connector
            // so.ProductID -> decimal
            // so.ProductID.Name -> (so.ProductID) -> record -> string
            string expr = @"FirstN(SortByColumns(Summarize(ForAll(AddColumns('[SalesLT].[SalesOrderDetail]' As so, _Product_ROWS, LookUp('[SalesLT].[Product]' As p, so.ProductID.ProductID = p.ProductID)), {Name: ThisRecord._Product_ROWS.Name, OrderQty: ThisRecord.OrderQty }), Name, Sum(ThisGroup, OrderQty) As QuantitySold), QuantitySold, SortOrder.Descending), 1)";

            CheckResult check = engine.Check(expr, options: new ParserOptions() { AllowsSideEffects = true }, symbolTable: symbolValues.SymbolTable);
            Assert.True(check.IsSuccess, string.Join(", ", check.Errors.Select(er => er.Message)));

            //// Verify delegation is working (__retrieveSingle)
            //string ir = Regex.Replace(check.PrintIR(), "RuntimeValues_[0-9]+", "RuntimeValues_XXXX");
            //Assert.Equal<object>(@"FirstN:*[Name:s, QuantitySold:w](SortByColumns:*[Name:s, QuantitySold:w], Scope 1(Summarize:*[Name:s, QuantitySold:w], Scope 2(ForAll:*[Name:s, OrderQty:w], Scope 3(AddColumns:*[LineTotal:w, ModifiedDate:d, OrderQty:w, ProductID:r!, SalesOrderDetailID:w, SalesOrderID:r!, UnitPrice:w, UnitPriceDiscount:w, _Product_ROWS:r!, rowguid:s], Scope 4(ResolvedObject('[SalesLT].[SalesOrderDetail]:RuntimeValues_XXXX'), ""_Product_ROWS"":s, Lazy(__retrieveSingle:r!, Scope 5(Delegable(ResolvedObject('[SalesLT].[Product]:RuntimeValues_XXXX'), __eq:r*, Scope 5(Delegable(ResolvedObject('[SalesLT].[Product]:RuntimeValues_XXXX'), ""ProductID"":s, FieldAccess(ScopeAccess(Scope 4, ProductID), ProductID)), __noop:N(), """":s))), Lazy({Name: FieldAccess(ScopeAccess(Scope 3, _Product_ROWS), Name)}, {OrderQty: ScopeAccess(Scope 3, OrderQty)})), ""Name"":s, Lazy({QuantitySold: Sum:w, Scope 6(ScopeAccess(Scope 2, ThisGroup), Lazy(ScopeAccess(Scope 6, OrderQty)))})), ""QuantitySold"":s, FieldAccess(ResolvedObject(Microsoft.PowerFx.Core.Types.Enums.EnumSymbol), Descending)), Float:n(1:w))", ir);

            // Use tabular connector. Internally we'll call CdpTableValue.GetRowsInternal to get the data
            // testConnector.SetResponseFromFile(@"Responses\SQL GetItems Products.json");
            FormulaValue result = await check.GetEvaluator().EvalAsync(CancellationToken.None, rc);
        }
    }
}
