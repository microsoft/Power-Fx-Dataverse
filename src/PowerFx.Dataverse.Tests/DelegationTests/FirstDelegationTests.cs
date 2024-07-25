using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class FirstDelegationTests
    {
        // Table 't1' has
        // 1st item with
        // Price = 100, Old_Price = 200,  Date = Date(2023, 6, 1), DateTime = DateTime(2023, 6, 1, 12, 0, 0)
        // 2nd item with
        // Price = 10
        // 3rd item with
        // Price = -10

        [Theory]

        //Basic case 
        [InlineData(1, "First(t1).Price", 100.0, false, false)]
        [InlineData(2, "First(t1).Price", 100.0, true, true)]
        [InlineData(3, "First(t1).Price", 100.0, true, false)]
        [InlineData(4, "First(t1).Price", 100.0, false, true)]

        // Filter inside FirstN, both can be combined *(vice versa isn't true)*
        [InlineData(5, "First(Filter(t1, Price < 100)).Price", 10.0, false, false)]
        [InlineData(6, "First(Filter(t1, Price < 100)).Price", 10.0, true, true)]
        [InlineData(7, "First(Filter(t1, Price < 100)).Price", 10.0, true, false)]
        [InlineData(8, "First(Filter(t1, Price < 100)).Price", 10.0, false, true)]

        [InlineData(9, "First(FirstN(t1, 2)).Price", 100.0, false, false)]
        [InlineData(10, "First(FirstN(t1, 2)).Price", 100.0, true, true)]
        [InlineData(11, "First(FirstN(t1, 2)).Price", 100.0, true, false)]
        [InlineData(12, "First(FirstN(t1, 2)).Price", 100.0, false, true)]

        [InlineData(13, "First(Distinct(t1, Quantity)).Value", 20.0, false, false)]
        [InlineData(14, "First(Distinct(t1, Quantity)).Value", 20.0, true, true)]
        [InlineData(15, "First(Distinct(t1, Quantity)).Value", 20.0, true, false)]
        [InlineData(16, "First(Distinct(t1, Quantity)).Value", 20.0, false, true)]

        [InlineData(17, "First(et).Field1", 200.0, false, false)]
        [InlineData(18, "First(et).Field1", 200.0, true, true)]
        [InlineData(19, "First(et).Field1", 200.0, true, false)]
        [InlineData(20, "First(et).Field1", 200.0, false, true)]
        public async Task FirstDelegationAsync(int id, string expr, object expected, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
        {
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "t2");
            map.Add("virtualremote", "t3");
            map.Add("elastictable", "et");
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: cdsNumberIsFloat, policy: policy);

            var opts = parserNumberIsFloatOption ?
                PluginExecutionTests._parserAllowSideEffects_NumberIsFloat :
                PluginExecutionTests._parserAllowSideEffects;

            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_count", FormulaValue.New(100));

            var inputs = DelegationTestUtility.TransformForWithFunction(expr, expectedWarnings?.Count() ?? 0);

            for (var i = 0; i < inputs.Count(); i++)
            {
                expr = inputs[i];

                var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
                Assert.True(check.IsSuccess);

                var scam = check.ScanDependencies(dv.MetadataCache);

                // compare IR to verify the delegations are happening exactly where we expect 
                var irNode = check.ApplyIR();
                var actualIr = check.GetCompactIRString();

                await DelegationTestUtility.CompareSnapShotAsync("FirstDelegation.txt", actualIr, id, i == 1);

                // Validate delegation warnings.
                // error.ToString() will capture warning status, message, and source span. 
                var errors = check.ApplyErrors();

                var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

                Assert.Equal(expectedWarnings.Length, errorList.Length);
                for (var j = 0; j < errorList.Length; j++)
                {
                    Assert.Equal(expectedWarnings[j], errorList[j]);
                }

                var run = check.GetEvaluator();

                var result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

                if (cdsNumberIsFloat && parserNumberIsFloatOption ||
                    cdsNumberIsFloat && !parserNumberIsFloatOption)
                {
                    Assert.Equal(expected, result.ToObject());
                }
                else
                {
                    Assert.Equal(new decimal((double)expected), result.ToObject());
                }
            }
        }
    }
}
