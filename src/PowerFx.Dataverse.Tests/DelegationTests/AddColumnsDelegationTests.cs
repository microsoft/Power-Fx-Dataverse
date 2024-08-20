// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public partial class DelegationTests
    {
        [Theory]
        [TestPriority(1)]

        // t1 is 'local', t3 is 'virtualremote' - only 1 warning with t1 but no warning with t3
        [InlineData(1, "AddColumns(t1 As a, XXX, LookUp(t3 As b, a.Price = b.'Virtual Data'))", 4, "localid", "0001, 0003, 0004, 0005")]
        [InlineData(2, "AddColumns(t1 As a, XXX, LookUp(t3 As b, b.'Virtual Data' = a.Price))", 4, "localid", "0001, 0003, 0004, 0005")]
        [InlineData(3, "AddColumns(t1 As a, XXX, LookUp(t3 As b, a.Price < b.'Virtual Data'))", 4, "localid", "0001, 0003, 0004, 0005")]
        [InlineData(4, "AddColumns(t1 As a, XXX, LookUp(t3 As b, b.'Virtual Data' < a.Price))", 4, "localid", "0001, 0003, 0004, 0005")]

        public async Task AddColumnsDelegationAsync(int id, string expr, int expectedRows, string column, string expectedIds, params string[] expectedWarnings)
        {
            foreach (bool cdsNumberIsFloat in new[] { true, false })
            {
                foreach (bool parserNumberIsFloatOption in new[] { true, false })
                {
                    int i = 1 + (4 * (id - 1)) + (cdsNumberIsFloat ? 0 : 2) + (parserNumberIsFloatOption ? 0 : 1);
                    await DelegationTestAsync(i, "AddColumnsDelegation.txt", expr, expectedRows, expectedIds, ResultGetter(column), cdsNumberIsFloat, parserNumberIsFloatOption, null, true, true, true, expectedWarnings.Select(ew => parserNumberIsFloatOption ? ew : ew.Replace("LtNumbers", "LtDecimals")).ToArray());
                }
            }
        }       
    }
}
