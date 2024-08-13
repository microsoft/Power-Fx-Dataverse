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
        [InlineData(3, "Filter(t1, DateAdd(ThisRecord.DateTime, -5) < Today())", 1, "localid", "0001")]
        [InlineData(4, "Filter(t1, DateAdd(ThisRecord.DateTime, 5) < Today())", 1, "localid", "0001")]
        [InlineData(5, "Filter(t1, DateAdd(ThisRecord.DateTime, -5, TimeUnit.Days) < Today())", 1, "localid", "0001")]
        [InlineData(6, "Filter(t1, DateAdd(ThisRecord.DateTime, 5, TimeUnit.Days) < Today())", 1, "localid", "0001")]
        [InlineData(7, "Filter(t1, DateAdd(ThisRecord.DateTime, -5, TimeUnit.Hours) < Today())", 1, "localid", "0001")]
        [InlineData(8, "Filter(t1, DateAdd(ThisRecord.DateTime, 5, TimeUnit.Hours) < Today())", 1, "localid", "0001")]

        // This set will delegate like previous 6 tests
        [InlineData(9, "Filter(t1, Today() > DateAdd(ThisRecord.DateTime, -5))", 1, "localid", "0001")]
        [InlineData(10, "Filter(t1, Today() > DateAdd(ThisRecord.DateTime, 5))", 1, "localid", "0001")]
        [InlineData(11, "Filter(t1, Today() > DateAdd(ThisRecord.DateTime, -5, TimeUnit.Days))", 1, "localid", "0001")]
        [InlineData(12, "Filter(t1, Today() > DateAdd(ThisRecord.DateTime, 5, TimeUnit.Days))", 1, "localid", "0001")]
        [InlineData(13, "Filter(t1, Today() > DateAdd(ThisRecord.DateTime, -5, TimeUnit.Hours))", 1, "localid", "0001")]
        [InlineData(14, "Filter(t1, Today() > DateAdd(ThisRecord.DateTime, 5, TimeUnit.Hours))", 1, "localid", "0001")]

        // Same delegation result
        [InlineData(15, "Filter(t1, DateDiff(ThisRecord.DateTime, Today()) < 5)", 0, "localid", null)]
        [InlineData(16, "Filter(t1, DateDiff(Today(), ThisRecord.DateTime) > -5)", 0, "localid", null)]
        [InlineData(17, "Filter(t1, 5 > DateDiff(ThisRecord.DateTime, Today()))", 0, "localid", null)]
        [InlineData(18, "Filter(t1, -5 < DateDiff(Today(), ThisRecord.DateTime))", 0, "localid", null)]

        [InlineData(19, "Filter(t1, DateDiff(ThisRecord.DateTime, Today()) < -5)", 0, "localid", null)]
        [InlineData(20, "Filter(t1, DateDiff(Today(), ThisRecord.DateTime) > 5)", 0, "localid", null)]      
        [InlineData(21, "Filter(t1, -5 > DateDiff(ThisRecord.DateTime, Today()))", 0, "localid", null)]
        [InlineData(22, "Filter(t1, 5 < DateDiff(Today(), ThisRecord.DateTime))", 0, "localid", null)]

        // No delegation in these cases
        [InlineData(23, "Filter(t1, ThisRecord.DateTime + 7 < Today())", 4, "localid", "0001, 0003, 0004, 0005", "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(24, "Filter(t1, ThisRecord.DateTime - 7 < Today())", 4, "localid", "0001, 0003, 0004, 0005", "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(25, "Filter(t1, 7 + ThisRecord.DateTime < Today())", 4, "localid", "0001, 0003, 0004, 0005", "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(26, "Filter(t1, ThisRecord.DateTime - 5 < Today())", 4, "localid", "0001, 0003, 0004, 0005", "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData(27, "LookUp(t1, DateDiff(DateTime, Today()) < 20000)", 1, "localid", "0001")]
        [InlineData(28, "LookUp(t1, DateDiff(DateTime, DateTime) < 2)", 1, "localid", "0001", "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(29, "LookUp(t1, DateDiff(DateTime, DateTime+0) < 2)", 1, "localid", "0001", "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(30, "LookUp(t1, DateAdd(DateTime, 2) <  DateAdd(DateTime, 2))", 0, "localid", null, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        public async Task LoopInvariantDelegationAsync(int id, string expr, int expectedRows, string column, string expectedIds, params string[] expectedWarnings)
        {
            foreach (bool cdsNumberIsFloat in new[] { true, false })
            {
                foreach (bool parserNumberIsFloatOption in new[] { true, false })
                {
                    int i = 1 + (4 * (id - 1)) + (cdsNumberIsFloat ? 0 : 2) + (parserNumberIsFloatOption ? 0 : 1);
                    await DelegationTestAsync(i, "LoopInvariantDelegation.txt", expr, expectedRows, expectedIds, ResultGetter(column), cdsNumberIsFloat, parserNumberIsFloatOption, null, true, true, true, expectedWarnings);
                }
            }
        }

        private static Func<FormulaValue, object> ResultGetter(string column)
        {
            return (FormulaValue result) => result switch
            {
                TableValue tv => string.Join(", ", tv.Rows.Select(drv => string.IsNullOrEmpty(column) ? EmptyColumn(drv.Value.Fields) : GetString(drv.Value.Fields.First(nv => nv.Name == column).Value)[^4..])),
                RecordValue rv => GetString(rv.Fields.First(nv => nv.Name == column).Value)[^4..],
                _ => throw FailException.ForFailure("Unexpected result")
            };
        }

        private static string EmptyColumn(IEnumerable<NamedValue> values)
        {
            Assert.Empty(values);
            return "∅";
        }
    }
}
