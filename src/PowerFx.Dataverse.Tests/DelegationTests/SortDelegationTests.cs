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
    public class SortDelegationTests
    {
        private readonly ITestOutputHelper _output;

        public SortDelegationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData(1, "Sort(t1, Price)", 4, "0004, 0003, 0005, 0001")]
        [InlineData(2, "Sort(t1, Price, SortOrder.Ascending)", 4, "0004, 0003, 0005, 0001")]
        [InlineData(3, "Sort(t1, Price, SortOrder.Descending)", 4, "0001, 0003, 0005, 0004")]

        // Non-delegable as it's a calculated column
        [InlineData(4, "Sort(t1, Price * 2, SortOrder.Descending)", 4, "0001, 0003, 0005, 0004")]

        // Non-delegable as FirstN needs to be executed first and Sort will occur in-memory
        [InlineData(5, "Sort(FirstN(t1, 5), Price)", 4, "0004, 0003, 0005, 0001")]

        // Delegable fully, both FirstN and Sort
        [InlineData(6, "FirstN(Sort(t1, Price), 2)", 2, "0003, 0001")]

        // Non-delegable
        [InlineData(7, "Sort(FirstN(t1, 1), Price)", 1, "0001")]
        [InlineData(8, "First(Sort(t1, Price))", 1, "0001")]
        [InlineData(9, "SortByColumns(t1, Price)", 4, "0004, 0003, 0005, 0001")]
        [InlineData(10, "SortByColumns(t1, Price, SortOrder.Ascending)", 4, "0004, 0003, 0005, 0001")]
        [InlineData(11, "SortByColumns(t1, Price, SortOrder.Descending)", 4, "0001, 0003, 0005, 0004")]

        // Non-delegable
        [InlineData(12, "SortByColumns(FirstN(t1, 5), Price)", 4, "0004, 0003, 0005, 0001")]
        [InlineData(13, "FirstN(SortByColumns(t1, Price), 2)", 2, "0003, 0001")]

        // Non-delegable
        [InlineData(14, "SortByColumns(FirstN(t1, 1), Price)", 1, "0001")]
        [InlineData(15, "First(SortByColumns(t1, Price))", 1, "0001")]
        [InlineData(16, "SortByColumns(t1, Price, SortOrder.Ascending, Quantity)", 4, "0004, 0003, 0005, 0001")]
        [InlineData(17, "SortByColumns(t1, Price, SortOrder.Ascending, Quantity, SortOrder.Ascending)", 4, "0004, 0003, 0005, 0001")]
        [InlineData(18, "SortByColumns(t1, Price, SortOrder.Descending, Quantity, SortOrder.Ascending)", 4, "0001, 0003, 0005, 0004")]
        [InlineData(19, "SortByColumns(t1, Price, SortOrder.Descending, Quantity, SortOrder.Descending)", 4, "0001, 0005, 0003, 0004")]

        // Sort non-delegable
        [InlineData(20, "Sort(FirstN(Filter(t1, Price <= 100), 2), Quantity)", 2, "0003, 0001")]

        // Delegable fully
        [InlineData(21, "FirstN(Sort(Filter(t1, Price <= 100), Quantity), 2)", 2, "0003, 0001")]
        [InlineData(22, "FirstN(Filter(Sort(t1, Quantity), Price <= 100), 2)", 2, "0003, 0001")]

        // Non-delegable
        [InlineData(23, @"Sort(t1, ""Price"")", 4, "0001, 0003, 0004, 0005")]
        [InlineData(24, @"Sort(t1, ""new_price"")", 4, "0001, 0003, 0004, 0005")]        
        [InlineData(25, @"Sort(t1, ""XXXXX"")", 4, "0001, 0003, 0004, 0005")]

        // Delegable
        // Excluding this test for now due to DV issue 515
        // [InlineData(26, @"SortByColumns(t1, ""Price"")", 4, "0004, 0003, 0005, 0001")]
        [InlineData(27, @"SortByColumns(t1, ""new_price"")", 4, "0004, 0003, 0005, 0001")]

        // Can't delegate two SortByColumns
        [InlineData(28, "SortByColumns(SortByColumns(t1, Price, SortOrder.Descending), Quantity, SortOrder.Descending)", 4, "0001, 0005, 0003, 0004")]

        // Not a delegable table
        [InlineData(29, @"Sort([30, 10, 20], Value)", 3, "10, 20, 30", true)]

        [InlineData(30, @"Distinct(Sort(t1, Price), Price)", 3, "-10, 10, 100", true)]        
        [InlineData(31, "LookUp(Sort(t1, Quantity), Price <= 100)", 1, "0001")]
        public async Task SortDelegationAsync(int id, string expr, int expectedRows, string expectedIds, bool useValue = false)
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
                await DelegationTestUtility.CompareSnapShotAsync("SortDelegation.txt", actualIr, id, i == 1);

                _output.WriteLine(actualIr);
                _output.WriteLine(string.Empty);

                IEnumerable<ExpressionError> errors = check.ApplyErrors();
                Assert.Empty(errors);

                IExpressionEvaluator run = check.GetEvaluator();
                FormulaValue result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

                Assert.IsNotType<ErrorValue>(result);

                string ids = null;
                if (result is TableValue tv)
                {
                    Assert.Equal(expectedRows, tv.Rows.Count());
                    ids = useValue
                        ? string.Join(", ", tv.Rows.Select(drv => (drv.Value.Fields.First(nv => nv.Name == "Value").Value as NumberValue).Value.ToString()))
                        : string.Join(", ", tv.Rows.Select(drv => (drv.Value.Fields.First(nv => nv.Name == "localid").Value as GuidValue).Value.ToString()[^4..]));
                }
                else if (result is RecordValue rv)
                {
                    Assert.Equal(1, expectedRows);
                    ids = (rv.Fields.First(nv => nv.Name == "localid").Value as GuidValue).Value.ToString()[^4..];
                }
                else
                {
                    Assert.Fail("result is neither a TableValue nor a RecordValue");
                }

                Assert.Equal<object>(expectedIds, ids);
            }
        }
    }
}
