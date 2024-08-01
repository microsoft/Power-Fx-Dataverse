// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Threading.Tasks;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public partial class DelegationTests
    {
        // Table 't1' has
        // 1st item with
        // Price = 100, Old_Price = 200,  Date = Date(2023, 6, 1), DateTime = DateTime(2023, 6, 1, 12, 0, 0)
        // 2nd item with
        // Price = 10
        // 3rd item with
        // Price = -10

        [Theory]
        [TestPriority(1)]

        //Basic case
        [InlineData(1, "First(t1).Price", 100.0, false, false)]
        [InlineData(2, "First(t1).Price", 100.0, true, true)]
        [InlineData(3, "First(t1).Price", 100.0, true, false)]
        [InlineData(4, "First(t1).Price", 100.0, false, true)]

        // Filter inside FirstN, both can be combined *(vice versa isn't true)*
        [InlineData(5, "First(Filter(t1, Price < 100)).Price", 10.0, false, false)]
        [InlineData(6, "First(Filter(t1, Price < 100)).Price", 10.0, true, true)]
        [InlineData(7, "First(Filter(t1, Price < 100)).Price", 10.0, true, false)]
        [InlineData(8, "First(Filter(t1, Price < 100)).Price", 10.0, false, true)]
        [InlineData(9, "First(FirstN(t1, 2)).Price", 100.0, false, false)]
        [InlineData(10, "First(FirstN(t1, 2)).Price", 100.0, true, true)]
        [InlineData(11, "First(FirstN(t1, 2)).Price", 100.0, true, false)]
        [InlineData(12, "First(FirstN(t1, 2)).Price", 100.0, false, true)]
        [InlineData(13, "First(Distinct(t1, Quantity)).Value", 20.0, false, false)]
        [InlineData(14, "First(Distinct(t1, Quantity)).Value", 20.0, true, true)]
        [InlineData(15, "First(Distinct(t1, Quantity)).Value", 20.0, true, false)]
        [InlineData(16, "First(Distinct(t1, Quantity)).Value", 20.0, false, true)]
        [InlineData(17, "First(et).Field1", 200.0, false, false)]
        [InlineData(18, "First(et).Field1", 200.0, true, true)]
        [InlineData(19, "First(et).Field1", 200.0, true, false)]
        [InlineData(20, "First(et).Field1", 200.0, false, true)]
        public async Task FirstDelegationAsync(int id, string expr, object expected, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
        {
            await DelegationTestAsync(id, "FirstDelegation.txt", expr, -2, expected, null, cdsNumberIsFloat, parserNumberIsFloatOption, null, false, true, true, expectedWarnings);
        }
    }
}
