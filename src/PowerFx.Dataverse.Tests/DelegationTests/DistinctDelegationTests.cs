using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class DistinctDelegationTests
    {
        [Theory]
        [InlineData(1, "Distinct(t1, Price)", 3)]
        [InlineData(2, "Distinct(t1, Quantity)", 2)]
        [InlineData(3, "Distinct(FirstN(t1, 2), Quantity)", 2)]
        [InlineData(4, "FirstN(Distinct(t1, Quantity), 2)", 2)]
        [InlineData(5, "Distinct(Filter(t1, Quantity < 30 And Price < 120), Quantity)", 2)]
        [InlineData(6, "Distinct(Filter(ShowColumns(t1, 'new_quantity', 'old_price'), new_quantity < 20), new_quantity)", 1)]
        [InlineData(7, "Filter(Distinct(ShowColumns(t1, 'new_quantity', 'old_price'), new_quantity), Value < 20)", 1)]
        
        // non primitive types are non delegable.
        [InlineData(8, "Distinct(t1, PolymorphicLookup)", -1)]
        
        // Other is a lookup field, hence not delegable.
        [InlineData(9, "Distinct(t1, Other)", -1)]
        [InlineData(10, "Distinct(et, Field1)", 2)]
        public async Task DistinctDelegationAsync(int id, string expr, int expectedRows, params string[] expectedWarnings)
        {
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "t2");
            map.Add("virtualremote", "t3");
            map.Add("elastictable", "et");
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: true, policy: policy);

            var opts = PluginExecutionTests._parserAllowSideEffects_NumberIsFloat;

            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);

            var inputs = DelegationTestUtility.TransformForWithFunction(expr, expectedWarnings?.Count() ?? 0);

            for (var i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                var check = engine1.Check(input, options: opts, symbolTable: dv.Symbols);
                Assert.True(check.IsSuccess);

                var scam = check.ScanDependencies(dv.MetadataCache);

                // compare IR to verify the delegations are happening exactly where we expect 
                var irNode = check.ApplyIR();
                var actualIr = check.GetCompactIRString();

                await DelegationTestUtility.CompareSnapShotAsync("DistinctDelegation.txt", actualIr, id, i == 1);

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

                // To check error cases.
                if (expectedRows < 0)
                {
                    Assert.IsType<ErrorValue>(result);
                }
                else
                {
                    Assert.IsAssignableFrom<TableValue>(result);
                    Assert.Equal(expectedRows, ((TableValue)result).Rows.Count());
                }
            }
        }
    }
}
