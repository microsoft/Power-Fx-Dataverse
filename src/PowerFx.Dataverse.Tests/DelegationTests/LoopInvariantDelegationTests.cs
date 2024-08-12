// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.PowerFx.Types;
using Xunit;
using Xunit.Sdk;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public partial class DelegationTests
    {
        [Theory]
        [TestPriority(1)]

        // No problem to delegate here as this is (field < expression) and expression will be evaluated at runtime
        [InlineData(1, "Filter(t1, DateTime < Today())", 1, "localid", "0001")]
        [InlineData(2, "Filter(t1, DateTime < DateAdd(Today(), -5, TimeUnit.Days))", 1, "localid", "0001")]

        // Delegation working here
        [InlineData(3, "Filter(t1, DateDiff(ThisRecord.DateTime, Today(), TimeUnit.Days) < 5)", 1, "localid", "0001")]
        [InlineData(4, "Filter(t1, DateAdd(ThisRecord.DateTime, -5, TimeUnit.Days) < Today())", 1, "localid", "0001")]

        // No delegation on * or / operations
        [InlineData(5, "Filter(t1, Price * 1.17 < 1000)", 4, "localid", "0001, 0003, 0004, 0005", "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(6, "Filter(t1, Price / 1.17 < 1000)", 4, "localid", "0001, 0003, 0004, 0005", "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Delegation working here as we can convert to Price < 1000 + 117
        [InlineData(7, "Filter(t1, Price - 117 < 1000)", 4, "localid", "0001, 0003, 0004, 0005")]

        // No delegation on * or / operations
        [InlineData(8, "Filter(t1, 1.17 * Price < 1000)", 4, "localid", "0001, 0003, 0004, 0005", "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        // No delegation as it would lead to inverting the operation: Price > (1.17 / 1000)
        [InlineData(9, "Filter(t1, 1.17 / Price < 1000)", 4, "localid", "0001, 0003, 0004, 0005", "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        // No delegation as it would lead to inverting the operation: Price > (117 - 1000)
        [InlineData(10, "Filter(t1, 117 - Price < 1000)", 4, "localid", "0001, 0003, 0004, 0005", "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Delegation working here
        [InlineData(11, "Filter(t1, ThisRecord.DateTime + 7 < Today())", 4, "localid", "0001, 0003, 0004, 0005")]
        [InlineData(12, "Filter(t1, ThisRecord.DateTime - 7 < Today())", 4, "localid", "0001, 0003, 0004, 0005")]
        [InlineData(13, "Filter(t1, 7 + ThisRecord.DateTime < Today())", 4, "localid", "0001, 0003, 0004, 0005")]
        
        // No delegation on * or / operations
        [InlineData(14, "Filter(t1, Price * -1.17 < 1000)", 4, "localid", "0001, 0003, 0004, 0005", "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(15, "Filter(t1, Price / -1.17 < 1000)", 4, "localid", "0001, 0003, 0004, 0005", "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Delegation working here        
        [InlineData(16, "Filter(t1, Price + 117 < 1000)", 4, "localid", "0001, 0003, 0004, 0005")]
        [InlineData(17, "Filter(t1, 117 + Price < 1000)", 4, "localid", "0001, 0003, 0004, 0005")]
        [InlineData(18, "Filter(t1, -117 + Price < 1000)", 4, "localid", "0001, 0003, 0004, 0005")]

        // No delegation on *, / or power operations
        [InlineData(19, "Filter(t1, -1.17 * Price < 1000)", 4, "localid", "0001, 0003, 0004, 0005", "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(20, "Filter(t1, Price ^ 2 < 1000)", 3, "localid", "0003, 0004, 0005", "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]        
        [InlineData(21, "Filter(t1, Price * (1.17 + 6) < 1000)", 4, "localid", "0001, 0003, 0004, 0005", "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(22, "Filter(t1, Price / (1.17 + 6) < 1000)", 4, "localid", "0001, 0003, 0004, 0005", "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        // DateAdd(column, N, days) Op FixedDate
        // DateDiff(column, date/number/decimal, days) Op FixedDate

        // column + 5 Op FixedDate ??

        // Can delegate
        [InlineData(23, "Filter(t1, Price - (117 + 6) < 1000)", 4, "localid", "0001, 0003, 0004, 0005")]
        [InlineData(24, "Filter(t1, Price - (117 * 6) < 1000)", 4, "localid", "0001, 0003, 0004, 0005")]        
        [InlineData(25, "Filter(t1, (1.17 + 6) + (Price + 50) < 1000)", 4, "localid", "0001, 0003, 0004, 0005")]
        [InlineData(26, "Filter(t1, 17 * (1.17 / 6) + (10 + (Price + 50)) < 1000)", 4, "localid", "0001, 0003, 0004, 0005")]

        [InlineData(27, "Filter(t1, ThisRecord.DateTime - 5 < Today())", 1, "localid", "0001")]
        public async Task LoopInvariantDelegationAsync(int id, string expr, int expectedRows, string column, string expectedIds, params string[] expectedWarnings)
        {
            await DelegationTestAsync(
                id,
                "LoopInvariantDelegation.txt",
                expr,
                expectedRows,
                expectedIds,
                (FormulaValue result) => result switch
                {
                    TableValue tv => string.Join(", ", tv.Rows.Select(drv => string.IsNullOrEmpty(column) ? EmptyColumn(drv.Value.Fields) : GetString(drv.Value.Fields.First(nv => nv.Name == column).Value)[^4..])),
                    RecordValue rv => GetString(rv.Fields.First(nv => nv.Name == column).Value),
                    _ => throw FailException.ForFailure("Unexpected result")
                },
                true,
                true,
                null,
                true,
                true,
                true,
                expectedWarnings);
        }        

        private static string EmptyColumn(IEnumerable<NamedValue> values)
        {
            Assert.Empty(values);
            return "∅";
        }
    }
}
