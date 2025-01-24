// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

        internal static ConcurrentDictionary<string, string> _delegationIds = new ConcurrentDictionary<string, string>();

        public readonly ITestOutputHelper _output;

        public DelegationTests(ITestOutputHelper output)
        {
            _output = output;
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

                Assert.True(check.IsSuccess, string.Join(", ", check.Errors.Select(er => $"{er.Span.Min}-{er.Span.Lim}: {er.Message}")));

                DependencyInfo scan = check.ApplyDependencyInfoScan(dv.MetadataCache);

                // compare IR to verify the delegations are happening exactly where we expect
                IRResult irNode = check.ApplyIR();
                string actualIr = check.GetCompactIRString();

                if (i == 0)
                {
                    SaveExpression(id, file, expr, dv, opts, config, allSymbols);
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
                    Assert.True(check2.IsSuccess, string.Join(", ", check2.Errors.Select(er => $"{er.Span.Min}-{er.Span.Lim}: {er.Message}")));

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
            StringBuilder sb = new StringBuilder();

            IReadOnlyDictionary<string, string> ode = dp.ODataElements;

            // Check if GroupByTransformationNode is present
            if (dp.GroupBy != null)
            {
                // Generate the $apply parameter based on the GroupByTransformationNode
                sb.Append("$apply");
                AddEqual(sb);

                AppendFilterParam(dp.ODataElements, sb, true);
                AppendGroupByParam(dp.ODataElements, sb);
                AppendOrderByParam(dp.ODataElements, sb, true);
            }
            else
            {
                // Join 
                AppendJoinParam(ode, sb);
                AppendFilterParam(ode, sb, false);
                AppendOrderByParam(ode, sb, false);
                AppendSelectParam(ode, sb);
            }

            if (ode.TryGetValue(DataverseDelegationParameters.Odata_Top, out string top))
            {
                AddSeparatorIfNeeded(sb, false);
                sb.Append(DataverseDelegationParameters.Odata_Top);
                AddEqual(sb);
                sb.Append(top);
            }

            return sb.ToString();
        }

        private static void AddEqual(StringBuilder sb)
        {
            sb.Append('=');
        }

        private static void AddSeparatorIfNeeded(StringBuilder sb, bool isApplySeprator)
        {
            if (sb.Length > 0)
            {
                if (isApplySeprator)
                {
                    sb.Append('/');
                }
                else
                {
                    sb.Append('&');
                }
            }
        }

        private static void AppendFilterParam(IReadOnlyDictionary<string, string> ode, StringBuilder sb, bool isApplySeprator)
        {
            if (ode.TryGetValue(DataverseDelegationParameters.Odata_Filter, out string filter))
            {
                // in group by, filter is part of apply and is first element.
                string filterParamString = null;
                if (!isApplySeprator)
                {
                    AddSeparatorIfNeeded(sb, isApplySeprator);
                    filterParamString = DataverseDelegationParameters.Odata_Filter;
                }
                else
                {
                    filterParamString = DataverseDelegationParameters.Odata_Filter.Substring(1);
                }

                sb.Append(filterParamString);
                AddEqual(sb);
                sb.Append(filter);
            }
        }

        private static void AppendOrderByParam(IReadOnlyDictionary<string, string> ode, StringBuilder sb, bool isGroupBySeprator)
        {
            if (ode.TryGetValue(DataverseDelegationParameters.Odata_OrderBy, out string orderBy))
            {
                AddSeparatorIfNeeded(sb, isGroupBySeprator);
                sb.Append(DataverseDelegationParameters.Odata_OrderBy);
                AddEqual(sb);
                sb.Append(orderBy);
            }
        }

        private static void AppendSelectParam(IReadOnlyDictionary<string, string> ode, StringBuilder sb)
        {
            if (ode.TryGetValue(DataverseDelegationParameters.Odata_Select, out string select))
            {
                AddSeparatorIfNeeded(sb, false);
                sb.Append(DataverseDelegationParameters.Odata_Select);
                AddEqual(sb);
                sb.Append(select);
            }
        }

        private static void AppendGroupByParam(IReadOnlyDictionary<string, string> oDataElements, StringBuilder sb)
        {
            if (oDataElements.TryGetValue(DataverseDelegationParameters.Odata_Apply, out string groupBy))
            {
                if (oDataElements.ContainsKey(DataverseDelegationParameters.Odata_Filter))
                {
                    AddSeparatorIfNeeded(sb, true);
                }

                sb.Append(groupBy);
            }
        }

        private static void AppendJoinParam(IReadOnlyDictionary<string, string> oDataElements, StringBuilder sb)
        {
            if (oDataElements.TryGetValue(DataverseDelegationParameters.Odata_Apply, out string apply))
            {                
                sb.Append(DataverseDelegationParameters.Odata_Apply);
                AddEqual(sb);
                sb.Append(apply);
            }        
        }

        public class HelperClock : IClockService
        {
            public DateTime UtcNow => new DateTime(2024, 7, 29, 21, 57, 04, DateTimeKind.Utc);
        }

        private void SaveExpression(int id, string file, string expr, DataverseConnection dv, ParserOptions opts, PowerFxConfig config, ReadOnlySymbolTable allSymbols)
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
            _delegationTests.TryAdd(expr, retVal.Calls);
            _delegationIds.AddOrUpdate($"{id:0000}-{file}", (s1) => null, (s1, s2) => throw new InvalidOperationException($"Conflicting test with {id} in file {file}"));
        }

        private static void ConfigureEngine(DataverseConnection dv, RecalcEngine engine, bool enableDelegation)
        {
            if (enableDelegation)
            {                
                engine.EnableDelegation(dv.MaxRows);                
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
