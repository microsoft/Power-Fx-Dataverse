using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class WithDelegationTests
    {

        [Theory]

        //Inner first which can still be delegated.
        [InlineData(1, "With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price >90)}, Filter(r2, Price = First(Filter(r1, Price > 90)).Price)))", 1, false, false)]
        [InlineData(2, "With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price >90)}, Filter(r2, Price = First(Filter(r1, Price > 90)).Price)))", 1, true, true)]
        [InlineData(3, "With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price >90)}, Filter(r2, Price = First(Filter(r1, Price > 90)).Price)))", 1, true, false)]
        [InlineData(4, "With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price >90)}, Filter(r2, Price = First(Filter(r1, Price > 90)).Price)))", 1, false, true)]
        [InlineData(5, "With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90))", 1, false, false)]
        [InlineData(6, "With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90))", 1, true, true)]
        [InlineData(7, "With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90))", 1, true, false)]
        [InlineData(8, "With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90))", 1, false, true)]
        [InlineData(9, "With({r: t1}, With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90)))", 1, false, false)]
        [InlineData(10, "With({r: t1}, With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90)))", 1, true, true)]
        [InlineData(11, "With({r: t1}, With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90)))", 1, true, false)]
        [InlineData(12, "With({r: t1}, With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90)))", 1, false, true)]
        [InlineData(13, "With({r : Filter(t1, Price < 120)}, With({r: t1}, Filter(r, Price > 90)))", 1, false, false)]
        [InlineData(14, "With({r : Filter(t1, Price < 120)}, With({r: t1}, Filter(r, Price > 90)))", 1, true, true)]
        [InlineData(15, "With({r : Filter(t1, Price < 120)}, With({r: t1}, Filter(r, Price > 90)))", 1, true, false)]
        [InlineData(16, "With({r : Filter(t1, Price < 120)}, With({r: t1}, Filter(r, Price > 90)))", 1, false, true)]

        // Second Scoped variable uses the first scoped variable. Still the second scoped variable is delegated.
        [InlineData(17, "With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(r1, Price > 90)}, Filter(r2, Price = 100)))", 1, false, false)]
        [InlineData(18, "With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(r1, Price > 90)}, Filter(r2, Price = 100)))", 1, true, true)]
        [InlineData(19, "With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(r1, Price > 90)}, Filter(r2, Price = 100)))", 1, true, false)]
        [InlineData(20, "With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(r1, Price > 90)}, Filter(r2, Price = 100)))", 1, false, true)]

        // inner lookup has filter and that should delegate.
        [InlineData(21, "With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price > 90)}, Filter(t1, Price = LookUp(r1, Price = 100).Price)))", 1, false, false, "Warning 18-20: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(22, "With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price > 90)}, Filter(t1, Price = LookUp(r1, Price = 100).Price)))", 1, true, true, "Warning 18-20: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(23, "With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price > 90)}, Filter(t1, Price = LookUp(r1, Price = 100).Price)))", 1, true, false, "Warning 18-20: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(24, "With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price > 90)}, Filter(t1, Price = LookUp(r1, Price = 100).Price)))", 1, false, true, "Warning 18-20: This operation on table 'local' may not work if it has more than 999 rows.")]

        // With's first arg is not a record node directly, but still a record type.
        [InlineData(25, "With(LookUp(t1, Old_Price > 100), Filter(t2, Data = Old_Price))", 1, false, false)]
        [InlineData(26, "With(LookUp(t1, Old_Price > 100), Filter(t2, Data = Old_Price))", 1, true, true)]
        [InlineData(27, "With(LookUp(t1, Old_Price > 100), Filter(t2, Data = Old_Price))", 1, true, false)]
        [InlineData(28, "With(LookUp(t1, Old_Price > 100), Filter(t2, Data = Old_Price))", 1, false, true)]

        public async Task WithDelegationAsync(int id, string expr, int expectedRows, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
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

            await DelegationTestUtility.CompareSnapShotAsync("WithDelegation.txt", actualIr, id, false);

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
