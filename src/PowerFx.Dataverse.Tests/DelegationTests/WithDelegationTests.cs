// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Threading.Tasks;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public partial class DelegationTests
    {
        [Theory]
        [TestPriority(1)]

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
            await DelegationTestAsync(id, "WithDelegation.txt", expr, expectedRows, null, null, cdsNumberIsFloat, parserNumberIsFloatOption, null, false, true, true, expectedWarnings);
        }
    }
}
