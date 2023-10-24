//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Xunit;
using FxOptionSetValue = Microsoft.PowerFx.Types.OptionSetValue;
using XrmOptionSetValue = Microsoft.Xrm.Sdk.OptionSetValue;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    // Run Dataverse execution against a live org.    
    public class LiveOrgExecutionTests
    {
        // Env var we look for to get a dataverse connection string. 
        private const string ConnectionStringVariable = "FxTestDataverseCx";

        private ServiceClient GetClient()
        {
            // "Data Source=tcp:SQL_SERVER;Initial Catalog=test;Integrated Security=True;Persist Security Info=True;";
            var cx = Environment.GetEnvironmentVariable(ConnectionStringVariable);

            // short-circuit if connection string is not set
            if (cx == null)
            {
                Skip.If(true, $"Skipping Live Dataverse tests. Set {cx} env var.");
                throw new NotImplementedException();
            }

            // https://docs.microsoft.com/en-us/power-apps/developer/data-platform/xrm-tooling/use-connection-strings-xrm-tooling-connect
            // For example:
            // $"Url=https://aurorabapenv67c10.crm10.dynamics.com/; Username={username}; Password={password}; authtype=OAuth";

            var svcClient = new ServiceClient(cx)
            {
                EnableAffinityCookie = true,
                UseWebApi = false
            };

            return svcClient;
        }

        [SkippableFact]
        public void ExecuteViaInterpreterFirst()
        {
            string tableName = "TableTest1S";
            List<IDisposable> disposableObjects = null;

            try
            {
                var result = RunDataverseTest(tableName, "First(TableTest1S)", out disposableObjects);
                var obj = result.ToObject() as Entity;

                Assert.Equal("Name1", obj.Attributes["crcef_name"].ToString());
                Assert.Equal(1, (obj.Attributes["crcef_properties"] as XrmOptionSetValue).Value); // Choice1
                Assert.Equal(17, obj.Attributes["crcef_score"]);

                DateTime dt = (DateTime)obj.Attributes["crcef_creationdate"];
                Assert.Equal(new DateTime(2022, 12, 27, 23, 0, 0), dt); // UTC time
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableTheory]
        [InlineData("LookUp(JV1S, reg = LookUp(JVLookups, Name <> \"test1\"))", "Result is Blank", true)]
        [InlineData("LookUp(JV1S, reg = LookUp(JVLookups, Name = \"test1\"))", "Name1", false)]
        [InlineData("LookUp(JV1S, reg <> LookUp(JVLookups, Name <> \"test1\"))", "Name2", false)]
        [InlineData("LookUp(JV1S, reg <> LookUp(JVLookups, Name = \"test1\"))", "Name2", false)]
        [InlineData("LookUp(JV1S, reg = {test:1})", "Result is Blank", true)]
        [InlineData("LookUp(JV1S, {test:1} = reg)", "Result is Blank", true)]
        [InlineData("LookUp(JV1S, reg <> {test:1})", "Name2", false)]
        [InlineData("LookUp(JV1S, {test:1} <> reg)", "Name2", false)]
        public void ExecuteViaInterpreterPolymorphicComparison(string expression, string expected, bool isResultEmpty = false)
        {
            var tableName = new string[] { "JV1S", "JVLookups" };

            List<IDisposable> disposableObjects = null;

            try
            {
                var result = RunDataverseTest(tableName, expression, out disposableObjects);
                if (!isResultEmpty)
                {
                    var obj = result.ToObject() as Entity;
                    Assert.Equal(expected, obj.Attributes["crcbc_name"].ToString());
                }
                else
                {
                    Assert.Null(result.ToObject());
                }
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableTheory]
        [InlineData("AsType(Index(JV1S, 2).poly_field, JVLookups).Name", "test1", false)]
        [InlineData("AsType(Index(JV1S, 1).poly_field, JVLookups)", "Result is Error", true)] // polymorphic field is of jvlookups2 type.
        [InlineData("AsType(Index(JV1S, 1).poly_field, JVLookup2S).Name", "jvlookup2 Name1", false)] // polymorphic field is of jvlookups2 type.
        public void ExecuteViaInterpreterAsType(string expression, object expected, bool isResultError = false)
        {
            var tableName = new string[] { "JV1S", "JVLookups", "JVLookup2S" };

            List<IDisposable> disposableObjects = null;

            try
            {
                var result = RunDataverseTest(tableName, expression, out disposableObjects);
                if (!isResultError)
                {
                    Assert.Equal(expected, result.ToObject());
                }
                else
                {
                    Assert.IsType<ErrorValue>(result);
                }
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableTheory]
        [InlineData("Index(Accounts, 1).Tasks", 0)]
        [InlineData("LookUp(Contacts, 'Full Name' = \"Mike\").Accounts", 2)]

        // If Tasks field was empty, returns empty table.
        [InlineData("Index(Accounts, 2).Tasks", 2)]

        public void ExecuteViaInterpreterOneToMany(string expression, int expected)
        {
            var tableName = new string[] { "account", "task", "contact" };

            List<IDisposable> disposableObjects = null;

            try
            {
                var result = RunDataverseTest(tableName, expression, out disposableObjects);
                Assert.True(result is TableValue);
                Assert.Equal(expected, ((TableValue)result).Count());
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableTheory]
        [InlineData("AsType(Blank(), JVLookups)")]
        [InlineData("AsType({test:1}, JVLookups)")]
        [InlineData("AsType(Index(JV1S, 1).reg, [1,2])")]        
        public void ExecuteViaInterpreterAsType_Negative(string expression)
        {
            var tableName = new string[] { "JVLookups" };

            List<IDisposable> disposableObjects = null;

            try
            {
                RunDataverseTest(tableName, expression, out disposableObjects, isCheckSucess: false);
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void ExecuteViaInterpreterFirstWithDisplayName()
        {
            string tableName = "TableTest1S";
            List<IDisposable> disposableObjects = null;

            try
            {
                var result1 = RunDataverseTest(tableName, "First(TableTest1S).Name", out disposableObjects, out var engine, out var symbols, out var runtimeConfig);

                var check2 = engine.Check("First(TableTest1S).Name", symbolTable: symbols);
                Assert.True(check2.IsSuccess);

                var run2 = check2.GetEvaluator();
                var result2 = run2.EvalAsync(CancellationToken.None, runtimeConfig).Result;

                Assert.Equal(result1.ToObject(), result2.ToObject());
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void ExecuteViaInterpreterFirstN()
        {
            string tableName = "TableTest1S";
            List<IDisposable> disposableObjects = null;

            try
            {
                var result1 = RunDataverseTest(tableName, "FirstN(TableTest1S, 5)", out disposableObjects, out var engine, out var symbols, out var runtimeConfig);

                var dv1 = result1 as DataverseTableValue;
                var r1 = dv1.Rows.ToList();

                var check2 = engine.Check("FirstN(TableTest1S, 1)", symbolTable: symbols);
                Assert.True(check2.IsSuccess);

                var run2 = check2.GetEvaluator();
                var result2 = run2.EvalAsync(CancellationToken.None, runtimeConfig).Result;

                var dv2 = result2 as DataverseTableValue;
                var r2 = dv2.Rows.ToList();

                Assert.Equal(5, r1.Count);
                Assert.Single(r2);
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void ExecuteViaInterpreterCollectNoKey()
        {
            string tableName = "account";
            string expr = "Collect(Accounts,{'Account Name': \"test\", 'Primary Contact':First(Contacts)})";
            List<IDisposable> disposableObjects = null;

            try
            {
                FormulaValue result = RunDataverseTest(tableName, expr, out disposableObjects);

                var obj = result.ToObject() as Entity;
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void ExecuteViaInterpreterCollectAndRefresh()
        {
            string tableName = "Accounts";
            string expr = "Collect(Accounts, {'Account Name': \"Account1\" }); Refresh(Accounts); Accounts";
            List<IDisposable> disposableObjects = null;

            try
            {
                FormulaValue result = RunDataverseTest(tableName, expr, out disposableObjects);
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableTheory]
        [InlineData("Collect(Contacts, {'Accounts (cr28b_account_someRef_contact)': Table(First(Accounts))})")]
        public void ExecuteViaInterpreterCollectOneToManyNotSupported(string expression)
        {
            var tableName = new string[] { "account", "contact" };

            List<IDisposable> disposableObjects = null;

            try
            {
                var result = RunDataverseTest(tableName, expression, out disposableObjects);
                Assert.True(result is ErrorValue);
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableTheory]
        [InlineData("Collect(Accounts, { 'Primary Contact': First(Contacts)})")]
        public void ExecuteViaInterpreterCollectManyToOne(string expression)
        {
            var tableName = new string[] { "account", "contact" };

            List<IDisposable> disposableObjects = null;

            try
            {
                var result = RunDataverseTest(tableName, expression, out disposableObjects);
                Assert.True(result is RecordValue);
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public async Task SlowRepeatingLookup()
        {
            var token = "";

            if (string.IsNullOrEmpty(token))
            {
                Skip.If(true, "No token specified");
            }

            var engine = new RecalcEngine();
            var serviceClient = new ServiceClient(new Uri("https://org1c5ae4ff.crm10.dynamics.com/"), (s) => Task.FromResult(token));
            var dec = new DataverseEntityCache(serviceClient, maxEntries: 500, cacheLifeTime: new TimeSpan(0, 10, 0));

            // Simulate first request for doc with id 29
            var dvc = new DataverseConnection(dec, new XrmMetadataProvider(serviceClient));
            dvc.AddTable("Accounts", "account");
            dvc.AddTable("Contacts", "contact");

            var recalcEngine = new RecalcEngine();

            var repeatingTable = (TableValue)await recalcEngine.Check("Contacts", symbolTable: dvc.Symbols)
                .GetEvaluator().EvalAsync(CancellationToken.None, symbolValues: dvc.SymbolValues).ConfigureAwait(false);

            foreach (var record in repeatingTable.Rows)
            {
                recalcEngine.UpdateVariable("ThisItem", record.ToFormulaValue());

                await recalcEngine.Check("ThisItem.'Full Name' & \" - First Account: \" & First(Filter(Accounts, ThisRecord.'Primary Contact'.Contact = ThisItem.Contact)).'Account Name'", symbolTable: dvc.Symbols)
                    .GetEvaluator().EvalAsync(CancellationToken.None, symbolValues: dvc.SymbolValues).ConfigureAwait(false);
            }

            Console.WriteLine(dec.CacheSize);
        }

        [SkippableFact]
        public void ExecuteViaInterpreterInsertRowsAsyncWithConflict()
        {
            string tableName = "Table2";
            var prefix = Guid.NewGuid().ToString().Replace("-", string.Empty).ToUpperInvariant();
            List<IDisposable> disposableObjects = null;

            try
            {
                RunDataverseTest(tableName, null, out disposableObjects, out var engine, out var symbols, out var runtimeConfig, async: true);

                Parallel.For(1, 5, new ParallelOptions() { MaxDegreeOfParallelism = 8 }, (i) =>
                {
                    object obj = InsertRow(symbols, runtimeConfig, prefix, engine, i);

                    Assert.NotNull(obj);
                    Assert.IsType<Entity>(obj);
                });

                // Insert last row a second time, in a DV table that has a Key constraint
                var obj5 = InsertRow(symbols, runtimeConfig, prefix, engine, 4);

                Assert.NotNull(obj5);
                Assert.IsType<ErrorValue>(obj5);

                var ev5 = obj5 as ErrorValue;

                Assert.Equal(1, ev5.Errors.Count);
                Assert.Equal("Error in CreateAsync: [DataverseOperationException] A record that has the attribute values Name already exists. " +
                                "The entity key Key requires that this set of attributes contains unique values. Select unique values and try again.\r\n" +
                                "[HttpOperationException] Operation returned an invalid status code 'PreconditionFailed'", ev5.Errors.First().Message);
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        private static object InsertRow(ReadOnlySymbolTable symbols, ReadOnlySymbolValues runtimeConfig, string prefix, RecalcEngine engine, int i)
        {
            string expr = $"Collect(Table2, {{ Name: \"{prefix}-{i}\" }})";
            Console.WriteLine("Running: {0}", expr);

            var check = engine.Check(expr, symbolTable: symbols, options: new ParserOptions() { AllowsSideEffects = true, NumberIsFloat = PowerFx2SqlEngine.NumberIsFloat });
            Assert.True(check.IsSuccess, string.Join("\r\n", check.Errors.Select(ee => ee.Message)));

            var run = check.GetEvaluator();
            var result = run.EvalAsync(CancellationToken.None, runtimeConfig).Result;
            var obj = result.ToObject();
            return obj;
        }

        [SkippableFact]
        public void ExecuteViaInterpreterFilter()
        {
            string tableName = "TableTest1S";
            string expr = "First(Filter(TableTest1S, Name = \"N2\"))";
            List<IDisposable> disposableObjects = null;

            try
            {
                FormulaValue result = RunDataverseTest(tableName, expr, out disposableObjects);

                var obj = result.ToObject() as Entity;
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void ExecuteViaInterpreterPatch()
        {
            string tableName = "TableTest1S";
            double newValue = new Random().Next() / 1000.0;
            Console.WriteLine($"Setting new value to {newValue}");
            var expr = $"Patch(TableTest1S, First(Filter(TableTest1S, Name = \"N1a\")), {{ YYY: {newValue} }})";

            List<IDisposable> disposableObjects = null;

            try
            {
                FormulaValue result = RunDataverseTest(tableName, expr, out disposableObjects);

                var obj = result.ToObject() as Entity;
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void FormulaColumnTest()
        {
            string tableName = "PFxTestTables";
            var newName = "ABC";

            // Formula: Concatenate(Name, " SULFIX")
            var formulaColumnValue = newName + " SULFIX";

            var expr = $"Patch(PFxTestTables, {{ PFxTestTable : GUID(\"0090411a-4397-ed11-aad1-000d3a58b59f\")}}, {{ Name : \"{newName}\" }})";

            List<IDisposable> disposableObjects = null;

            try
            {
                FormulaValue result = RunDataverseTest(tableName, expr, out disposableObjects);

                var obj = result.ToObject() as Entity;

                Assert.Equal(formulaColumnValue, obj.Attributes["cr959_formulacolumn"]);
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void PatchingFormulaColumnTest()
        {
            string tableName = "PFxTestTables";

            // Formula: Concatenate(Name, " SULFIX")
            var formulaColumnValue = "Bar" + " SULFIX";

            var expr = "Patch(PFxTestTables, { PFxTestTable : GUID(\"61574f20-4397-ed11-aad1-000d3a58b59f\")}, { 'Formula column' : \"New value\" })";

            List<IDisposable> disposableObjects = null;

            try
            {
                FormulaValue result = RunDataverseTest(tableName, expr, out disposableObjects);

                var obj = result.ToObject() as Entity;

                // Formula column has not changed.
                Assert.Equal(formulaColumnValue, obj.Attributes["cr959_formulacolumn"]);
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void PatchingWithInvalidValueTest()
        {
            string tableName = "PFxTestTables";

            // Column 'Limited number' has a range limit of 0 - 100.
            var expr = "Patch(PFxTestTables, { PFxTestTable : GUID(\"61574f20-4397-ed11-aad1-000d3a58b59f\")}, { 'Limited number' : 200 })";

            List<IDisposable> disposableObjects = null;

            try
            {
                FormulaValue result = RunDataverseTest(tableName, expr, out disposableObjects);

                Assert.IsType<ErrorValue>(result);
                Assert.Contains("A validation error occurred", ((ErrorValue)result).Errors.First().Message);
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void ExecuteViaInterpreterPatchWithConflict()
        {
            string tableName = "Table2";
            var expr = @"Patch(Table2, { Table2 : GUID(""c9ebbaac-5728-ed11-9db2-0022482aea8f"") }, { Name : ""N1"" })";

            List<IDisposable> disposableObjects = null;

            try
            {
                FormulaValue result = RunDataverseTest(tableName, expr, out disposableObjects);

                Assert.NotNull(result);
                Assert.IsType<ErrorValue>(result);

                var err = result as ErrorValue;
                Assert.Equal(1, err.Errors.Count);

                string errMsg = "Error in UpdateAsync: [DataverseOperationException] A record that has the attribute values Name already exists. " +
                                "The entity key Key requires that this set of attributes contains unique values. Select unique values and try again.\r\n" +
                                "[HttpOperationException] Operation returned an invalid status code 'PreconditionFailed'";

                Assert.Equal(errMsg, err.Errors.First().Message);
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void ExecuteViaInterpreterPatchWithId()
        {
            string tableName = "Table2";
            var expr = $"Patch(Table2, {{ Table2 : GUID(\"b8e7086e-c22d-ed11-9db2-0022482aea8f\")}}, {{ Name : \"XYZ\" }})";
            List<IDisposable> disposableObjects = null;

            try
            {
                FormulaValue result = RunDataverseTest(tableName, expr, out disposableObjects);

                Assert.NotNull(result);
                Assert.IsType<Entity>(result.ToObject());
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void ExecuteViaInterpreterPatchWithIdBlank()
        {
            string tableName = "Table2";
            var expr = $"Patch(Table2, {{ Table2 : GUID(\"b8e7086e-c22d-ed11-9db2-0022482aea8f\")}}, {{ Name : Blank() }})";
            List<IDisposable> disposableObjects = null;

            try
            {
                FormulaValue result = RunDataverseTest(tableName, expr, out disposableObjects);

                Assert.NotNull(result);
                Assert.IsType<Entity>(result.ToObject());
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void ExecuteViaInterpreterPatchWithDateTime()
        {
            string tableName = "Table2";

            var dt = DateTime.Now;
            long tk = dt.Ticks % 10000000;
            dt = dt.Add(new TimeSpan(-tk)); // Make sure we have no milli/micro-second

            var expr = $"Patch(Table2, {{ Table2 : GUID(\"b8e7086e-c22d-ed11-9db2-0022482aea8f\")}}, {{ MyDate : DateTimeValue(\"{dt.ToString(new CultureInfo("en-US"))}\") }})"; // "9/7/2022 5:30:44 PM" (local time, not UTC)

            List<IDisposable> disposableObjects = null;

            try
            {
                FormulaValue result = RunDataverseTest(tableName, expr, out disposableObjects, out var engine, out var runtimeConfig);

                Assert.NotNull(result);
                Assert.IsType<Entity>(result.ToObject());

                var expr2 = $"First(Filter(Table2, Table2 = GUID(\"b8e7086e-c22d-ed11-9db2-0022482aea8f\"))).MyDate";
                var result2 = engine.EvalAsync(expr2, CancellationToken.None, new ParserOptions() { AllowsSideEffects = true, NumberIsFloat = PowerFx2SqlEngine.NumberIsFloat }, runtimeConfig: new RuntimeConfig(runtimeConfig)).Result;
                Assert.NotNull(result2);

#pragma warning disable CS0618 // Type or member is obsolete
                DateTime dt2 = (result2 as DateTimeValue).Value;
                Assert.Equal(dt, dt2);
#pragma warning restore CS0618 // Type or member is obsolete
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void ExecuteViaInterpreterRead()
        {
            string tableName = "Table2";
            int wn = new Random().Next(1000000);
            decimal dc = wn / 100m;
            float ft = wn / 117f;
            double cy = ft;

            var expr = $"First(Filter(Table2, Table2 = GUID(\"b8e7086e-c22d-ed11-9db2-0022482aea8f\")))";

            List<IDisposable> disposableObjects = null;

            try
            {
                FormulaValue result = RunDataverseTest(tableName, expr, out disposableObjects, out var engine, out var runtimeConfig);

                Assert.NotNull(result);
                Assert.IsType<Entity>(result.ToObject());
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void ExecuteViaInterpreterPatchWithNumbers()
        {
            string tableName = "Table2";
            int wn = new Random().Next(1000000);
            decimal dc = wn / 100m;
            float ft = ((int)((wn / 117) * 100d)) / 100;
            double cy = ft;

            var expr = $"Patch(Table2, {{ Table2: GUID(\"b8e7086e-c22d-ed11-9db2-0022482aea8f\")}}, {{ WholeNumber: {wn}, Decimal: {dc}, Float: {ft}, 'Currency (crcef2_currency)': {cy} }})";

            List<IDisposable> disposableObjects = null;

            try
            {
                FormulaValue result = RunDataverseTest(tableName, expr, out disposableObjects, out var engine, out var runtimeConfig);

                Assert.NotNull(result);
                Assert.IsType<Entity>(result.ToObject());

                var expr2 = $"First(Filter(Table2, Table2 = GUID(\"b8e7086e-c22d-ed11-9db2-0022482aea8f\")))";
                var result2 = engine.EvalAsync(expr2, CancellationToken.None, new ParserOptions() { AllowsSideEffects = true, NumberIsFloat = PowerFx2SqlEngine.NumberIsFloat }, runtimeConfig: new RuntimeConfig(runtimeConfig)).Result;
                Assert.NotNull(result2);

                Entity e = (Entity)result2.ToObject();
                int wn2 = e.GetAttributeValue<int>("crcef2_wholenumber");
                decimal dc2 = e.GetAttributeValue<decimal>("crcef2_decimal");
                float ft2 = (float)e.GetAttributeValue<double>("crcef2_float");
                double cy2 = (double)e.GetAttributeValue<Money>("crcef2_currency").Value;

                Assert.Equal(wn, wn2);
                Assert.Equal(dc, dc2);
                Assert.Equal(ft, ft2);
                Assert.Equal(cy, cy2);
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void ExecuteViaInterpreterPatchWithOptionSet()
        {
            string tableName = "TableTest1S";
            string expr = $"First(Filter(TableTest1S, TableTest1 = GUID(\"4ed3cf85-651d-ed11-9db1-0022482aea8f\"))).Properties2";
            FxOptionSetValue os = null;
            List<IDisposable> disposableObjects = null;

            try
            {
                FormulaValue result = RunDataverseTest(tableName, expr, out disposableObjects, out var engine, out var symbols, out var runtimeConfig);
                Assert.NotNull(result);

                os = result as FxOptionSetValue;

                string currentValue = os?.DisplayName;
                string newValue = currentValue == "Value1" ? "'Properties2 (TableTest1S)'.Value2" : "'Properties2 (TableTest1S)'.Value1";
                string expr2 = $"Patch(TableTest1S, {{ TableTest1 : GUID(\"4ed3cf85-651d-ed11-9db1-0022482aea8f\")}}, {{ Properties2: {newValue} }})";

                FormulaValue result2 = engine.EvalAsync(expr2, CancellationToken.None, new ParserOptions() { AllowsSideEffects = true, NumberIsFloat = PowerFx2SqlEngine.NumberIsFloat }, symbolTable: symbols, runtimeConfig: new RuntimeConfig(runtimeConfig)).Result;
                Assert.NotNull(result2);
                Assert.IsType<Entity>(result2.ToObject());
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }

            try
            {
                FormulaValue result3 = RunDataverseTest(tableName, expr, out disposableObjects);
                Assert.NotNull(result3);

                FxOptionSetValue os3 = result3 as FxOptionSetValue;
                Assert.NotEqual(os.Option, os3.Option);
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void ExecuteViaInterpreterPatchWithInvalidId()
        {
            string tableName = "Table2";
            var expr = $"Patch(Table2, {{ Table2 : GUID(\"b8e7086e-ffff-ffff-ffff-0022482aea8f\")}}, {{ Name : \"XYZ\" }})";
            List<IDisposable> disposableObjects = null;

            try
            {
                FormulaValue result = RunDataverseTest(tableName, expr, out disposableObjects);

                Assert.NotNull(result);
                Assert.IsType<ErrorValue>(result);

                var err = result as ErrorValue;
                Assert.Equal(1, err.Errors.Count);

                string errMsg = "Error in RetrieveAsync: [FaultException<OrganizationServiceFault>] crcef2_table2 With Id = b8e7086e-ffff-ffff-ffff-0022482aea8f Does Not Exist";
                Assert.Equal(errMsg, err.Errors.First().Message);
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void ExecuteViaInterpreterPatchWithInvalidIdAsync()
        {
            string tableName = "Table2";
            string expr = $"Patch(Table2, {{ Table2 : GUID(\"b8e7086e-ffff-ffff-ffff-0022482aea8f\")}}, {{ Name : \"XYZ\" }})";

            List<IDisposable> disposableObjects = null;

            try
            {
                FormulaValue result = RunDataverseTest(tableName, expr, out disposableObjects, async: true);

                Assert.NotNull(result);
                Assert.IsType<ErrorValue>(result);

                var err = result as ErrorValue;
                Assert.Equal(1, err.Errors.Count);

                string errMsg = "Error in RetrieveAsync: [FaultException<OrganizationServiceFault>] crcef2_table2 With Id = b8e7086e-ffff-ffff-ffff-0022482aea8f Does Not Exist";
                Assert.Equal(errMsg, err.Errors.First().Message);
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void ExecuteViaInterpreterRemoveWithId()
        {
            string tableName = "Table2";
            string newName = $"N7-{Guid.NewGuid().ToString().ToUpperInvariant().Replace("-", "")}";
            string expr = $"Collect(Table2, {{ Name: \"{newName}\" }} ); Remove(Table2, First(Filter(Table2, crcef2_name = \"{newName}\")))";
            List<IDisposable> disposableObjects = null;

            try
            {
                FormulaValue result = RunDataverseTest(tableName, expr, out disposableObjects);
                bool bv = (bool)result.ToObject();

                Assert.True(bv);
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void ExecuteViaInterpreterRemoveByName()
        {
            string tableName = "Table2";
            string newName = $"N7-{Guid.NewGuid().ToString().ToUpperInvariant().Replace("-", "")}";
            string expr = $"Collect(Table2, {{ Name: \"{newName}A\", Name2: \"{newName}\" }}); Collect(Table2, {{ Name: \"{newName}B\", Name2: \"{newName}\" }}); Remove(Table2, {{ crcef2_name2: \"{newName}\" }})";
            List<IDisposable> disposableObjects = null;

            try
            {
                FormulaValue result = RunDataverseTest(tableName, expr, out disposableObjects);
                ErrorValue ev = (ErrorValue)result.ToObject();

                Assert.Equal("Dataverse record doesn't contain primary Id, of Guid type", string.Join("|", ev.Errors.Select(ee => ee.Message)));
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableTheory]
        [InlineData("If(First(PFxTables).Choice, \"YES\", \"NO\")", "NO")]
        [InlineData("If(First(PFxTables).Choice = 'Choice (PFxTables)'.Positive, \"YES\", \"NO\")", "NO")]
        [InlineData("Text(First(PFxTables).Choice)", "Negative")]
        [InlineData("Text(First(PFxTables).Choice) & 'Choice (PFxTables)'.Positive", "NegativePositive")]
        [InlineData("Patch(PFxTables, First(PFxTables), {'Choice':'Choice (PFxTables)'.Negative,'Name':\"PATCH1\"});First(PFxTables).Name", "PATCH1")]
        [InlineData("Collect(PFxTables, {'Choice':'Choice (PFxTables)'.Positive,'Name':\"COLLECT1\"});LookUp(PFxTables, Name = \"COLLECT1\").Name", "COLLECT1")]
        [InlineData("Collect(PFxTables, {'Choice':'Choice (PFxTables)'.Positive,'Name':\"POSITIVE1\"});If(LookUp(PFxTables, Name = \"COLLECT1\").Choice, \"Affirmitive\", \"Nope\")", "Affirmitive")]
        public void BooleanOptionSetCoercionTest(string expr, string expected)
        {
            string tableName = "PFxTables";

            List<IDisposable> disposableObjects = null;

            try
            {
                StringValue result = RunDataverseTest(tableName, expr, out disposableObjects, async: true) as StringValue;

                Assert.NotNull(result);
                Assert.Equal(expected, result.Value);
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableTheory]
        [InlineData("First(Tasks).Owner")]
        [InlineData("With( {r : First(Tasks).Owner}, r)")]
        public void ExecuteViaInterpreterWithAndPolymorphic(string expression)
        {
            var tableName = new string[] { "account", "task" };

            List<IDisposable> disposableObjects = null;

            try
            {
                var result = RunDataverseTest(tableName, expression, out disposableObjects);
                Assert.True(result is RecordValue);
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void AllNotSupportedAttributesTest()
        {
            string tableName = "PFxColumns";
            string baseExpr = "First(PFxColumns)";

            var expectedErrors = new List<string>()
            {
                "Hyperlink column type not supported.",
                "Image column type not supported.",
                "File column type not supported.",
            };

            List<IDisposable> disposableObjects = null;

            try
            {
                var dataverseResult = RunDataverseTest(tableName, baseExpr, out disposableObjects, async: true) as DataverseRecordValue;

                foreach (var attr in dataverseResult.Entity.Attributes.Where(attr => attr.Key.Contains("cr100_aa")))
                {
                    try
                    {
                        var expr = string.Format("{0}.{1}", baseExpr, attr.Key);

                        var result = RunDataverseTest(tableName, expr, out disposableObjects, async: true);

                        if (result is ErrorValue errorValue)
                        {
                            Assert.Contains(errorValue.Errors.First().Message, expectedErrors);
                        }
                        else
                        {
                            Assert.IsAssignableFrom<FormulaValue>(result);
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public void MultiselectFieldTest()
        {
            string tableName = "PFxTables";
            string expr = "Concat(First(PFxTables).AAMultipleChoices, Value)";

            List<IDisposable> disposableObjects = null;

            try
            {
                var result = RunDataverseTest(tableName, expr, out disposableObjects);

                Assert.IsType<StringValue>(result);
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableFact]
        public async Task TestAllTableAllFields()
        {
            ServiceClient svcClient = GetClient();
            List<IDisposable> disposableObjects = new List<IDisposable>() { svcClient };

            try
            {
                var displayNameProvider = svcClient.GetTableDisplayNames();
                var allmetadatas = svcClient.GetAllValidEntityMetadata(entityFilters: EntityFilters.Entity);

                foreach (var metadata in allmetadatas)
                {
                    if (!displayNameProvider.TryGetLogicalOrDisplayName(new DName(metadata.LogicalName), out var logicalname, out var displayName))
                    {
                        continue;
                    }

                    var engine = new RecalcEngine();
                    var expr = $"First({logicalname})";
                    var dv = SingleOrgPolicy.New(svcClient);

                    var check = engine.Check(expr, symbolTable: dv.Symbols);

                    Assert.True(check.IsSuccess);

                    foreach (var fieldname in check.ReturnType._type.GetAllNames(DPath.Root))
                    {
                        var localEngine = new RecalcEngine();

                        expr = $"First({logicalname}).'{fieldname.Name}'";

                        var localCheck = localEngine.Check(expr, symbolTable: dv.Symbols);

                        Assert.True(localCheck.IsSuccess);

                        var formulaValue = await localCheck.GetEvaluator().EvalAsync(CancellationToken.None);

                        Assert.IsNotType<ErrorValue>(formulaValue);
                    }
                }
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        [SkippableTheory]
        [InlineData("First('Appointments').appointment_PostRoles")]
        [InlineData("With({x:First('Appointments')}, x.appointment_PostRoles)")]
        [InlineData("ForAll('Appointments', ThisRecord.appointment_PostRoles)")]
        public void UnsupportedRelationshipEntitiesTest(string expression)
        {
            List<IDisposable> disposableObjects = null;

            try
            {
                var tableName = new string[] { };

                RunDataverseTest(tableName, null, out disposableObjects, out RecalcEngine engine, out ReadOnlySymbolTable symbols, out _, false);

                CheckResult check = engine.Check(expression, symbolTable: symbols, options: new ParserOptions() { AllowsSideEffects = true });
                Assert.False(check.IsSuccess);
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }
        }

        private FormulaValue RunDataverseTest(string tableName, string expr, out List<IDisposable> disposableObjects, bool async = false)
        {
            return RunDataverseTest(tableName, expr, out disposableObjects, out _, out _, out _, async);
        }

        private FormulaValue RunDataverseTest(string[] tableName, string expr, out List<IDisposable> disposableObjects, bool isCheckSucess = true, bool async = false)
        {
            return RunDataverseTest(tableName, expr, out disposableObjects, out _, out _, out _, isCheckSucess, async);
        }

        private FormulaValue RunDataverseTest(string tableName, string expr, out List<IDisposable> disposableObjects, out RecalcEngine engine, out ReadOnlySymbolValues runtimeConfig, bool async = false)
        {
            return RunDataverseTest(tableName, expr, out disposableObjects, out engine, out _, out runtimeConfig, async);
        }

        private FormulaValue RunDataverseTest(string[] tableNames, string expr, out List<IDisposable> disposableObjects, out RecalcEngine engine, out ReadOnlySymbolTable symbols, out ReadOnlySymbolValues runtimeConfig, bool isCheckSucess = true, bool async = false)
        {
            ServiceClient svcClient = GetClient();
            XrmMetadataProvider xrmMetadataProvider = new XrmMetadataProvider(svcClient);
            disposableObjects = new List<IDisposable>() { svcClient };

            DataverseConnection dv = null;

            if (async)
            {
                var asyncClient = new DataverseAsyncClient(svcClient);
                disposableObjects.Add(asyncClient);
                dv = new DataverseConnection(asyncClient, new XrmMetadataProvider(svcClient));
            }
            else
            {
                dv = new DataverseConnection(svcClient);
            }

            dv.AddTable("Accounts", "account");
            dv.AddTable("Tasks", "task");
            dv.AddTable("Note", "annotation");
            dv.AddTable("Contacts", "contact");
            dv.AddTable("Appointments", "appointment");

            symbols = ReadOnlySymbolTable.Compose(dv.Symbols);

            foreach (string tableName in tableNames)
            {
                string logicalName = null;
                TableValue tableValue = null;
                if (!dv.TryGetVariableName(tableName, out _))
                {
                    bool b1 = xrmMetadataProvider.TryGetLogicalName(tableName, out logicalName);
                    Assert.True(b1);
                    tableValue = dv.AddTable(variableName: tableName, tableLogicalName: logicalName);
                    Assert.NotNull(tableValue);
                    symbols = ReadOnlySymbolTable.Compose(symbols, dv.GetRowScopeSymbols(tableLogicalName: logicalName));
                }
                else
                {
                    symbols = ReadOnlySymbolTable.Compose(symbols, dv.GetRowScopeSymbols(tableLogicalName: tableName));
                }

            }

            Assert.NotNull(symbols);

            var config = new PowerFxConfig();
            config.SymbolTable.EnableMutationFunctions();
            engine = new RecalcEngine(config);
            runtimeConfig = dv.SymbolValues;

            if (string.IsNullOrEmpty(expr))
            {
                return null;
            }

            CheckResult check = engine.Check(expr, symbolTable: symbols, options: new ParserOptions() { AllowsSideEffects = true });
            Assert.Equal(isCheckSucess, check.IsSuccess); // string.Join("\r\n", check.Errors.Select(ee => ee.Message))

            if (!isCheckSucess)
            {
                return null;
            }

            IExpressionEvaluator run = check.GetEvaluator();
            FormulaValue result = run.EvalAsync(CancellationToken.None, runtimeConfig).Result;

            return result;
        }

        private static readonly Dictionary<string, string> PredefinedTables = new()
        {
            { "Accounts", "account" },
            { "Tasks", "task" },
            { "Note", "annotation" },
            { "Contacts", "contact" }
        };

        private FormulaValue RunDataverseTest(string tableName, string expr, out List<IDisposable> disposableObjects, out RecalcEngine engine, out ReadOnlySymbolTable symbols, out ReadOnlySymbolValues runtimeConfig, bool async = false)
        {
            ServiceClient svcClient = GetClient();
            XrmMetadataProvider xrmMetadataProvider = new XrmMetadataProvider(svcClient);
            disposableObjects = new List<IDisposable>() { svcClient };

            if (!PredefinedTables.TryGetValue(tableName, out string logicalName))
            {
                bool b1 = xrmMetadataProvider.TryGetLogicalName(tableName, out logicalName);
                Assert.True(b1);
            }

            DataverseConnection dv = null;

            if (async)
            {
                var asyncClient = new DataverseAsyncClient(svcClient);
                disposableObjects.Add(asyncClient);
                dv = new DataverseConnection(asyncClient, new XrmMetadataProvider(svcClient));
            }
            else
            {
                dv = new DataverseConnection(svcClient);
            }
            TableValue tableValue = dv.AddTable(variableName: tableName, tableLogicalName: logicalName);
            symbols = ReadOnlySymbolTable.Compose(dv.GetRowScopeSymbols(tableLogicalName: logicalName), dv.Symbols);

            Assert.NotNull(tableValue);
            Assert.NotNull(symbols);

            var config = new PowerFxConfig();
            config.SymbolTable.EnableMutationFunctions();
            engine = new RecalcEngine(config);
            runtimeConfig = dv.SymbolValues;

            if (string.IsNullOrEmpty(expr))
            {
                return null;
            }

            CheckResult check = engine.Check(expr, symbolTable: symbols, options: new ParserOptions() { AllowsSideEffects = true, NumberIsFloat = PowerFx2SqlEngine.NumberIsFloat });
            Assert.True(check.IsSuccess, string.Join("\r\n", check.Errors.Select(ee => ee.Message)));

            IExpressionEvaluator run = check.GetEvaluator();
            FormulaValue result = run.EvalAsync(CancellationToken.None, runtimeConfig).Result;

            return result;
        }

        private void DisposeObjects(List<IDisposable> objects)
        {
            if (objects != null)
            {
                foreach (IDisposable obj in objects)
                {
                    obj.Dispose();
                }
            }
        }
    }

    internal class DataverseAsyncClient : IDataverseServices, IDisposable, IDataverseRefresh
    {
        private readonly ServiceClient _svcClient;
        private bool disposedValue;

        public DataverseAsyncClient(ServiceClient client)
        {
            _svcClient = client ?? throw new ArgumentNullException(nameof(client));
        }

        public virtual async Task<DataverseResponse<Guid>> CreateAsync(Entity entity, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => _svcClient.CreateAsync(entity, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult(), "Create");
        }

        public virtual async Task<DataverseResponse> DeleteAsync(string entityName, Guid id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => _svcClient.DeleteAsync(entityName, id, cancellationToken).ConfigureAwait(false), "Delete");
        }

        public virtual HttpResponseMessage ExecuteWebRequest(HttpMethod method, string queryString, string body, Dictionary<string, List<string>> customHeaders, string contentType = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _svcClient.ExecuteWebRequest(method, queryString, body, customHeaders, contentType, cancellationToken);
        }

        public virtual async Task<DataverseResponse<Entity>> RetrieveAsync(string entityName, Guid id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => _svcClient.RetrieveAsync(entityName, id, new ColumnSet(true), cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult(), "Retrieve");
        }

        public virtual async Task<DataverseResponse<EntityCollection>> RetrieveMultipleAsync(QueryBase query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => _svcClient.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult(), "RetrieveMultiple");
        }

        public virtual async Task<DataverseResponse> UpdateAsync(Entity entity, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => { _svcClient.UpdateAsync(entity, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult(); return entity; }, "Update");
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _svcClient?.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public void Refresh(string logicalTableName)
        {
        }

        public FormulaValue AddPlugIn(string @namespace, CustomApiSignature signature)
        {
            throw new NotImplementedException();
        }

        public Task<FormulaValue> ExecutePlugInAsync(RuntimeConfig config, string name, RecordValue arguments, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<DataverseResponse<OrganizationResponse>> ExecuteAsync(OrganizationRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
