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
        [InlineData(1, "FirstN(ShowColumns(t1, 'new_price', 'old_price'), 1)", 2, true)]
        [InlineData(2, "ShowColumns(FirstN(t1, 1), 'new_price', 'old_price')", 2, true)]
        [InlineData(3, "FirstN(Filter(ShowColumns(t1, 'new_price', 'old_price'), new_price < 120), 1)", 2, true)]
        [InlineData(4, "First(ShowColumns(t1, 'new_price', 'old_price'))", 2, true)]
        [InlineData(5, "First(Filter(ShowColumns(t1, 'new_price', 'old_price'), new_price < 120))", 2, true)]
        [InlineData(6, "LookUp(ShowColumns(t1, 'new_price', 'old_price'), new_price < 120)", 2, true)]
        [InlineData(7, "ShowColumns(Filter(t1, Price < 120), 'new_price')", 1, true)]
        [InlineData(8, "ShowColumns(Filter(t1, Price < 120), 'new_price', 'old_price')", 2, true)]
        [InlineData(9, "Filter(ShowColumns(t1, 'new_price', 'old_price'), new_price < 120)", 2, true)]

        // This is not delegated, but doesn't impact perf.
        [InlineData(10, "ShowColumns(LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\")), 'new_price')", 1, true)]
        [InlineData(11, "LookUp(ShowColumns(t1, 'localid'), localid=GUID(\"00000000-0000-0000-0000-000000000001\"))", 1, true)]
        [InlineData(12, "First(ShowColumns(ShowColumns(t1, 'localid'), 'localid'))", 1, true)]
        [InlineData(13, "First(ShowColumns(ShowColumns(t1, 'localid', 'new_price'), 'localid'))", 1, true)]
        [InlineData(14, "First(ShowColumns(ShowColumns(t1, 'localid'), 'new_price'))", 1, false)]
        [InlineData(15, "ShowColumns(Distinct(t1, 'new_price'), Value)", 1, true)]
        [InlineData(16, "ShowColumns(SortByColumns(t1, Price), 'new_price')", 1, true)]
        [InlineData(17, "ShowColumns(ShowColumns(t1, Price), 'new_price')", 1, true)]
        [InlineData(18, "ShowColumns(ForAll(t1, Price), Value)", 1, true)]
        [InlineData(19, "ShowColumns(ForAll(t1, { z: Price }), z)", 1, true)]
        public async Task ShowColumnDelegationAsync(int id, string expr, int expectedCount, bool isCheckSuccess, params string[] expectedWarnings)
        {
            await DelegationTestAsync(
                id,
                "ShowColumnsDelegation.txt",
                expr,
                -2,
                expectedCount,
                result => result switch
                {
                    TableValue tv => tv.Type.FieldNames.Count(),
                    RecordValue rv => rv.Type.FieldNames.Count(),
                    _ => throw FailException.ForFailure("Unexpected result")
                },
                false,
                false,
                null,
                false,
                isCheckSuccess,
                false,
                expectedWarnings);
        }
    }
}
