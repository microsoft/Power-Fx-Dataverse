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
    public class ShowColumnsDelegationTests
    {
        [Theory]

        [InlineData("FirstN(ShowColumns(t1, 'new_price', 'old_price'), 1)", 2, 1, true)]
        [InlineData("ShowColumns(FirstN(t1, 1), 'new_price', 'old_price')", 2, 2, true)]
        [InlineData("FirstN(Filter(ShowColumns(t1, 'new_price', 'old_price'), new_price < 120), 1)", 2, 3, true)]
        [InlineData("First(ShowColumns(t1, 'new_price', 'old_price'))", 2, 4, true)]
        [InlineData("First(Filter(ShowColumns(t1, 'new_price', 'old_price'), new_price < 120))", 2, 5, true)]
        [InlineData("LookUp(ShowColumns(t1, 'new_price', 'old_price'), new_price < 120)", 2, 6, true)]
        [InlineData("ShowColumns(Filter(t1, Price < 120), 'new_price')", 1, 7, true)]
        [InlineData("ShowColumns(Filter(t1, Price < 120), 'new_price', 'old_price')", 2, 8, true)]
        [InlineData("Filter(ShowColumns(t1, 'new_price', 'old_price'), new_price < 120)", 2, 9, true)]

        // This is not delegated, but doesn't impact perf.
        [InlineData("ShowColumns(LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\")), 'new_price')", 1, 10, true)]
        [InlineData("LookUp(ShowColumns(t1, 'localid'), localid=GUID(\"00000000-0000-0000-0000-000000000001\"))", 1, 11, true)]
        [InlineData("First(ShowColumns(ShowColumns(t1, 'localid'), 'localid'))", 1, 12, true)]
        [InlineData("First(ShowColumns(ShowColumns(t1, 'localid', 'new_price'), 'localid'))", 1, 13, true)]
        [InlineData("First(ShowColumns(ShowColumns(t1, 'localid'), 'new_price'))", 1, 14, false)]
        public async Task ShowColumnDelegationAsync(string expr, int expectedCount, int id, bool isCheckSuccess, params string[] expectedWarnings)
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

            if (!isCheckSuccess)
            {
                Assert.False(check.IsSuccess);
                return;
            }

            var scam = check.ScanDependencies(dv.MetadataCache);

            Assert.True(check.IsSuccess);

            // compare IR to verify the delegations are happening exactly where we expect 
            var irNode = check.ApplyIR();
            var actualIr = check.GetCompactIRString();

            await DelegationTestUtility.CompareSnapShotAsync("ShowColumnsDelegation.txt", actualIr, id, false);

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

            var columnCount = 0;
            if (result is TableValue tv)
            {
                columnCount = tv.Type.FieldNames.Count();
            }
            else if (result is RecordValue rv)
            {
                columnCount = rv.Type.FieldNames.Count();
            }

            Assert.Equal(expectedCount, columnCount);
        }
    }
}
