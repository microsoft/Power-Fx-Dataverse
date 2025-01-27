// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Connectors;
using Microsoft.PowerFx.Core.Tests;
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
            Assert.Equal<object>(@"FieldAccess(__retrieveSingle:r!(Delegable(ResolvedObject('Products:RuntimeValues_XXXX'), __eq:r*, Scope 1(Delegable(ResolvedObject('Products:RuntimeValues_XXXX'), {fieldName: ""ProductID"":s}, {fieldFunctions: Table:*[Value:n]()}, 680:w), __noop:N(), ResolvedObject(__noJoin()), ResolvedObject(__noopGroupBy()), ResolvedObject(__allColumns())), Name)", ir);

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
            Assert.Equal<object>(@"FieldAccess(First:r!(FirstN:r*(__retrieveMultiple:r*, Scope 1(Delegable(ResolvedObject('Products:RuntimeValues_XXXX'), __eq:r*, Scope 1(Delegable(ResolvedObject('Products:RuntimeValues_XXXX'), {fieldName: ""ProductID"":s}, {fieldFunctions: Table:*[Value:n]()}, 680:w), __noop:N(), ResolvedObject(__noJoin()), ResolvedObject(__noopGroupBy()), 1000:n, ResolvedObject(__allColumns())), Float:n(2:w))), Name)", ir);

            testConnector.SetResponseFromFile(@"Responses\SQL GetItems Products.json");
            FormulaValue result = await check.GetEvaluator().EvalAsync(CancellationToken.None, rc);

            StringValue address = Assert.IsType<StringValue>(result);
            Assert.Equal("HL Road Frame - Black, 58", address.Value);

            bool b = sqlTable.RecordType.TryGetFieldExternalTableName("ProductModelID", out string tableName, out string foreignKey);
            Assert.False(b);

            IEnumerable<string> actual = testConnector._log.ToString().Split("\r\n").Where(x => x.Contains("/items?"));
            string query = Assert.Single(actual);

            // OData query - note $filter is present but $top=1000
            Assert.Equal<object>(@" x-ms-request-url: /apim/sql/2cc03a388d38465fba53f05cd2c76181/v2/datasets/default,default/tables/%5BSalesLT%5D.%5BProduct%5D/items?api-version=2015-09-01&$filter=%28ProductID%20eq%20680%29&$top=1000", query);
        }

        [Fact]
        [Obsolete("Using Join function")]
        public async Task SAP_CDP2()
        {
            using var testConnector = new LoggingTestServer(null /* no swagger */, _output);
            var config = new PowerFxConfig(Features.PowerFxV1);
            config.EnableJoinFunction();
            var engine = new RecalcEngine(config);
            engine.EnableDelegation();

            ConsoleLogger logger = new ConsoleLogger(_output);
            using var httpClient = new HttpClient(testConnector);
            string connectionId = "b5097592f2ae498ea32458b1035634a9";
            string jwt = "eyJ0eXAiOiJK...";
            using var client = new PowerPlatformConnectorClient("49970107-0806-e5a7-be5e-7c60e2750f01.12.common.firstrelease.azure-apihub.net", "49970107-0806-e5a7-be5e-7c60e2750f01", connectionId, () => jwt, httpClient) { SessionId = "8e67ebdc-d402-455a-b33a-304820832383" };

            /* Keeping code for debugging
             
            string ShemaUpdater(string tableName, string text)
            {
                // logger.WriteLine($"{tableName} {text}");
                string newText = (string)LoggingTestServer.GetFileText($@"Responses\SAP_{tableName}_Schema.json");
                return newText;
            }
            */

            testConnector.SetResponseFromFile(@"Responses\SAP GetDataSetMetadata.json");
            DatasetMetadata dm = await CdpDataSource.GetDatasetsMetadataAsync(client, $"/apim/sapodata/{connectionId}", CancellationToken.None, logger);

            CdpDataSource cds = new CdpDataSource("https://sapes5.sapdevcenter.com/sap/opu/odata/iwbep/GWSAMPLE_BASIC/");

            testConnector.SetResponseFromFiles(@"Responses\SAP GetDataSetMetadata.json", @"Responses\SAP GetTables 2.json");
            CdpTable sapTableProductSet = await cds.GetTableAsync(client, $"/apim/sapodata/{connectionId}", "ProductSet", null, CancellationToken.None, logger);

            testConnector.SetResponseFromFiles(@"Responses\SAP GetTables 2.json");
            CdpTable sapTableBusinessPartnerSet = await cds.GetTableAsync(client, $"/apim/sapodata/{connectionId}", "BusinessPartnerSet", null, CancellationToken.None, logger);

            testConnector.SetResponseFromFile(@"Responses\SAP_ProductSet_Schema.json");
            await sapTableProductSet.InitAsync(client, $"/apim/sapodata/{connectionId}", CancellationToken.None, /* ShemaUpdater, */ logger);

            testConnector.SetResponseFromFile(@"Responses\SAP_BusinessPartnerSet_Schema.json");
            await sapTableBusinessPartnerSet.InitAsync(client, $"/apim/sapodata/{connectionId}", CancellationToken.None, /* ShemaUpdater,*/ logger);

            Assert.True(sapTableProductSet.IsInitialized);
            Assert.True(sapTableBusinessPartnerSet.IsInitialized);

            CdpTableValue sapTableValueProductSet = sapTableProductSet.GetTableValue();
            CdpTableValue sapTableValueBusinessPartnerSet = sapTableBusinessPartnerSet.GetTableValue();
            Assert.Equal<object>(
                "r*[Category:s, ChangedAt:s, CreatedAt:s, CurrencyCode:s, Depth:w, Description:s, DescriptionLanguage:s, DimUnit:s, Height:w, MeasureUnit:s, Name:s, " +
                "NameLanguage:s, Price:w, ProductID:s, SupplierID:s, SupplierName:s, TaxTarifCode:w, ToSalesOrderLineItems:~SalesOrderLineItemSet:![], ToSupplier:~BusinessPartnerSet:![], " +
                "TypeCode:s, WeightMeasure:w, WeightUnit:s, Width:w]", sapTableValueProductSet.Type.ToStringWithDisplayNames());

            string expr = "Join(ProductSet, BusinessPartnerSet, LeftRecord.SupplierID = RightRecord.BusinessPartnerID, JoinType.Left, RightRecord.EmailAddress As Email)";

            SymbolValues symbolValues = new SymbolValues()
                                               .Add("ProductSet", sapTableValueProductSet)
                                               .Add("BusinessPartnerSet", sapTableValueBusinessPartnerSet);
            RuntimeConfig rc = new RuntimeConfig(symbolValues).AddService<ConnectorLogger>(logger);

            testConnector.SetResponseFromFiles(@"Responses\SAP_BusinessPartnerSet_Schema.json", @"Responses\SAP_SalesOrderLineItemSet_Schema.json");
            CheckResult check = engine.Check(expr, options: new ParserOptions() { AllowsSideEffects = true }, symbolTable: symbolValues.SymbolTable);
            Assert.True(check.IsSuccess, string.Join("\r\n", check.Errors.Select((er, i) => $"{i:00}: {er.Message}")));

            string ir = Regex.Replace(check.PrintIR(), "RuntimeValues_[0-9]+", "RuntimeValues_XXXX");

            // Note Join delegation with navigation property: {ProductSet.SupplierID=BusinessPartnerSet.BusinessPartnerID,Left [RightRecord] <{EmailAddress As Email}> $expand=ToSupplier}
            Assert.Equal<object>(
                "__retrieveMultiple:*[Category:s, ChangedAt:s, CreatedAt:s, CurrencyCode:s, Depth:w, Description:s, DescriptionLanguage:s, DimUnit:s, Email:s, Height:w, MeasureUnit:s, Name:s, NameLanguage:s, " +
                "Price:w, ProductID:s, SupplierID:s, SupplierName:s, TaxTarifCode:w, ToSalesOrderLineItems:r!, ToSupplier:r!, TypeCode:s, WeightMeasure:w, WeightUnit:s, Width:w], Scope 1(Delegable(ResolvedObject('Produ" +
                "ctSet:RuntimeValues_XXXX'), __noop:N(), __noop:N(), ResolvedObject({ProductSet.SupplierID=BusinessPartnerSet.BusinessPartnerID,Left [RightRecord] <{EmailAddress As Email}> $expand=ToSupplier}), " +
                "ResolvedObject(__noopGroupBy()), 1000:n, ResolvedObject({ProductID,TypeCode,Category,Name,NameLanguage,Description,DescriptionLanguage,SupplierID,SupplierName,TaxTarifCode,MeasureUnit,WeightMeasure,Wei" +
                "ghtUnit,CurrencyCode,Price,Width,Depth,Height,DimUnit,CreatedAt,ChangedAt,ToSupplier,ToSalesOrderLineItems}))", ir);

            // $$ Data here are invalid as $expand isn't working yet with SAP connector
            testConnector.SetResponseFromFile(@"Responses\SAP GetData.json");
            FormulaValue result = await check.GetEvaluator().EvalAsync(CancellationToken.None, rc);

            // Get last network call
            Match network = new Regex(@"^.*x-ms-request-url.*$", RegexOptions.Multiline).Matches(testConnector._log.ToString()).Last();

            Assert.True(network.Success);

            // Note $expand=ToSupplier
            Assert.Equal<object>(@"x-ms-request-url: /apim/sapodata/b5097592f2ae498ea32458b1035634a9/datasets/https%253A%252F%252Fsapes5.sapdevcenter.com%252Fsap%252Fopu%252Fodata%252Fiwbep%252FGWSAMPLE_BASIC%252F/tables/ProductSet/items?api-version=2015-09-01&$expand=ToSupplier&$top=1000", network.Value.Trim());
        }
    }
}
