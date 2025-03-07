// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Threading.Tasks;
using Microsoft.PowerFx.Types;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public partial class DelegationTests
    {
        [Theory]
        [TestPriority(1)]
        [InlineData(1, "IsBlank(FirstN(t1, 1))", false)]
        [InlineData(2, "IsBlank(ShowColumns(Filter(t1, Price < 120), 'new_price'))", false)]
        [InlineData(3, "IsBlank(LookUp(t1, Price < -100))", true)]
        [InlineData(4, "IsBlank(Distinct(t1, Price))", false)]
        public async Task IsBlankDelegationAsync(int id, string expr, bool expected, params string[] expectedWarnings)
        {
            await DelegationTestAsync(id, "IsBlankDelegation.txt", expr, -2, expected, result => ((BooleanValue)result).Value, false, false, null, false, true, false, false, expectedWarnings);
        }
    }
}
