// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public partial class DelegationTests
    {
        [Theory]
        [TestPriority(1)]
        [InlineData(1, "Distinct(t1, Price)", 3)]
        [InlineData(2, "Distinct(t1, Quantity)", 2)]
        [InlineData(3, "Distinct(FirstN(t1, 2), Quantity)", 2)]
        [InlineData(4, "FirstN(Distinct(t1, Quantity), 2)", 2)]
        [InlineData(5, "Distinct(Filter(t1, Quantity < 30 And Price < 120), Quantity)", 2)]
        [InlineData(6, "Distinct(Filter(ShowColumns(t1, 'new_quantity', 'old_price'), new_quantity < 20), new_quantity)", 1)]
        [InlineData(7, "Filter(Distinct(ShowColumns(t1, 'new_quantity', 'old_price'), new_quantity), Value < 20)", 1)]

        // non primitive types are non delegable.
        [InlineData(8, "Distinct(t1, PolymorphicLookup)", -1)]

        // Other is a lookup field, hence not delegable.
        [InlineData(9, "Distinct(t1, Other)", -1)]
        [InlineData(10, "Distinct(et, Field1)", 2)]
        [InlineData(11, "Distinct(SortByColumns(t1, Price), Price)", 3)]
        [InlineData(12, "Distinct(Distinct(t1, Price), Value)", 3)]
        [InlineData(13, "Distinct(ShowColumns(t1, 'new_price'), 'new_price')", 3)]
        [InlineData(14, "Distinct(ForAll(t1, Price), Value)", 3)]
        [InlineData(15, "Distinct(ForAll(t1, {Xyz: Price}), Xyz)", 3)]
        public async Task DistinctDelegationAsync(int id, string expr, int expectedRows, params string[] expectedWarnings)
        {
            await DelegationTestAsync(id, "DistinctDelegation.txt", expr, expectedRows, null, null, true, true, null, false, true, true, expectedWarnings);
        }
    }
}
