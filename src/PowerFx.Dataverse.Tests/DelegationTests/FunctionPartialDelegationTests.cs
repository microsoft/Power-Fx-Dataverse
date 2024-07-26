using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class FunctionPartialDelegationTests : DelegationTests
    {
        public FunctionPartialDelegationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]

        // do not give warning on tabular function, where source is delegable.
        [InlineData(1, "Concat(Filter(t1, Price < 120), Price & \",\")", "100,10,-10,", false, false)]
        [InlineData(2, "Concat(FirstN(t1, 2), Price & \",\")", "100,10,", false, false)]
        [InlineData(3, "Concat(ShowColumns(t1, 'new_price'), new_price & \",\")", "100,10,-10,", false, false)]

        // Give warning when source is entire table.
        [InlineData(4, "Concat(t1, Price & \",\")", "100,10,-10,", false, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        public async Task FunctionPartialDelegationAsync(int id, string expr, object expected, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
        {
            await DelegationTestAsync(id, "PartialFunctionDelegation.txt", expr, -2, expected, result => result.ToObject(), cdsNumberIsFloat, parserNumberIsFloatOption, null, false, true, expectedWarnings);
        }
    }
}
