using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class ForAllDelegationTests
    {
        private readonly ITestOutputHelper _output;

        public ForAllDelegationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData(1, "ForAll([10,20,30], Value)", 3, "Value", "10, 20, 30")]
        [InlineData(2, "ForAll(t1, Price)", 4, "Value", "100, 10, -10, 10")]
        [InlineData(3, "ForAll(t1, { Price: Price })", 4, "Price", "100, 10, -10, 10")]
        [InlineData(4, "ForAll(t1, { Xyz: Price })", 4, "Xyz", "100, 10, -10, 10")]
        [InlineData(5, "ForAll(t1, { Price: Price, Price2: Price })", 4, "Price2", "100, 10, -10, 10")]
        [InlineData(6, "ForAll(t1, { Price: Price * 2 })", 4, "Price", "200, 20, -20, 20", true)]
        [InlineData(7, "First(ForAll(t1, Price))", 1, "Value", "100")]
        [InlineData(8, "First(ForAll(t1, { Price: Price }))", 1, "Price", "100")]
        [InlineData(9, "First(ForAll(t1, { Xyz: Price }))", 1, "Xyz", "100")]
        [InlineData(10, "First(ForAll(t1, { Price: Price, Price2: Price }))", 1, "Price2", "100")]
        [InlineData(11, "First(ForAll(t1, { Price: Price * 2 }))", 1, "Price", "200", true)]
        [InlineData(12, "FirstN(ForAll(t1, Price), 2)", 2, "Value", "100, 10")]
        [InlineData(13, "FirstN(ForAll(t1, { Price: Price }), 2)", 2, "Price", "100, 10")]
        [InlineData(14, "FirstN(ForAll(t1, { Xyz: Price }), 2)", 2, "Xyz", "100, 10")]
        [InlineData(15, "FirstN(ForAll(t1, { Price: Price, Price2: Price }), 2)", 2, "Price2", "100, 10")]
        [InlineData(16, "FirstN(ForAll(t1, { Price: Price * 2 }), 2)", 2, "Price", "200, 20", true)]
        [InlineData(17, "ForAll(Filter(t1, Price < 0 Or Price > 90), Price)", 2, "Value", "100, -10")]
        [InlineData(18, "ForAll(Sort(Filter(t1, Price < 0 Or Price > 90), Price), Price)", 2, "Value", "-10, 100")]
        [InlineData(19, "ForAll(Filter(Sort(t1, Price), Price < 0 Or Price > 90), Price)", 2, "Value", "-10, 100")]
        [InlineData(20, "ForAll(FirstN(t1, 3), { Price: Price, Price2: Price })", 3, "Price", "100, 10, -10")]
        [InlineData(21, "FirstN(ForAll(t1, { Price: Price, Price2: Price }), 3)", 3, "Price", "100, 10, -10")]
        [InlineData(22, "ForAll(Distinct(Filter(t1, Price > 0), Price), Value)", 2, "Value", "100, 10")]
        [InlineData(23, "Distinct(ForAll(Filter(t1, Price > 0), Price), Value)", 2, "Value", "100, 10")]         
        public async Task ForAllDelegationAsync(int id, string expr, int expectedRows, string column, string expectedIds, bool expectedWarning = false)
        {
            AllTablesDisplayNameProvider map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");

            SingleOrgPolicy policy = new SingleOrgPolicy(map);
            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: true, policy: policy, withExtraEntity: true);
            ParserOptions opts = PluginExecutionTests._parserAllowSideEffects_NumberIsFloat;
            PowerFxConfig config = new PowerFxConfig();
            config.SymbolTable.EnableMutationFunctions();

            RecalcEngine engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);

            IList<string> inputs = DelegationTestUtility.TransformForWithFunction(expr, 0);

            for (int i = 0; i < inputs.Count; i++)
            {
                string input = inputs[i];
                CheckResult check = engine1.Check(input, options: opts, symbolTable: dv.Symbols);
                Assert.True(check.IsSuccess, string.Join(", ", check.Errors.Select(er => er.Message)));

                DependencyInfo scam = check.ScanDependencies(dv.MetadataCache);
                Core.IR.IRResult irNode = check.ApplyIR();
                string actualIr = check.GetCompactIRString();

                _output.WriteLine(input);
                await DelegationTestUtility.CompareSnapShotAsync("ForAllDelegation.txt", actualIr, id, i == 1);

                _output.WriteLine(actualIr);
                _output.WriteLine(string.Empty);

                IEnumerable<ExpressionError> errors = check.ApplyErrors();
                Assert.True(expectedWarning ? errors.Count() == 1 && errors.First().Message.Contains("may not work if it has more than") : !errors.Any());

                IExpressionEvaluator run = check.GetEvaluator();
                FormulaValue result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

                Assert.IsNotType<ErrorValue>(result);

                string ids = null;
                if (result is TableValue tv)
                {
                    Assert.Equal(expectedRows, tv.Rows.Count());
                    ids = string.Join(", ", tv.Rows.Select(drv => GetString(drv.Value.Fields.First(nv => nv.Name == column).Value)));
                }
                else if (result is RecordValue rv)
                {
                    Assert.Equal(1, expectedRows);
                    //ids = (rv.Fields.First(nv => nv.Name == "localid").Value as GuidValue).Value.ToString()[^4..];
                    ids = GetString(rv.Fields.First(nv => nv.Name == column).Value);
                }
                else
                {
                    Assert.Fail("result is neither a TableValue nor a RecordValue");
                }

                Assert.Equal<object>(expectedIds, ids);
            }
        }

        private static string GetString(FormulaValue fv) => fv?.ToObject()?.ToString() ?? "<Blank>";
    }
}
