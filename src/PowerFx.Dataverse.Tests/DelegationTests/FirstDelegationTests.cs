using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using System.Threading;
using Xunit;
using System.Threading.Tasks;

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
        [InlineData("First(t1).Price", 100.0, 1, false, false)]
        [InlineData("First(t1).Price", 100.0, 2, true, true)]
        [InlineData("First(t1).Price", 100.0, 3, true, false)]
        [InlineData("First(t1).Price", 100.0, 4, false, true)]

        // Filter inside FirstN, both can be combined *(vice versa isn't true)*
        [InlineData("First(Filter(t1, Price < 100)).Price", 10.0, 5, false, false)]
        [InlineData("First(Filter(t1, Price < 100)).Price", 10.0, 6, true, true)]
        [InlineData("First(Filter(t1, Price < 100)).Price", 10.0, 7, true, false)]
        [InlineData("First(Filter(t1, Price < 100)).Price", 10.0, 8, false, true)]

        [InlineData("First(FirstN(t1, 2)).Price", 100.0, 9, false, false)]
        [InlineData("First(FirstN(t1, 2)).Price", 100.0, 10, true, true)]
        [InlineData("First(FirstN(t1, 2)).Price", 100.0, 11, true, false)]
        [InlineData("First(FirstN(t1, 2)).Price", 100.0, 12, false, true)]

        [InlineData("First(Distinct(t1, Quantity)).Value", 20.0, 13, false, false)]
        [InlineData("First(Distinct(t1, Quantity)).Value", 20.0, 14, true, true)]
        [InlineData("First(Distinct(t1, Quantity)).Value", 20.0, 15, true, false)]
        [InlineData("First(Distinct(t1, Quantity)).Value", 20.0, 16, false, true)]
        public async Task FirstDelegationAsync(string expr, object expected, int id, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
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
            engine1.UpdateVariable("_count", FormulaValue.New(100));

            var inputs = DelegationTestUtility.TransformForWithFunction(expr, expectedWarnings?.Count() ?? 0);

            for ( var i = 0; i < inputs.Count(); i++)
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

                var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

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
