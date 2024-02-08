using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using System.Threading;
using Xunit;
using System.Threading.Tasks;
using System.IO;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class DistinctDelegationTests
    {
        [Theory]
        [InlineData("Distinct(t1, Price)", 1, 3)]
        [InlineData("Distinct(t1, Quantity)", 2, 2)]
        [InlineData("Distinct(FirstN(t1, 2), Quantity)", 3, 2)]
        [InlineData("FirstN(Distinct(t1, Quantity), 2)", 4, 2)]
        [InlineData("Distinct(Filter(t1, Quantity < 30 And Price < 120), Quantity)", 5, 2)]
        [InlineData("Distinct(Filter(ShowColumns(t1, 'new_quantity', 'old_price'), new_quantity < 20), new_quantity)", 6, 1)]
        [InlineData("Filter(Distinct(ShowColumns(t1, 'new_quantity', 'old_price'), new_quantity), Value < 20)", 7, 1)]
        // non primitive types are non delegable.
        [InlineData("Distinct(t1, PolymorphicLookup)", 8, -1)]
        // Other is a lookup field, hence not delegable.
        [InlineData("Distinct(t1, Other)", 9, -1)]
        public async Task DistinctDelegationAsync(string expr, int id, int expectedRows, params string[] expectedWarnings)
        {
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "t2");
            map.Add("virtualremote", "t3");
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: true, policy: policy);

            var opts = PluginExecutionTests._parserAllowSideEffects_NumberIsFloat;

            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);

            var inputs = DelegationTestUtility.TransformForWithFunction(expr, expectedWarnings?.Count() ?? 0);

            for(var i= 0; i< inputs.Count; i++)
            {
                var input = inputs[i];
                var check = engine1.Check(input, options: opts, symbolTable: dv.Symbols);
                Assert.True(check.IsSuccess);

                var scam = check.ScanDependencies(dv.MetadataCache);

                // compare IR to verify the delegations are happening exactly where we expect 
                var irNode = check.ApplyIR();
                var actualIr = check.GetCompactIRString();

                await DelegationTestUtility.CompareSnapShotAsync("DistinctDelegation.txt", actualIr, id, i==1);

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

                var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

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
