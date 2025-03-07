// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Linq;
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
        [InlineData(1, "Sort(t1, Price)", 4, "0004, 0003, 0005, 0001", false)]
        [InlineData(2, "Sort(t1, Price, SortOrder.Ascending)", 4, "0004, 0003, 0005, 0001", false)]
        [InlineData(3, "Sort(t1, Price, SortOrder.Descending)", 4, "0001, 0003, 0005, 0004", false)]

        // Non-delegable as it's a calculated column
        [InlineData(4, "Sort(t1, Price * 2, SortOrder.Descending)", 4, "0001, 0003, 0005, 0004", false, "Warning 5-7: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Non-delegable as FirstN needs to be executed first and Sort will occur in-memory
        [InlineData(5, "Sort(FirstN(t1, 5), Price)", 4, "0004, 0003, 0005, 0001", false)]

        // Delegable fully, both FirstN and Sort
        [InlineData(6, "FirstN(Sort(t1, Price), 2)", 2, "0004, 0003", false)]

        // Non-delegable
        [InlineData(7, "Sort(FirstN(t1, 1), Price)", 1, "0001", false)]
        [InlineData(8, "First(Sort(t1, Price))", 1, "0004", false)]
        [InlineData(9, "SortByColumns(t1, Price)", 4, "0004, 0003, 0005, 0001", false)]
        [InlineData(10, "SortByColumns(t1, Price, SortOrder.Ascending)", 4, "0004, 0003, 0005, 0001", false)]
        [InlineData(11, "SortByColumns(t1, Price, SortOrder.Descending)", 4, "0001, 0003, 0005, 0004", false)]

        // Non-delegable
        [InlineData(12, "SortByColumns(FirstN(t1, 5), Price)", 4, "0004, 0003, 0005, 0001", false)]
        [InlineData(13, "FirstN(SortByColumns(t1, Price), 2)", 2, "0004, 0003", false)]

        // Non-delegable
        [InlineData(14, "SortByColumns(FirstN(t1, 1), Price)", 1, "0001", false)]
        [InlineData(15, "First(SortByColumns(t1, Price))", 1, "0004", false)]
        [InlineData(16, "SortByColumns(t1, Price, SortOrder.Ascending, Quantity)", 4, "0004, 0003, 0005, 0001", false)]
        [InlineData(17, "SortByColumns(t1, Price, SortOrder.Ascending, Quantity, SortOrder.Ascending)", 4, "0004, 0003, 0005, 0001", false)]
        [InlineData(18, "SortByColumns(t1, Price, SortOrder.Descending, Quantity, SortOrder.Ascending)", 4, "0001, 0003, 0005, 0004", false)]
        [InlineData(19, "SortByColumns(t1, Price, SortOrder.Descending, Quantity, SortOrder.Descending)", 4, "0001, 0005, 0003, 0004", false)]

        // Sort non-delegable
        [InlineData(20, "Sort(FirstN(Filter(t1, Price <= 100), 2), Quantity)", 2, "0003, 0001", false)]

        // Delegable fully
        [InlineData(21, "FirstN(Sort(Filter(t1, Price <= 100), Quantity), 2)", 2, "0003, 0004", false)]
        [InlineData(22, "FirstN(Filter(Sort(t1, Quantity), Price <= 100), 2)", 2, "0003, 0004", false)]

        // Non-delegable
        [InlineData(23, @"Sort(t1, ""Price"")", 4, "0001, 0003, 0004, 0005", false, "Warning 5-7: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(24, @"Sort(t1, ""new_price"")", 4, "0001, 0003, 0004, 0005", false, "Warning 5-7: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(25, @"Sort(t1, ""XXXXX"")", 4, "0001, 0003, 0004, 0005", false, "Warning 5-7: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Delegable
        // Excluding this test for now due to DV issue 515
        // [InlineData(26, @"SortByColumns(t1, ""Price"")", 4, "0004, 0003, 0005, 0001")]
        [InlineData(27, @"SortByColumns(t1, ""new_price"")", 4, "0004, 0003, 0005, 0001", false)]

        // Can't delegate two SortByColumns
        [InlineData(28, "SortByColumns(SortByColumns(t1, Price, SortOrder.Descending), Quantity, SortOrder.Descending)", 4, "0001, 0005, 0003, 0004", false)]

        // Not a delegable table
        [InlineData(29, "Sort([30, 10, 20], Value)", 3, "10, 20, 30", true)]

        [InlineData(30, "Distinct(Sort(t1, Price), Price)", 3, "-10, 10, 100", true)]
        [InlineData(31, "LookUp(Sort(t1, Quantity), Price <= 100)", 1, "0003", false)]
        [InlineData(32, "Sort(Distinct(t1, Price), Value)", 3, "-10, 10, 100", true)]
        [InlineData(33, "ShowColumns(Sort(t1, Price), 'new_quantity', 'new_price', 'localid')", 4, "0004, 0003, 0005, 0001", false)]
        [InlineData(34, "Sort(ShowColumns(t1, 'new_quantity', 'new_price', 'localid'), new_price)", 4, "0004, 0003, 0005, 0001", false)]
        [InlineData(35, "Sort(Sort(t1, Price), Quantity)", 4, "0004, 0003, 0005, 0001", false)]
        [InlineData(36, "Sort(SortByColumns(t1, Price), Quantity)", 4, "0004, 0003, 0005, 0001", false)]
        [InlineData(37, "Sort(Filter(t1, Price > 0), Quantity)", 3, "0003, 0005, 0001", false)]
        [InlineData(38, "Sort(ForAll(t1, { Value: Price }), Value)", 4, "-10, 10, 10, 100", true)]
        [InlineData(39, "SortByColumns(Distinct(t1, Price), Value)", 3, "-10, 10, 100", true)]
        [InlineData(40, "SortByColumns(Filter(t1, Price > 0), Quantity)", 3, "0003, 0005, 0001", false)]
        [InlineData(41, "SortByColumns(Sort(t1, Price), Quantity)", 4, "0004, 0003, 0005, 0001", false)]
        [InlineData(42, "SortByColumns(ShowColumns(t1, 'new_quantity', 'new_price', 'localid'), new_price)", 4, "0004, 0003, 0005, 0001", false)]
        [InlineData(43, "SortByColumns(ForAll(t1, { Value: Price }), Value)", 4, "-10, 10, 10, 100", true)]
        public async Task SortDelegationAsync(int id, string expr, int expectedRows, string expectedIds, bool useValue, params string[] expectedWarning)
        {
            await DelegationTestAsync(
                id,
                "SortDelegation.txt",
                expr,
                expectedRows,
                expectedIds,
                result => result switch
                {
                    TableValue tv => useValue
                                        ? string.Join(", ", tv.Rows.Select(drv => GetString(drv.Value.Fields.First(nv => nv.Name == "Value").Value)))
                                        : string.Join(", ", tv.Rows.Select(drv => (drv.Value.Fields.First(nv => nv.Name == "localid").Value as GuidValue).Value.ToString()[^4..])),
                    RecordValue rv => (rv.Fields.First(nv => nv.Name == "localid").Value as GuidValue).Value.ToString()[^4..],
                    _ => throw FailException.ForFailure("Unexpected result")
                },
                true,
                true,
                null,
                true,
                true,
                true,
                expectedWarning);
        }
    }
}
