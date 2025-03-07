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

        // Here we only have 1 warning on the table t1 (which is 'local') and not on 'virtualremote' (t3) as LookUp is delegated
        [InlineData(1, "AddColumns(t1 As a, XXX, LookUp(t3 As b, a.Price = b.'Virtual Data'))", 4, "localid", "0001, 0003, 0004, 0005", "Warning 11-13: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(2, "AddColumns(t1 As a, XXX, LookUp(t3 As b, b.'Virtual Data' = a.Price))", 4, "localid", "0001, 0003, 0004, 0005", "Warning 11-13: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(3, "AddColumns(t1 As a, XXX, LookUp(t3 As b, a.Price < b.'Virtual Data'))", 4, "localid", "0001, 0003, 0004, 0005", "Warning 11-13: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(4, "AddColumns(t1 As a, XXX, LookUp(t3 As b, b.'Virtual Data' < a.Price))", 4, "localid", "0001, 0003, 0004, 0005", "Warning 11-13: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(5, "Filter(AddColumns(t1 As a, XXX, LookUp(t3 As b, b.'Virtual Data' < a.Price)), Price <= 10)", 3, "localid", "0003, 0004, 0005", "Warning 18-20: This operation on table 'local' may not work if it has more than 999 rows.")]
        public async Task AddColumnsDelegationAsync(int id, string expr, int expectedRows, string column, string expectedIds, params string[] expectedWarnings)
        {
            foreach (bool cdsNumberIsFloat in new[] { true, false })
            {
                foreach (bool parserNumberIsFloatOption in new[] { true, false })
                {
                    int i = 1 + (4 * (id - 1)) + (cdsNumberIsFloat ? 0 : 2) + (parserNumberIsFloatOption ? 0 : 1);
                    await DelegationTestAsync(i, "AddColumnsDelegation.txt", expr, expectedRows, expectedIds, ResultGetter(column), cdsNumberIsFloat, parserNumberIsFloatOption, null, true, true, true, false, expectedWarnings.Select(ew => parserNumberIsFloatOption ? ew : ew.Replace("LtNumbers", "LtDecimals")).ToArray());
                }
            }
        }
    }
}
