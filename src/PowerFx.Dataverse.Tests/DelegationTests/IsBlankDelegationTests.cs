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
    public class IsBlankDelegationTests
    {

        [Theory]

        [InlineData("IsBlank(FirstN(t1, 1))", false, 1)]
        [InlineData("IsBlank(ShowColumns(Filter(t1, Price < 120), 'new_price'))", false, 2)]
        [InlineData("IsBlank(LookUp(t1, Price < -100))", true, 3)]
        [InlineData("IsBlank(Distinct(t1, Price))", false, 4)]
        public async Task IsBlankDelegationAsync(string expr, bool expected, int id, params string[] expectedWarnings)
        {
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "t2");
            map.Add("virtualremote", "t3");
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: false, policy: policy);

            var opts = PluginExecutionTests._parserAllowSideEffects;

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

            await DelegationTestUtility.CompareSnapShotAsync("IsBlankDelegation.txt", actualIr, id, false);

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

            var result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.Equal(expected, ((BooleanValue)result).Value);
        }
    }
}
