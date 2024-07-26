using System.Linq;
using System.Threading.Tasks;
using Microsoft.PowerFx.Types;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class ForAllDelegationTests : DelegationTests
    {
        public ForAllDelegationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData(1, "ForAll([10,20,30], Value)", 3, "Value", "10, 20, 30")]
        [InlineData(2, "ForAll(t1, Price)", 4, "Value", "100, 10, -10, 10")]
        [InlineData(3, "ForAll(t1, { Price: Price })", 4, "Price", "100, 10, -10, 10")]
        [InlineData(4, "ForAll(t1, { Xyz: Price })", 4, "Xyz", "100, 10, -10, 10")]
        [InlineData(5, "ForAll(t1, { Price: Price, Price2: Price })", 4, "Price2", "100, 10, -10, 10")]
        [InlineData(6, "ForAll(t1, { Price: Price * 2 })", 4, "Price", "200, 20, -20, 20", "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(7, "First(ForAll(t1, Price))", 1, "Value", "100")]
        [InlineData(8, "First(ForAll(t1, { Price: Price }))", 1, "Price", "100")]
        [InlineData(9, "First(ForAll(t1, { Xyz: Price }))", 1, "Xyz", "100")]
        [InlineData(10, "First(ForAll(t1, { Price: Price, Price2: Price }))", 1, "Price2", "100")]
        [InlineData(11, "First(ForAll(t1, { Price: Price * 2 }))", 1, "Price", "200", "Warning 13-15: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(12, "FirstN(ForAll(t1, Price), 2)", 2, "Value", "100, 10")]
        [InlineData(13, "FirstN(ForAll(t1, { Price: Price }), 2)", 2, "Price", "100, 10")]
        [InlineData(14, "FirstN(ForAll(t1, { Xyz: Price }), 2)", 2, "Xyz", "100, 10")]
        [InlineData(15, "FirstN(ForAll(t1, { Price: Price, Price2: Price }), 2)", 2, "Price2", "100, 10")]
        [InlineData(16, "FirstN(ForAll(t1, { Price: Price * 2 }), 2)", 2, "Price", "200, 20", "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(17, "ForAll(Filter(t1, Price < 0 Or Price > 90), Price)", 2, "Value", "100, -10")]
        [InlineData(18, "ForAll(Sort(Filter(t1, Price < 0 Or Price > 90), Price), Price)", 2, "Value", "-10, 100")]
        [InlineData(19, "ForAll(Filter(Sort(t1, Price), Price < 0 Or Price > 90), Price)", 2, "Value", "-10, 100")]
        [InlineData(20, "ForAll(FirstN(t1, 3), { Price: Price, Price2: Price })", 3, "Price", "100, 10, -10")]
        [InlineData(21, "FirstN(ForAll(t1, { Price: Price, Price2: Price }), 3)", 3, "Price", "100, 10, -10")]
        [InlineData(22, "ForAll(Distinct(Filter(t1, Price > 0), Price), Value)", 2, "Value", "100, 10")]
        [InlineData(23, "Distinct(ForAll(Filter(t1, Price > 0), Price), Value)", 2, "Value", "100, 10")]
        [InlineData(24, "ForAll(Filter(t1, Price < 0 Or Price > 90), { x: Price })", 2, "x", "100, -10")]
        [InlineData(25, "ForAll(Sort(Filter(t1, Price < 0 Or Price > 90), Price), { x: Price })", 2, "x", "-10, 100")]
        [InlineData(26, "ForAll(Filter(Sort(t1, Price), Price < 0 Or Price > 90), { x: Price })", 2, "x", "-10, 100")]
        [InlineData(27, "ForAll(FirstN(t1, 3), { Price: Price, Price2: Price })", 3, "Price2", "100, 10, -10")]
        [InlineData(28, "FirstN(ForAll(t1, { Price: Price, Price2: Price }), 3)", 3, "Price2", "100, 10, -10")]
        [InlineData(29, "ForAll(ForAll(t1, Price), Value)", 4, "Value", "100, 10, -10, 10")]
        [InlineData(30, "ForAll(ForAll(t1, Price), { x: Value })", 4, "x", "100, 10, -10, 10")]
        [InlineData(31, "ForAll(ForAll(t1, { x: Price }), { x: x })", 4, "x", "100, 10, -10, 10")]
        [InlineData(32, "ForAll(ForAll(t1, { x: Price }), { y: x })", 4, "y", "100, 10, -10, 10")]
        [InlineData(33, "ForAll(ForAll(t1, { x: Price, y: LocalId }), { x: y, y: x })", 4, "y", "100, 10, -10, 10")]
        [InlineData(34, "ForAll(ForAll(t1, { x: Price, y: LocalId }), { x: y, y: x })", 4, "x", "00000000-0000-0000-0000-000000000001, 00000000-0000-0000-0000-000000000003, 00000000-0000-0000-0000-000000000004, 00000000-0000-0000-0000-000000000005")]
        public async Task ForAllDelegationAsync(int id, string expr, int expectedRows, string column, string expectedIds, params string[] expectedWarnings)
        {
            await DelegationTestAsync(id, "ForAllDelegation.txt", expr, expectedRows, expectedIds,
                (result) => result switch
                {
                    TableValue tv => string.Join(", ", tv.Rows.Select(drv => GetString(drv.Value.Fields.First(nv => nv.Name == column).Value))),
                    RecordValue rv => GetString(rv.Fields.First(nv => nv.Name == column).Value),
                    _ => throw FailException.ForFailure("Unexpected result")
                },
                true, true, null, true, true, expectedWarnings);
        }

        private static string GetString(FormulaValue fv) => fv?.ToObject()?.ToString() ?? "<Blank>";
    }
}
