// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Connectors;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Functions;
using Microsoft.PowerFx.Types;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    // This class is sealed to make sure all tests should be calling DelegationTestAsync with [TestPriority(1)] so that they all
    // execute before CheckDelegationExpressions test which uses [TestPriority(2)]
    // In DelegationTestAsync, we accumulate delegation tests in _delegationTests dictionary below
    // and then validate with have the expected combinations in CheckDelegationExpressions
    // For determining the sequence of function calls, we use CallVisitor below and only look at 1st call arguments as
    // all delegated calls have the table/delegation managed as first parameter.
    // We ignore/skip 'With' call as it's just modifying the context.
    [TestCaseOrderer("Microsoft.PowerFx.Dataverse.Tests.PriorityOrderer", "PowerFx.Dataverse.Tests")]
    public sealed partial class DelegationTests
    {
        internal static ConcurrentDictionary<string, List<string>> _delegationTests = new ConcurrentDictionary<string, List<string>>();
        internal static ConcurrentDictionary<string, string> _delegationDelegationIRs = new ConcurrentDictionary<string, string>();

        public readonly ITestOutputHelper _output;

        public DelegationTests(ITestOutputHelper output)
        {
            _output = output;
            DetectDuplicateInlineData(typeof(DelegationTests));
        }

        [Fact]
        public async Task LivePowerAppsConnectorTest()
        {
#if false
            var endpoint = "https://44f782dc-c6fb-eafc-907b-dc95ca486d9c.15.common.tip1002.azure-apihub.net/";
            var connectionId = "5772e1af38d64721bc9b96307fae662e";
            var envId = "44f782dc-c6fb-eafc-907b-dc95ca486d9c";
            var sessionId = "4eac2adc-8cd1-441d-b0e9-608d3f360f8d";
            var dataset = "testconnector.database.windows.net,testconnector";
            var tableToUseInExpression = "Employees";
            var expr = @"CountRows(Employees)";
            var jwt = " ";

            var dataSourceInfo = ads.First();
            Assert.NotNull(dataSourceInfo);

            Assert.True(dataSourceInfo.IsDelegatable);
            Assert.True(dataSourceInfo.IsPageable);
            Assert.True(dataSourceInfo.IsRefreshable);
            Assert.True(dataSourceInfo.IsSelectable);
            Assert.True(dataSourceInfo.IsWritable);
            Assert.True(dataSourceInfo.RequiresAsync);

            SymbolValues symbolValues = new SymbolValues().Add(tableToUseInExpression, sqlTable);
            RuntimeConfig rc = new RuntimeConfig(symbolValues);

            var config = new PowerFxConfig(Features.PowerFxV1);
            var engine = new RecalcEngine(config);
            engine.EnableDelegation(2);
            CheckResult check = engine.Check(expr, options: new ParserOptions() { AllowsSideEffects = true }, symbolTable: symbolValues.SymbolTable);
            var ir = check.GetCompactIRString();
            Assert.True(check.IsSuccess);
            FormulaValue result = await check.GetEvaluator().EvalAsync(CancellationToken.None, rc);
#endif
        }

        [Fact]
        public async Task LiveSampleConnectorTest()
        {
#if false
            var endpoint = "https://localhost:7157";
            var dataset = "default";
            var tableToUseInExpression = "MyTable";
            var expr = @"MyTable";
            var uriPrefix = string.Empty;

            using var client = new System.Net.Http.HttpClient() { BaseAddress = new Uri(endpoint) };

            CdpDataSource cds = new CdpDataSource(dataset, ConnectorSettings.NewCDPConnectorSettings(extractSensitivityLabel: true));

            IEnumerable<CdpTable> tables = await cds.GetTablesAsync(client, uriPrefix, CancellationToken.None);
            CdpTable connectorTable = tables.First(t => t.DisplayName == tableToUseInExpression);

            Assert.False(connectorTable.IsInitialized);
            Assert.Equal(tableToUseInExpression, connectorTable.DisplayName);

            await connectorTable.InitAsync(client, uriPrefix, CancellationToken.None);

            CdpTableValue sqlTable = connectorTable.GetTableValue();

            var ads = sqlTable.Type._type.AssociatedDataSources;
            Assert.NotNull(ads);
            Assert.Single(ads);

            var dataSourceInfo = ads.First();
            Assert.NotNull(dataSourceInfo);

            Assert.True(dataSourceInfo.IsDelegatable);
            Assert.True(dataSourceInfo.IsPageable);
            Assert.True(dataSourceInfo.IsRefreshable);
            Assert.True(dataSourceInfo.IsSelectable);
            Assert.True(dataSourceInfo.IsWritable);
            Assert.True(dataSourceInfo.RequiresAsync);

            SymbolValues symbolValues = new SymbolValues().Add(tableToUseInExpression, sqlTable);
            RuntimeConfig rc = new RuntimeConfig(symbolValues);

            var config = new PowerFxConfig(Features.PowerFxV1);
            var engine = new RecalcEngine(config);
            engine.EnableDelegation(2);
            CheckResult check = engine.Check(expr, options: new ParserOptions() { AllowsSideEffects = true }, symbolTable: symbolValues.SymbolTable);
            var ir = check.GetCompactIRString();
            Assert.True(check.IsSuccess);
            FormulaValue result = await check.GetEvaluator().EvalAsync(CancellationToken.None, rc);
            Assert.IsNotAssignableFrom<ErrorValue>(result);
            var rType = ((TableValue)result).Type.ToRecord();

            var b1 = ((ICDPAggregateMetadata)rType).TryGetSensitivityLabelInfo(out var cdpSensitivityLabelInfo);
            var b2 = ((ICDPAggregateMetadata)rType).TryGetMetadataItems(out var cdpMD);
#endif
        }

        private static void DetectDuplicateInlineData(Type testClass)
        {
            var theoryMethods = testClass.GetMethods()
                .Where(m => m.GetCustomAttributes(typeof(TheoryAttribute), true).Any());

            foreach (var method in theoryMethods)
            {
                var seenIds = new HashSet<int>();
                var paramNames = method.GetParameters().Select(p => p.Name).ToArray();
                int index = Array.IndexOf(paramNames, "id");

                if (index < 0)
                {
                    continue; // Skip if no 'id' param
                }

                var inlineDatas = method.GetCustomAttributes(typeof(InlineDataAttribute), true)
                                    .Cast<InlineDataAttribute>();

                foreach (var attr in inlineDatas)
                {
                    var data = attr.GetData(null!).FirstOrDefault();
                    if (data == null || index >= data.Length)
                    {
                        continue;
                    }

                    var id = Convert.ToInt32(data[index]);

                    if (seenIds.Contains(id, default))
                    {
                        throw new Exception($"Duplicate ID '{id}' found in method '{method.Name}'");
                    }
                    else 
                    { 
                        seenIds.Add(id);
                    }
                }
            }
        }

        internal async Task DelegationTestAsync(int id, string file, string expr, int expectedRows, object expectedResult, Func<FormulaValue, object> resultGetter, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, Action<PowerFxConfig> extraConfig, bool withExtraEntity, bool isCheckSuccess, bool withTransformed, params string[] expectedWarnings)
        {
            _output.WriteLine($"{id}");

            AllTablesDisplayNameProvider map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "t2");
            map.Add("virtualremote", "t3");
            map.Add("elastictable", "et");

            TestSingleOrgPolicy singleOrgPolicy = new TestSingleOrgPolicy(map);
            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: cdsNumberIsFloat, policy: singleOrgPolicy, withExtraEntity: withExtraEntity);
            ParserOptions opts = parserNumberIsFloatOption ? PluginExecutionTests._parserAllowSideEffects_NumberIsFloat : PluginExecutionTests._parserAllowSideEffects;

            PowerFxConfig config;
            RecalcEngine engine;
            SymbolTable fakeSymbolTable;
            ISymbolSlot fakeSlot;
            TestDataverseTableValue fakeTableValue;
            ReadOnlySymbolTable allSymbols;
            
            Configure(true, extraConfig, dv, out config, out engine, out fakeSymbolTable, out fakeSlot, out fakeTableValue, out allSymbols);

            IList<string> inputs = DelegationTestUtility.TransformForWithFunction(expr, expectedWarnings?.Count() ?? 0);

            for (int i = 0; i < inputs.Count; i++)
            {
                if (i == 1 && !withTransformed)
                {
                    break;
                }

                string input = inputs[i];
                CheckResult check = engine.Check(input, options: opts, symbolTable: allSymbols);

                if (!isCheckSuccess)
                {
                    Assert.False(check.IsSuccess);
                    _output.WriteLine(string.Join(", ", check.Errors.Select(er => er.Message)));
                    return;
                }

                Assert.True(check.IsSuccess, string.Join(", ", check.Errors.Select(er => er.ToString())));

                DependencyInfo scan = check.ApplyDependencyInfoScan();

                // compare IR to verify the delegations are happening exactly where we expect
                IRResult irNode = check.ApplyIR();
                string actualIr = check.GetCompactIRString();

                if (i == 0)
                {
                    SaveExpression(id, file, expr, actualIr, dv, opts, config, allSymbols);
                }

                _output.WriteLine("IR with delegation");
                _output.WriteLine(actualIr);
                _output.WriteLine(check.PrintIR());

                // Validate delegation warnings.
                // error.ToString() will capture warning status, message, and source span.
                IEnumerable<ExpressionError> errors = check.ApplyErrors();

                string[] errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();
                Assert.True(expectedWarnings.Length == errorList.Length, string.Join("\r\n", errorList));

                for (int j = 0; j < errorList.Length; j++)
                {
                    Assert.Equal<object>(expectedWarnings[j], errorList[j]);
                }

                IExpressionEvaluator run = check.GetEvaluator();

                // Place a reference to tableT1 in the fakeT1 symbol values and compose in
                var fakeSymbolValues = new SymbolValues(fakeSymbolTable);
                fakeSymbolValues.Set(fakeSlot, fakeTableValue);
                var allValues = ReadOnlySymbolValues.Compose(fakeSymbolValues, dv.SymbolValues);
                RuntimeConfig rc = new RuntimeConfig(allValues);

                rc.SetClock(new HelperClock());
                rc.SetTimeZone(TimeZoneInfo.Utc);

                FormulaValue result = await run.EvalAsync(CancellationToken.None, rc);

                IEnumerable<DataverseDelegationParameters> ddpl = singleOrgPolicy.GetDelegationParameters();
                string oDataStrings = string.Join(" | ", ddpl.Select(dp => GetODataString(dp)));

                await DelegationTestUtility.CompareSnapShotAsync(id, file, string.IsNullOrEmpty(oDataStrings) ? actualIr : $"{actualIr} | {oDataStrings}", id, i == 1);

                FormulaValue nonDelegatingResult = null;
                if (resultGetter != null)
                {
                    // Get non-delegating version of the result to ensure consistency
                    Configure(false, extraConfig, dv, out PowerFxConfig config2, out RecalcEngine engine2, out SymbolTable fakeSymbolTable2, out ISymbolSlot fakeSlot2, out TestDataverseTableValue fakeTableValue2, out ReadOnlySymbolTable allSymbols2);
                    
                    CheckResult check2 = engine2.Check(input, options: opts, symbolTable: allSymbols2);
                    Assert.True(check2.IsSuccess, string.Join(", ", check2.Errors.Select(er => er.ToString())));

                    IExpressionEvaluator run2 = check2.GetEvaluator();
                    var fakeSymbolValues2 = new SymbolValues(fakeSymbolTable2);
                    fakeSymbolValues2.Set(fakeSlot2, fakeTableValue2);
                    var allValues2 = ReadOnlySymbolValues.Compose(fakeSymbolValues2, dv.SymbolValues);
                    RuntimeConfig rc2 = new RuntimeConfig(allValues2);
                    rc2.SetClock(new HelperClock());
                    rc2.SetTimeZone(TimeZoneInfo.Utc);
                    
                    nonDelegatingResult = await run2.EvalAsync(CancellationToken.None, rc2);
                }

                _output.WriteLine(string.Empty);
                _output.WriteLine($"OData strings: {oDataStrings}");

                if (expectedRows < 0)
                {
                    if (expectedRows != -2)
                    {
                        Assert.IsType<ErrorValue>(result);
                    }
                }
                else if (result is ErrorValue ev)
                {
                    Assert.Fail($"Unexpected error: {string.Join("\r\n", ev.Errors.Select(er => er.Message))}");
                }
                else if (result is RecordValue rv)
                {
                    Assert.Equal(1, expectedRows);
                }
                else if (result is TableValue tv)
                {
                    Assert.Equal(expectedRows, tv.Rows.Count());
                }
                else if (result is BlankValue)
                {
                    Assert.Equal(0, expectedRows);
                }
                else
                {
                    Assert.Fail($"Unexpected result type {result.GetType().Name}");
                }

                if (expectedResult != null)
                {
                    if (expectedResult is Type expectedType)
                    {
                        Assert.IsType(expectedType, result);
                    }
                    else if (resultGetter != null)
                    {
                        // Verify non-delegating first
                        Assert.Equal(expectedResult, resultGetter(nonDelegatingResult));

                        // Confirm that with delegation we have same results
                        Assert.Equal(expectedResult, resultGetter(result));                        
                    }
                    else if (cdsNumberIsFloat && (parserNumberIsFloatOption || (cdsNumberIsFloat && !parserNumberIsFloatOption)))
                    {
                        Assert.Equal(expectedResult, result.ToObject());
                    }
                    else
                    {
                        Assert.Equal(new decimal((double)expectedResult), result.ToObject());
                    }
                }
            }
        }

        private static void Configure(bool enableDelegation, Action<PowerFxConfig> extraConfig, DataverseConnection dv, out PowerFxConfig config, out RecalcEngine engine, out SymbolTable fakeSymbolTable, out ISymbolSlot fakeSlot, out TestDataverseTableValue fakeTableValue, out ReadOnlySymbolTable allSymbols)
        {
            config = new PowerFxConfig();
            config.EnableJoinFunction();

            Assert.True(config.Features.SupportColumnNamesAsIdentifiers, "config broken");

            config.SymbolTable.EnableMutationFunctions();
            extraConfig?.Invoke(config);

            engine = new RecalcEngine(config);
            ConfigureEngine(dv, engine, enableDelegation);

            // Make fakeT1 non delegable
            var tableT1Type = dv.GetRecordType("local").SetNonDelegable();
            fakeSymbolTable = new SymbolTable();
            fakeSlot = fakeSymbolTable.AddVariable("fakeT1", (TableType)tableT1Type.ToTable());
            fakeTableValue = new TestDataverseTableValue(tableT1Type, dv, dv.GetMetadataOrThrow("local"));
            allSymbols = ReadOnlySymbolTable.Compose(fakeSymbolTable, dv.Symbols);
        }

        internal static string GetODataString(DataverseDelegationParameters dp)
        {
            return dp.GetODataQueryString();
        }

        public class HelperClock : IClockService
        {
            public DateTime UtcNow => new DateTime(2024, 7, 29, 21, 57, 04, DateTimeKind.Utc);
        }

        private void SaveExpression(int id, string file, string expr, string expectedDelegationIR, DataverseConnection dv, ParserOptions opts, PowerFxConfig config, ReadOnlySymbolTable allSymbols)
        {
            RecalcEngine engine2 = new RecalcEngine(config);
            ConfigureEngine(dv, engine2, false);
            CheckResult check2 = engine2.Check(expr, options: opts, symbolTable: allSymbols);
            Assert.True(check2.IsSuccess, string.Join(", ", check2.Errors.Select(er => er.Message)));
            IRResult irNode2 = check2.ApplyIR();
            string actualIr2 = check2.PrintIR();

            _output.WriteLine("IR without delegation:");
            _output.WriteLine(actualIr2);

            CallVisitor visitor = new CallVisitor();
            CallVisitor.RetVal retVal = visitor.StartVisit(irNode2.TopNode, null);

            var key = $"{id:0000}-{file}";

            _delegationTests.TryAdd(expr, retVal.Calls);
            _delegationDelegationIRs.AddOrUpdate(key, (s1) => expectedDelegationIR, (s1, s2) => s2 == expectedDelegationIR ? null : throw new InvalidOperationException($"Conflicting test with {id} in file {file}"));
        }

        private static void ConfigureEngine(DataverseConnection dv, RecalcEngine engine, bool enableDelegation)
        {
            if (enableDelegation)
            {                
                engine.EnableTestDelegation(dv.MaxRows);                
            }

            engine.UpdateVariable("_count", FormulaValue.New(100m));
            engine.UpdateVariable("_g1", FormulaValue.New(PluginExecutionTests._g1)); // matches entity
            engine.UpdateVariable("_gMissing", FormulaValue.New(Guid.Parse("00000000-0000-0000-9999-000000000001"))); // no match
        }

        [Fact]
        [TestPriority(2)]               
        public void CheckDelegationExpressions()
        {
#if false

            // For debugging only
            string file = @"c:\temp\delegation.txt";

            File.WriteAllText(file, JsonConvert.SerializeObject(_delegationTests.Select(kvp => new KeyValuePair<string, string>(kvp.Key, string.Join("|", kvp.Value))).OrderBy(kvp => kvp.Key)));
            IEnumerable<KeyValuePair<string, List<string>>> data = JsonConvert.DeserializeObject<List<KeyValuePair<string, string>>>(File.ReadAllText(file)).Select(kvp => new KeyValuePair<string, List<string>>(kvp.Key, kvp.Value.Split('|').ToList()));
            _delegationTests = new ConcurrentDictionary<string, List<string>>(data);
#endif

            _output.WriteLine($"Number of expressions {_delegationTests.Count}");

            ConcurrentDictionary<string, List<string>> d2 = new ConcurrentDictionary<string, List<string>>();
            foreach (KeyValuePair<string, List<string>> kvp in _delegationTests.OrderBy(kvp => kvp.Key))
            {
                string expr = kvp.Key;
                string functions = string.Join(", ", kvp.Value);

                d2.AddOrUpdate(functions, new List<string>() { expr }, (e, lst) =>
                {
                    lst.Add(expr);
                    return lst;
                });
            }

            foreach (KeyValuePair<string, List<string>> kvp in d2.OrderBy(kvp => kvp.Key))
            {
                string functions = kvp.Key;

                _output.WriteLine($"[{functions}]");

                foreach (string expr in kvp.Value)
                {
                    _output.WriteLine(expr);
                }

                _output.WriteLine(string.Empty);
            }

            _output.WriteLine("----");

            int missing = 0;

            string[] functionsReturningTable = new[] { "Distinct", "Filter", "FirstN", "Sort", "SortByColumns", "ShowColumns", "ForAll" };
            string[] functionsReturningRecord = new[] { "First", "LookUp" };

            foreach (string f1 in functionsReturningTable.Union(functionsReturningRecord))
            {
                foreach (string f2 in functionsReturningTable)
                {
                    string f = $"{f1}, {f2}";

                    if (d2.ContainsKey(f))
                    {
                        continue;
                    }

                    missing++;
                    _output.WriteLine($"Missing {f}");
                }
            }

            Assert.True(missing == 0, $"Missing {missing} tests");
        }
    }

    public static class AdsExtensions
    {   
        public static RecordType SetNonDelegable(this RecordType recordType)            
        {            
            DataverseDataSourceInfo previous = (DataverseDataSourceInfo)recordType._type.AssociatedDataSources.First();

            // Make a copy to not alter the original data source
            DataverseDataSourceInfo newDS = new DataverseDataSourceInfo(
                (Microsoft.AppMagic.Authoring.Importers.DataDescription.CdsTableDefinition)previous.TableDefinition, 
                (Microsoft.PowerFx.Dataverse.CdsEntityMetadataProvider)previous.DataEntityMetadataProvider,
                previous.EntityName);

            newDS._isDelegable = false;

            return (RecordType)FormulaType.Build(newDS.Schema.ToRecord());
        }
    }

    internal class CallVisitor : SearchIRVisitor<CallVisitor.RetVal, CallVisitor.Context>
    {
        public class RetVal
        {
            public RetVal()
            {
                Calls = new List<string>();
            }

            public List<string> Calls { get; private set; }
        }

        public class Context
        {
        }

        public override RetVal Visit(Core.IR.Nodes.CallNode node, Context context)
        {
            string funcName = node.Function.Name;
            int i = funcName == "With" ? 1 : 0;

            RetVal ret = node.Args[i].Accept(this, context);

            if (ret != null)
            {
                if (i == 0)
                {
                    ret.Calls.Insert(0, funcName);
                }

                return ret;
            }

            RetVal retVal = new RetVal();

            if (i == 0)
            {
                retVal.Calls.Insert(0, funcName);
            }

            return retVal;
        }

        public RetVal StartVisit(IntermediateNode node, Context ctx)
        {
            return node switch
            {
                Core.IR.Nodes.CallNode callNode => this.Visit(callNode, ctx),
                Core.IR.Nodes.RecordFieldAccessNode recordFieldAccessNode => base.Visit(recordFieldAccessNode, ctx),
                _ => throw new Exception($"Unknown {node.GetType().Name} type")
            };
        }
    }
}
