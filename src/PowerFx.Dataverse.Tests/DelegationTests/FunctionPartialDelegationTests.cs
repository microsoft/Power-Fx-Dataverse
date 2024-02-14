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
    public class FunctionPartialDelegationTests
    {
        [Theory]

        // do not give warning on tabular function, where source is delegable.
        [InlineData("Concat(Filter(t1, Price < 120), Price & \",\")",
           "100,10,-10,",
           1,
           false,
           false)]

        [InlineData("Concat(FirstN(t1, 2), Price & \",\")",
           "100,10,",
           2,
           false,
           false)]

        [InlineData("Concat(ShowColumns(t1, 'new_price'), new_price & \",\")",
           "100,10,-10,",
           3,
           false,
           false)]

        // Give warning when source is entire table.
        [InlineData("Concat(t1, Price & \",\")",
           "100,10,-10,",
           4,
           false,
           false,
           "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        public async Task FunctionPartialDelegationAsync(string expr, object expected, int id, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
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
            engine1.UpdateVariable("_count", FormulaValue.New(100m));

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.True(check.IsSuccess);

            var scam = check.ScanDependencies(dv.MetadataCache);

            // compare IR to verify the delegations are happening exactly where we expect 
            var irNode = check.ApplyIR();
            var actualIr = check.GetCompactIRString();

            await DelegationTestUtility.CompareSnapShotAsync("PartialFunctionDelegation.txt", actualIr, id, false);

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

            Assert.Equal(expected, result.ToObject());
        }
    }
}
