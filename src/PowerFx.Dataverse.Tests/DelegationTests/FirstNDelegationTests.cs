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
    public class FirstNDelegationTests
    {
        // Table 't1' has
        // 1st item with
        // Price = 100, Old_Price = 200,  Date = Date(2023, 6, 1), DateTime = DateTime(2023, 6, 1, 12, 0, 0)
        // 2nd item with
        // Price = 10
        // 3rd item with
        // Price = -10

        [Theory]

        // Basic case.
        [InlineData("FirstN(t1, 2)", 2, 1, false, false)]
        [InlineData("FirstN(t1, 2)", 2, 2, true, true)]
        [InlineData("FirstN(t1, 2)", 2, 3, true, false)]
        [InlineData("FirstN(t1, 2)", 2, 4, false, true)]

        // Variable as arg 
        [InlineData("FirstN(t1, _count)", 3, 5, false, false)]
        [InlineData("FirstN(t1, _count)", 3, 6, true, true)]
        [InlineData("FirstN(t1, _count)", 3, 7, true, false)]
        [InlineData("FirstN(t1, _count)", 3, 8, false, true)]

        // Function as arg 
        [InlineData("FirstN(t1, If(1<0,_count, 1))", 1, 9, false, false)]
        [InlineData("FirstN(t1, If(1<0,_count, 1))", 1, 10, true, true)]
        [InlineData("FirstN(t1, If(1<0,_count, 1))", 1, 11, true, false)]
        [InlineData("FirstN(t1, If(1<0,_count, 1))", 1, 12, false, true)]

        // Filter inside FirstN, both can be cominded (vice versa isn't true)
        [InlineData("FirstN(Filter(t1, Price > 90), 10)", 1, 13, false, false)]
        [InlineData("FirstN(Filter(t1, Price > 90), 10)", 1, 14, true, true)]
        [InlineData("FirstN(Filter(t1, Price > 90), 10)", 1, 15, true, false)]
        [InlineData("FirstN(Filter(t1, Price > 90), 10)", 1, 16, false, true)]

        // Aliasing prevents delegation. 
        [InlineData("With({r : t1}, FirstN(r, Float(100)))", 3, 17, false, false)]
        [InlineData("With({r : t1}, FirstN(r, 100))", 3, 18, true, true)]
        [InlineData("With({r : t1}, FirstN(r, 100))", 3, 19, true, false)]
        [InlineData("With({r : t1}, FirstN(r, 100))", 3, 20, false, true)]

        // Error handling

        // Error propagates
        [InlineData("FirstN(t1, 1/0)", -1, 21, false, false)]
        [InlineData("FirstN(t1, 1/0)", -1, 22, true, true)]
        [InlineData("FirstN(t1, 1/0)", -1, 23, true, false)]
        [InlineData("FirstN(t1, 1/0)", -1, 24, false, true)]

        // Blank is treated as 0.
        [InlineData("FirstN(t1, If(1<0, 1))", 0, 25, false, false)]
        [InlineData("FirstN(t1, If(1<0, 1))", 0, 26, true, true)]
        [InlineData("FirstN(t1, If(1<0, 1))", 0, 27, true, false)]
        [InlineData("FirstN(t1, If(1<0, 1))", 0, 28, false, true)]

        //Inserts default second arg.
        [InlineData("FirstN(t1)", 1, 29, false, false)]
        [InlineData("FirstN(t1)", 1, 30, true, true)]
        [InlineData("FirstN(t1)", 1, 31, true, false)]
        [InlineData("FirstN(t1)", 1, 32, false, true)]

        [InlineData("FirstN(et, 2)", 2, 33, false, false)]
        [InlineData("FirstN(et, 2)", 2, 34, true, true)]
        [InlineData("FirstN(et, 2)", 2, 35, true, false)]
        [InlineData("FirstN(et, 2)", 2, 36, false, true)]
        public async Task FirstNDelegationAsync(string expr, int expectedRows, int id, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
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
            config.Features.FirstLastNRequiresSecondArguments = false;
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_count", FormulaValue.New(100));

            var inputs = DelegationTestUtility.TransformForWithFunction(expr, expectedWarnings?.Count() ?? 0);

            for(var i = 0;i<inputs.Count(); i++)
            {
                expr = inputs[i];

                var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
                Assert.True(check.IsSuccess);

                // compare IR to verify the delegations are happening exactly where we expect 
                var irNode = check.ApplyIR();
                var actualIr = check.GetCompactIRString();

                await DelegationTestUtility.CompareSnapShotAsync("FirstNDelegation.txt", actualIr, id, i == 1);

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
