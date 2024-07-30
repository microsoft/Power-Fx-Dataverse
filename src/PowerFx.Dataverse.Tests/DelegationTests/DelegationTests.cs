using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class DelegationTests
    {
        protected readonly ITestOutputHelper _output;

        public DelegationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        protected async Task DelegationTestAsync(int id, string file, string expr, int expectedRows, object expectedResult, Func<FormulaValue, object> resultGetter, bool cdsNumberIsFloat,
            bool parserNumberIsFloatOption, Action<PowerFxConfig> extraConfig, bool withExtraEntity, bool isCheckSuccess, bool withTransformed, params string[] expectedWarnings)
        {
            AllTablesDisplayNameProvider map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "t2");
            map.Add("virtualremote", "t3");
            map.Add("elastictable", "et");

            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: cdsNumberIsFloat, policy: new SingleOrgPolicy(map), withExtraEntity: withExtraEntity);
            ParserOptions opts = parserNumberIsFloatOption ? PluginExecutionTests._parserAllowSideEffects_NumberIsFloat : PluginExecutionTests._parserAllowSideEffects;

            PowerFxConfig config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            extraConfig?.Invoke(config);

            RecalcEngine engine = new RecalcEngine(config);
            engine.EnableDelegation(dv.MaxRows);
            engine.UpdateVariable("_count", FormulaValue.New(100m));
            engine.UpdateVariable("_g1", FormulaValue.New(PluginExecutionTests._g1)); // matches entity
            engine.UpdateVariable("_gMissing", FormulaValue.New(Guid.Parse("00000000-0000-0000-9999-000000000001"))); // no match

            // Add a variable with same table type.
            // But it's not in the same symbol table, so we can't delegate this.
            // Previously this was UpdateVariable, but UpdateVariable no longer supports dataverse tables (by design).
            var tableT1Type = dv.GetRecordType("local");
            var fakeSymbolTable = new SymbolTable();
            var fakeSlot = fakeSymbolTable.AddVariable("fakeT1", tableT1Type.ToTable());
            var fakeTableValue = new DataverseTableValue(tableT1Type, dv, dv.GetMetadataOrThrow("local"));
            var allSymbols = ReadOnlySymbolTable.Compose(fakeSymbolTable, dv.Symbols);

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

                Assert.True(check.IsSuccess, string.Join(", ", check.Errors.Select(er => er.Message)));

                DependencyInfo scam = check.ScanDependencies(dv.MetadataCache);

                // compare IR to verify the delegations are happening exactly where we expect
                IRResult irNode = check.ApplyIR();
                string actualIr = check.GetCompactIRString();

                await DelegationTestUtility.CompareSnapShotAsync(file, actualIr, id, i == 1);

                // Validate delegation warnings.
                // error.ToString() will capture warning status, message, and source span.
                IEnumerable<ExpressionError> errors = check.ApplyErrors();

                string[] errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();
                Assert.True(expectedWarnings.Length == errorList.Length, string.Join("\r\n", errorList));

                for (int j = 0; j < errorList.Length; j++)
                {
                    Assert.Equal(expectedWarnings[j], errorList[j]);
                }

                IExpressionEvaluator run = check.GetEvaluator();

                // Place a reference to tableT1 in the fakeT1 symbol values and compose in
                var fakeSymbolValues = new SymbolValues(fakeSymbolTable);
                fakeSymbolValues.Set(fakeSlot, fakeTableValue);
                var allValues = ReadOnlySymbolValues.Compose(fakeSymbolValues, dv.SymbolValues);

                FormulaValue result = await run.EvalAsync(CancellationToken.None, allValues);

                if (expectedRows < 0)
                {
                    if (expectedRows != -2)
                    {
                        Assert.IsType<ErrorValue>(result);
                    }
                }
                else if (result is RecordValue rv)
                {
                    Assert.Equal(1, expectedRows);
                }
                else if (result is TableValue tv)
                {
                    Assert.Equal(expectedRows, tv.Rows.Count());
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
                        Assert.Equal(expectedResult, resultGetter(result));
                    }
                    else if (cdsNumberIsFloat && parserNumberIsFloatOption || cdsNumberIsFloat && !parserNumberIsFloatOption)
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
    }
}
