//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

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
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price >90)}, Filter(r2, Price = First(Filter(r1, Price > 90)).Price)))", 1, 1, false, false)]
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price >90)}, Filter(r2, Price = First(Filter(r1, Price > 90)).Price)))", 1, 2, true, true)]
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price >90)}, Filter(r2, Price = First(Filter(r1, Price > 90)).Price)))", 1, 3, true, false)]
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price >90)}, Filter(r2, Price = First(Filter(r1, Price > 90)).Price)))", 1, 4, false, true)]

        [InlineData("With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90))", 1, 5, false, false)]
        [InlineData("With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90))", 1, 6, true, true)]
        [InlineData("With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90))", 1, 7, true, false)]
        [InlineData("With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90))", 1, 8, false, true)]

        [InlineData("With({r: t1}, With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90)))", 1, 9, false, false)]
        [InlineData("With({r: t1}, With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90)))", 1, 10, true, true)]
        [InlineData("With({r: t1}, With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90)))", 1, 11, true, false)]
        [InlineData("With({r: t1}, With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90)))", 1, 12, false, true)]

        [InlineData("With({r : Filter(t1, Price < 120)}, With({r: t1}, Filter(r, Price > 90)))", 1, 13, false, false)]
        [InlineData("With({r : Filter(t1, Price < 120)}, With({r: t1}, Filter(r, Price > 90)))", 1, 14, true, true)]
        [InlineData("With({r : Filter(t1, Price < 120)}, With({r: t1}, Filter(r, Price > 90)))", 1, 15, true, false)]
        [InlineData("With({r : Filter(t1, Price < 120)}, With({r: t1}, Filter(r, Price > 90)))", 1, 16, false, true)]

        // Second Scoped variable uses the first scoped variable. Still the second scoped variable is delegated.
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(r1, Price > 90)}, Filter(r2, Price = 100)))", 1, 17, false, false)]
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(r1, Price > 90)}, Filter(r2, Price = 100)))", 1, 18, true, true)]
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(r1, Price > 90)}, Filter(r2, Price = 100)))", 1, 19, true, false)]
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(r1, Price > 90)}, Filter(r2, Price = 100)))", 1, 20, false, true)]

        // inner lookup has filter and that should delegate.
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price > 90)}, Filter(t1, Price = LookUp(r1, Price = 100).Price)))", 1, 21, false, false,
            @"Warning 114-120: Delegation warning. The ""Filter"" part of this formula might not work correctly on large data sets.",
            @"Warning 18-20: This operation on table 'local' may not work if it has more than 999 rows.",
            @"Warning 89-90: Delegation warning. The ""Filter"" part of this formula might not work correctly on large data sets.",
            @"Warning 91-114: Delegation warning. The ""LookUp"" part of this formula might not work correctly on large data sets.")]
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price > 90)}, Filter(t1, Price = LookUp(r1, Price = 100).Price)))", 1, 22, true, true,
            @"Warning 114-120: Delegation warning. The ""Filter"" part of this formula might not work correctly on large data sets.", 
            @"Warning 18-20: This operation on table 'local' may not work if it has more than 999 rows.", 
            @"Warning 89-90: Delegation warning. The ""Filter"" part of this formula might not work correctly on large data sets.", 
            @"Warning 91-114: Delegation warning. The ""LookUp"" part of this formula might not work correctly on large data sets.")]            
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price > 90)}, Filter(t1, Price = LookUp(r1, Price = 100).Price)))", 1, 23, true, false,
            @"Warning 114-120: Delegation warning. The ""Filter"" part of this formula might not work correctly on large data sets.",
            @"Warning 18-20: This operation on table 'local' may not work if it has more than 999 rows.",
            @"Warning 89-90: Delegation warning. The ""Filter"" part of this formula might not work correctly on large data sets.",
            @"Warning 91-114: Delegation warning. The ""LookUp"" part of this formula might not work correctly on large data sets.")]
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price > 90)}, Filter(t1, Price = LookUp(r1, Price = 100).Price)))", 1, 24, false, true,
            @"Warning 114-120: Delegation warning. The ""Filter"" part of this formula might not work correctly on large data sets.",
            @"Warning 18-20: This operation on table 'local' may not work if it has more than 999 rows.",
            @"Warning 89-90: Delegation warning. The ""Filter"" part of this formula might not work correctly on large data sets.",
            @"Warning 91-114: Delegation warning. The ""LookUp"" part of this formula might not work correctly on large data sets.")]

        // With's first arg is not a record node directly, but still a record type.
        [InlineData("With(LookUp(t1, Old_Price > 100), Filter(t2, Data = Old_Price))", 1, 25, false, false)]
        [InlineData("With(LookUp(t1, Old_Price > 100), Filter(t2, Data = Old_Price))", 1, 26, true, true)]
        [InlineData("With(LookUp(t1, Old_Price > 100), Filter(t2, Data = Old_Price))", 1, 27, true, false)]
        [InlineData("With(LookUp(t1, Old_Price > 100), Filter(t2, Data = Old_Price))", 1, 28, false, true)]
        public async Task WithDelegationAsync(string expr, int expectedRows, int id, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
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
