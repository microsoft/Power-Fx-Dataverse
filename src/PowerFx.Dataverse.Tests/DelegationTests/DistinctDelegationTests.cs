using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using System.Threading;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class DistinctDelegationTests
    {
        [Theory]
        [InlineData("Distinct(t1, Price)", 3, "__retrieveMultiple(local, __noFilter(), 999, True, new_price)", true, true)]
        [InlineData("Distinct(t1, Quantity)", 2, "__retrieveMultiple(local, __noFilter(), 999, True, new_quantity)", true, true)]
        [InlineData("Distinct(FirstN(t1, 2), Quantity)", 1, "Distinct(__retrieveMultiple(local, __noFilter(), 2, False), (new_quantity))", true, true)]
        [InlineData("FirstN(Distinct(t1, Quantity), 2)", 2, "__retrieveMultiple(local, __noFilter(), 2, True, new_quantity)", true, true)]
        [InlineData("Distinct(Filter(ShowColumns(t1, 'new_price', 'old_price'), new_price < 120), new_price)", 3, "__retrieveMultiple(local, __lt(local, new_price, 120), 999, True, new_price)", true, true)]
        [InlineData("Filter(Distinct(ShowColumns(t1, 'new_price', 'old_price'), new_price), Value < 120)", 3, "__retrieveMultiple(local, __lt(local, new_price, 120), 999, True, new_price)", true, true)]
        public void DistinctDelegation(string expr, int expectedRows, string expectedIr, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
        {
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "t2");
            map.Add("virtualremote", "t3");
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: cdsNumberIsFloat, policy: policy);

            var opts = parserNumberIsFloatOption ?
                PluginExecutionTests._parserAllowSideEffects_NumberIsFloat :
                PluginExecutionTests._parserAllowSideEffects;

            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);

            var inputs = DelegationTests.TransformForWithFunction(expr, expectedIr, expectedWarnings?.Count() ?? 0);

            foreach (var input in inputs)
            {
                expr = input.Item1;
                expectedIr = input.Item2;

                var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
                Assert.True(check.IsSuccess);

                var scam = check.ScanDependencies(dv.MetadataCache);

                // compare IR to verify the delegations are happening exactly where we expect 
                var irNode = check.ApplyIR();
                var actualIr = check.GetCompactIRString();
                Assert.Equal(expectedIr, actualIr);

                // Validate delegation warnings.
                // error.ToString() will capture warning status, message, and source span. 
                var errors = check.ApplyErrors();

                var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

                Assert.Equal(expectedWarnings.Length, errorList.Length);
                for (var i = 0; i < errorList.Length; i++)
                {
                    Assert.Equal(expectedWarnings[i], errorList[i]);
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
