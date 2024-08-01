// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class FirstNDelegationTests : DelegationTests
    {
        public FirstNDelegationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // Table 't1' has
        // 1st item with
        // Price = 100, Old_Price = 200,  Date = Date(2023, 6, 1), DateTime = DateTime(2023, 6, 1, 12, 0, 0)
        // 2nd item with
        // Price = 10
        // 3rd item with
        // Price = -10

        [Theory]

        // Basic case.
        [InlineData(1, "FirstN(t1, 2)", 2, false, false)]
        [InlineData(2, "FirstN(t1, 2)", 2, true, true)]
        [InlineData(3, "FirstN(t1, 2)", 2, true, false)]
        [InlineData(4, "FirstN(t1, 2)", 2, false, true)]

        // Variable as arg
        [InlineData(5, "FirstN(t1, _count)", 3, false, false)]
        [InlineData(6, "FirstN(t1, _count)", 3, true, true)]
        [InlineData(7, "FirstN(t1, _count)", 3, true, false)]
        [InlineData(8, "FirstN(t1, _count)", 3, false, true)]

        // Function as arg
        [InlineData(9, "FirstN(t1, If(1<0,_count, 1))", 1, false, false)]
        [InlineData(10, "FirstN(t1, If(1<0,_count, 1))", 1, true, true)]
        [InlineData(11, "FirstN(t1, If(1<0,_count, 1))", 1, true, false)]
        [InlineData(12, "FirstN(t1, If(1<0,_count, 1))", 1, false, true)]

        // Filter inside FirstN, both can be cominded (vice versa isn't true)
        [InlineData(13, "FirstN(Filter(t1, Price > 90), 10)", 1, false, false)]
        [InlineData(14, "FirstN(Filter(t1, Price > 90), 10)", 1, true, true)]
        [InlineData(15, "FirstN(Filter(t1, Price > 90), 10)", 1, true, false)]
        [InlineData(16, "FirstN(Filter(t1, Price > 90), 10)", 1, false, true)]

        // Aliasing prevents delegation.
        [InlineData(17, "With({r : t1}, FirstN(r, Float(100)))", 3, false, false)]
        [InlineData(18, "With({r : t1}, FirstN(r, 100))", 3, true, true)]
        [InlineData(19, "With({r : t1}, FirstN(r, 100))", 3, true, false)]
        [InlineData(20, "With({r : t1}, FirstN(r, 100))", 3, false, true)]

        // Error handling

        // Error propagates
        [InlineData(21, "FirstN(t1, 1/0)", -1, false, false)]
        [InlineData(22, "FirstN(t1, 1/0)", -1, true, true)]
        [InlineData(23, "FirstN(t1, 1/0)", -1, true, false)]
        [InlineData(24, "FirstN(t1, 1/0)", -1, false, true)]

        // Blank is treated as 0.
        [InlineData(25, "FirstN(t1, If(1<0, 1))", 0, false, false)]
        [InlineData(26, "FirstN(t1, If(1<0, 1))", 0, true, true)]
        [InlineData(27, "FirstN(t1, If(1<0, 1))", 0, true, false)]
        [InlineData(28, "FirstN(t1, If(1<0, 1))", 0, false, true)]

        //Inserts default second arg.
        [InlineData(29, "FirstN(t1)", 1, false, false)]
        [InlineData(30, "FirstN(t1)", 1, true, true)]
        [InlineData(31, "FirstN(t1)", 1, true, false)]
        [InlineData(32, "FirstN(t1)", 1, false, true)]
        [InlineData(33, "FirstN(et, 2)", 2, false, false)]
        [InlineData(34, "FirstN(et, 2)", 2, true, true)]
        [InlineData(35, "FirstN(et, 2)", 2, true, false)]
        [InlineData(36, "FirstN(et, 2)", 2, false, true)]
        public async Task FirstNDelegationAsync(int id, string expr, int expectedRows, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
        {
            await DelegationTestAsync(id, "FirstNDelegation.txt", expr, expectedRows, null, null, cdsNumberIsFloat, parserNumberIsFloatOption, (config) => config.Features.FirstLastNRequiresSecondArguments = false, false, true, true, expectedWarnings);
        }
    }
}
