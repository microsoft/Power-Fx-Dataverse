//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class FilterDelegationTests
    {
        // Table 't1' has
        // 1st item with
        // Price = 100, Old_Price = 200,  Date = Date(2023, 6, 1), DateTime = DateTime(2023, 6, 1, 12, 0, 0), Currency = 100
        // 2nd item with
        // Price = 10
        // 3rd item with
        // Price = -10

        [Theory]

        //Basic case
        [InlineData("Filter(t1, Price < 100)", 2, 1, false, false)]
        [InlineData("Filter(t1, Price < 100)", 2, 2, true, true)]
        [InlineData("Filter(t1, Price < 100)", 2, 3, true, false)]
        [InlineData("Filter(t1, Price < 100)", 2, 4, false, true)]

        [InlineData("Filter(t1, Price <= 100)", 3, 5, false, false)]
        [InlineData("Filter(t1, Price <= 100)", 3, 6, true, true)]
        [InlineData("Filter(t1, Price <= 100)", 3, 7, true, false)]
        [InlineData("Filter(t1, Price <= 100)", 3, 8, false, true)]

        [InlineData("Filter(t1, Price = 100)", 1, 9, false, false)]
        [InlineData("Filter(t1, Price = 100)", 1, 10, true, true)]
        [InlineData("Filter(t1, Price = 100)", 1, 11, true, false)]
        [InlineData("Filter(t1, Price = 100)", 1, 12, false, true)]

        [InlineData("Filter(t1, Price > 100)", 0, 13, false, false)]
        [InlineData("Filter(t1, Price > 100)", 0, 14, true, true)]
        [InlineData("Filter(t1, Price > 100)", 0, 15, true, false)]
        [InlineData("Filter(t1, Price > 100)", 0, 16, false, true)]

        [InlineData("Filter(t1, Price >= 100)", 1, 17, false, false)]
        [InlineData("Filter(t1, Price >= 100)", 1, 18, true, true)]
        [InlineData("Filter(t1, Price >= 100)", 1, 19, true, false)]
        [InlineData("Filter(t1, Price >= 100)", 1, 20, false, true)]

        [InlineData("Filter(t1, Price < Float(120))", 3, 21, false, false)]
        [InlineData("Filter(t1, Price < Float(120))", 3, 22, true, true)]
        [InlineData("Filter(t1, Price < Float(120))", 3, 23, true, false)]
        [InlineData("Filter(t1, Price < Float(120))", 3, 24, false, true)]

        [InlineData("Filter(t1, Price < Decimal(20))", 2, 25, false, false)]
        [InlineData("Filter(t1, Price < Decimal(20))", 2, 26, true, true)]
        [InlineData("Filter(t1, Price < Decimal(20))", 2, 27, true, false)]
        [InlineData("Filter(t1, Price < Decimal(20))", 2, 28, false, true)]

        [InlineData("Filter(t1, Price < Abs(-120))", 3, 29, false, false)]
        [InlineData("Filter(t1, Price < Abs(-120))", 3, 30, true, true)]
        [InlineData("Filter(t1, Price < Abs(-120))", 3, 31, true, false)]
        [InlineData("Filter(t1, Price < Abs(-120))", 3, 32, false, true)]

        // These two tests have Coalesce for numeric literals where the NumberIsFloat version does not.
        // Although not wrong, they should be the same.  Being tracked with https://github.com/microsoft/Power-Fx/issues/1609
        // Date
        [InlineData("Filter(t1, Date = Date(2023, 6, 1))", 1, 33, false, false)]
        [InlineData("Filter(t1, Date = Date(2023, 6, 1))", 1, 34, true, true)]
        [InlineData("Filter(t1, Date = Date(2023, 6, 1))", 1, 35, true, false)]
        [InlineData("Filter(t1, Date = Date(2023, 6, 1))", 1, 36, false, true)]

        // DateTime with coercion
        [InlineData("Filter(t1, DateTime = Date(2023, 6, 1))", 0, 37, false, false)]
        [InlineData("Filter(t1, DateTime = Date(2023, 6, 1))", 0, 38, true, true)]
        [InlineData("Filter(t1, DateTime = Date(2023, 6, 1))", 0, 39, true, false)]
        [InlineData("Filter(t1, DateTime = Date(2023, 6, 1))", 0, 40, false, true)]

        [InlineData("With({r: t1}, Filter(r, DateTime = Date(2023, 6, 1)))", 0, 41, false, false)]
        [InlineData("With({r: t1}, Filter(r, DateTime = Date(2023, 6, 1)))", 0, 42, true, true)]
        [InlineData("With({r: t1}, Filter(r, DateTime = Date(2023, 6, 1)))", 0, 43, true, false)]
        [InlineData("With({r: t1}, Filter(r, DateTime = Date(2023, 6, 1)))", 0, 44, false, true)]

        //Order doesn't matter
        [InlineData("Filter(t1, 0 > Price)", 1, 45, false, false)]
        [InlineData("Filter(t1, 0 > Price)", 1, 46, true, true)]
        [InlineData("Filter(t1, 0 > Price)", 1, 47, true, false)]
        [InlineData("Filter(t1, 0 > Price)", 1, 48, false, true)]

        // Variable as arg
        [InlineData("Filter(t1, Price > _count)", 0, 49, false, false)]
        [InlineData("Filter(t1, Price > _count)", 0, 50, true, true)]
        [InlineData("Filter(t1, Price > _count)", 0, 51, true, false)]
        [InlineData("Filter(t1, Price > _count)", 0, 52, false, true)]

        // Function as arg
        [InlineData("Filter(t1, Price > If(1<0,_count, 1))", 2, 53, false, false)]
        [InlineData("Filter(t1, Price > If(1<0,_count, 1))", 2, 54, true, true)]
        [InlineData("Filter(t1, Price > If(1<0,_count, 1))", 2, 55, true, false)]
        [InlineData("Filter(t1, Price > If(1<0,_count, 1))", 2, 56, false, true)]

        // Filter nested in another function both delegated.
        [InlineData("Filter(Filter(t1, Price > 0), Price < 100)", 1, 57, false, false)]
        [InlineData("Filter(Filter(t1, Price > 0), Price < 100)", 1, 58, true, true)]
        [InlineData("Filter(Filter(t1, Price > 0), Price < 100)", 1, 59, true, false)]
        [InlineData("Filter(Filter(t1, Price > 0), Price < 100)", 1, 60, false, true)]

        // Basic case with And
        [InlineData("Filter(t1, Price < 120 And 90 < Price)", 1, 61, false, false)]
        [InlineData("Filter(t1, Price < 120 And 90 < Price)", 1, 62, true, true)]
        [InlineData("Filter(t1, Price < 120 And 90 < Price)", 1, 63, true, false)]
        [InlineData("Filter(t1, Price < 120 And 90 < Price)", 1, 64, false, true)]

        // Basic case with Or
        [InlineData("Filter(t1, Price < 0 Or Price > 90)", 2, 65, false, false)]
        [InlineData("Filter(t1, Price < 0 Or Price > 90)", 2, 66, true, true)]
        [InlineData("Filter(t1, Price < 0 Or Price > 90)", 2, 67, true, false)]
        [InlineData("Filter(t1, Price < 0 Or Price > 90)", 2, 68, false, true)]

        // Delegation Not Allowed

        // predicate that uses function that is not delegable.
        [InlineData("Filter(t1, IsBlank(Price))", 0, 69, false, false)]
        [InlineData("Filter(t1, IsBlank(Price))", 0, 70, true, true)]
        [InlineData("Filter(t1, IsBlank(Price))", 0, 71, true, false)]
        [InlineData("Filter(t1, IsBlank(Price))", 0, 72, false, true)]

        [InlineData("Filter(t1, Price < 200 And IsBlank(Old_Price))", 1, 73, false, false)]
        [InlineData("Filter(t1, Price < 200 And IsBlank(Old_Price))", 1, 74, true, true)]
        [InlineData("Filter(t1, Price < 200 And IsBlank(Old_Price))", 1, 75, true, false)]
        [InlineData("Filter(t1, Price < 200 And IsBlank(Old_Price))", 1, 76, false, true)]

        // predicate that uses function that is not delegable.
        [InlineData("Filter(t1, Price < 120 And IsBlank(_count))", 0, 77, false, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(t1, Price < 120 And IsBlank(_count))", 0, 78, true, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(t1, Price < 120 And IsBlank(_count))", 0, 79, true, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(t1, Price < 120 And IsBlank(_count))", 0, 80, false, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData("With({r: t1}, Filter(r, Price < 120 And IsBlank(_count)))", 0, 81, false, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r: t1}, Filter(r, Price < 120 And IsBlank(_count)))", 0, 82, true, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r: t1}, Filter(r, Price < 120 And IsBlank(_count)))", 0, 83, true, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r: t1}, Filter(r, Price < 120 And IsBlank(_count)))", 0, 84, false, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Filter nested in FirstN function. Only FirstN is delegated.
        [InlineData("Filter(FirstN(t1, 100), Price = 100)", 1, 85, false, false, "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(FirstN(t1, 100), Price = 100)", 1, 86, true, true, "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(FirstN(t1, 100), Price = 100)", 1, 87, true, false, "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(FirstN(t1, 100), Price = 100)", 1, 88, false, true, "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData("With({r: t1}, Filter(FirstN(r, 100), Price = 100))", 1, 89, false, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r: t1}, Filter(FirstN(r, 100), Price = 100))", 1, 90, true, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r: t1}, Filter(FirstN(r, 100), Price = 100))", 1, 91, true, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r: t1}, Filter(FirstN(r, 100), Price = 100))", 1, 92, false, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData("With({r : t1}, Filter(r, Price < 120))", 3, 93, false, false)]
        [InlineData("With({r : t1}, Filter(r, Price < 120))", 3, 94, true, true)]
        [InlineData("With({r : t1}, Filter(r, Price < 120))", 3, 95, true, false)]
        [InlineData("With({r : t1}, Filter(r, Price < 120))", 3, 96, false, true)]

        // Comparing fields can't be delegated.
        [InlineData("Filter(t1, Price < Old_Price)", 2, 97, false, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(t1, Price < Old_Price)", 2, 98, true, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(t1, Price < Old_Price)", 2, 99, true, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(t1, Price < Old_Price)", 2, 100, false, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData("With({r: t1}, Filter(r, Price < Old_Price))", 2, 101, false, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r: t1}, Filter(r, Price < Old_Price))", 2, 102, true, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r: t1}, Filter(r, Price < Old_Price))", 2, 103, true, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r: t1}, Filter(r, Price < Old_Price))", 2, 104, false, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Not All binary op are supported.
        [InlineData("Filter(t1, \"row1\" in Name)", 1, 105, false, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(t1, \"row1\" in Name)", 1, 106, true, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(t1, \"row1\" in Name)", 1, 107, true, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(t1, \"row1\" in Name)", 1, 108, false, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData("With({r: t1}, Filter(r, \"row1\" in Name))", 1, 109, false, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r: t1}, Filter(r, \"row1\" in Name))", 1, 110, true, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r: t1}, Filter(r, \"row1\" in Name))", 1, 111, true, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r: t1}, Filter(r, \"row1\" in Name))", 1, 112, false, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Error handling
        [InlineData("Filter(t1, Price < 1/0)", -1, 113, false, false)]
        [InlineData("Filter(t1, Price < 1/0)", -1, 114, true, true)]
        [InlineData("Filter(t1, Price < 1/0)", -1, 115, true, false)]
        [InlineData("Filter(t1, Price < 1/0)", -1, 116, false, true)]

        // Blank handling
        [InlineData("Filter(t1, Price < Blank())", 1, 117, false, false)]
        [InlineData("Filter(t1, Price < Blank())", 1, 118, true, true)]
        [InlineData("Filter(t1, Price < Blank())", 1, 119, true, false)]
        [InlineData("Filter(t1, Price < Blank())", 1, 120, false, true)]

        [InlineData("Filter(t1, Price > Blank())", 2, 121, false, false)]
        [InlineData("Filter(t1, Price > Blank())", 2, 122, true, true)]
        [InlineData("Filter(t1, Price > Blank())", 2, 123, true, false)]
        [InlineData("Filter(t1, Price > Blank())", 2, 124, false, true)]

        [InlineData("Filter(t1, Price = Blank())", 0, 125, false, false)]
        [InlineData("Filter(t1, Price = Blank())", 0, 126, true, true)]
        [InlineData("Filter(t1, Price = Blank())", 0, 127, true, false)]
        [InlineData("Filter(t1, Price = Blank())", 0, 128, false, true)]

        [InlineData("Filter(t1, Price <> Blank())", 3, 129, false, false)]
        [InlineData("Filter(t1, Price <> Blank())", 3, 130, true, true)]
        [InlineData("Filter(t1, Price <> Blank())", 3, 131, true, false)]
        [InlineData("Filter(t1, Price <> Blank())", 3, 132, false, true)]

        [InlineData("Filter(t1, Rating <> 'Rating (Locals)'.Hot)", 1, 133, false, false)]
        [InlineData("Filter(t1, Rating <> 'Rating (Locals)'.Hot)", 1, 134, true, true)]
        [InlineData("Filter(t1, Rating <> 'Rating (Locals)'.Hot)", 1, 135, true, false)]
        [InlineData("Filter(t1, Rating <> 'Rating (Locals)'.Hot)", 1, 136, false, true)]

        [InlineData("Filter(t1, Rating = 'Rating (Locals)'.Hot)", 2, 137, false, false)]
        [InlineData("Filter(t1, Rating = 'Rating (Locals)'.Hot)", 2, 138, true, true)]
        [InlineData("Filter(t1, Rating = 'Rating (Locals)'.Hot)", 2, 139, true, false)]
        [InlineData("Filter(t1, Rating = 'Rating (Locals)'.Hot)", 2, 140, false, true)]

        [InlineData("Filter(t1, Currency > 0)", 1, 141, false, false)]
        [InlineData("Filter(t1, Currency > 0)", 1, 142, true, true)]
        [InlineData("Filter(t1, Currency > 0)", 1, 143, true, false)]
        [InlineData("Filter(t1, Currency > 0)", 1, 144, false, true)]

        [InlineData("With({r: t1}, Filter(r, Currency > 0))", 1, 145, false, false)]
        [InlineData("With({r: t1}, Filter(r, Currency > 0))", 1, 146, true, true)]
        [InlineData("With({r: t1}, Filter(r, Currency > 0))", 1, 147, true, false)]
        [InlineData("With({r: t1}, Filter(r, Currency > 0))", 1, 148, false, true)]

        [InlineData("Filter(t1, virtual.'Virtual Data' = 10)", 1, 149, false, false)]
        [InlineData("Filter(t1, virtual.'Virtual Data' = 10)", 1, 150, true, true)]
        [InlineData("Filter(t1, virtual.'Virtual Data' = 10)", 1, 151, true, false)]
        [InlineData("Filter(t1, virtual.'Virtual Data' = 10)", 1, 152, false, true)]

        [InlineData("Filter(t1, virtual.'Virtual Data' <> 10)", 2, 153, false, false)]
        [InlineData("Filter(t1, virtual.'Virtual Data' <> 10)", 2, 154, true, true)]
        [InlineData("Filter(t1, virtual.'Virtual Data' <> 10)", 2, 155, true, false)]
        [InlineData("Filter(t1, virtual.'Virtual Data' <> 10)", 2, 156, false, true)]

        [InlineData("Filter(t1, virtual.'Virtual Data' <> 10 And Price <> 10)", 1, 157, false, false)]
        [InlineData("Filter(t1, virtual.'Virtual Data' <> 10 And Price <> 10)", 1, 158, true, true)]
        [InlineData("Filter(t1, virtual.'Virtual Data' <> 10 And Price <> 10)", 1, 159, true, false)]
        [InlineData("Filter(t1, virtual.'Virtual Data' <> 10 And Price <> 10)", 1, 160, false, true)]

        [InlineData("Filter(t1, IsBlank(virtual.'Virtual Data'))", 2, 161, false, false)]
        [InlineData("Filter(t1, IsBlank(virtual.'Virtual Data'))", 2, 162, true, true)]
        [InlineData("Filter(t1, IsBlank(virtual.'Virtual Data'))", 2, 163, true, false)]
        [InlineData("Filter(t1, IsBlank(virtual.'Virtual Data'))", 2, 164, false, true)]

        [InlineData("Filter(t1, AsType(PolymorphicLookup, t2).Data = 200)", 1, 165, true, true,
            @"Warning 40-45: Delegation warning. The highlighted part of this formula might not work correctly with column ""AsType.data"" on large data sets.",
            @"Warning 46-47: Delegation warning. The ""Filter"" part of this formula might not work correctly on large data sets.")]
        [InlineData("Filter(t1, AsType(PolymorphicLookup, t2).Data = 200)", 1, 166, false, false,
            @"Warning 40-45: Delegation warning. The highlighted part of this formula might not work correctly with column ""AsType.data"" on large data sets.",
            @"Warning 46-47: Delegation warning. The ""Filter"" part of this formula might not work correctly on large data sets.")]
        [InlineData("Filter(t1, AsType(PolymorphicLookup, t2).Data = 200)", 1, 167, true, false,
            @"Warning 40-45: Delegation warning. The highlighted part of this formula might not work correctly with column ""AsType.data"" on large data sets.",
            @"Warning 46-47: Delegation warning. The ""Filter"" part of this formula might not work correctly on large data sets.")]
        [InlineData("Filter(t1, AsType(PolymorphicLookup, t2).Data = 200)", 1, 168, false, true,
            @"Warning 40-45: Delegation warning. The highlighted part of this formula might not work correctly with column ""AsType.data"" on large data sets.",
            @"Warning 46-47: Delegation warning. The ""Filter"" part of this formula might not work correctly on large data sets.")]
       
        [InlineData("Filter(t1, AsType(PolymorphicLookup, t2).Data <> 200)", 2, 169, true, true,
            @"Warning 40-45: Delegation warning. The highlighted part of this formula might not work correctly with column ""AsType.data"" on large data sets.",
            @"Warning 46-48: Delegation warning. The ""Filter"" part of this formula might not work correctly on large data sets.")]
        [InlineData("Filter(t1, AsType(PolymorphicLookup, t2).Data <> 200)", 2, 170, false, false,
            @"Warning 40-45: Delegation warning. The highlighted part of this formula might not work correctly with column ""AsType.data"" on large data sets.",
            @"Warning 46-48: Delegation warning. The ""Filter"" part of this formula might not work correctly on large data sets.")]
        [InlineData("Filter(t1, AsType(PolymorphicLookup, t2).Data <> 200)", 2, 171, true, false,
            @"Warning 40-45: Delegation warning. The highlighted part of this formula might not work correctly with column ""AsType.data"" on large data sets.",
            @"Warning 46-48: Delegation warning. The ""Filter"" part of this formula might not work correctly on large data sets.")]
        [InlineData("Filter(t1, AsType(PolymorphicLookup, t2).Data <> 200)", 2, 172, false, true,
            @"Warning 40-45: Delegation warning. The highlighted part of this formula might not work correctly with column ""AsType.data"" on large data sets.",
            @"Warning 46-48: Delegation warning. The ""Filter"" part of this formula might not work correctly on large data sets.")]

        [InlineData("Filter(t1, PolymorphicLookup = First(t2))", 1, 173, true, true)]
        [InlineData("Filter(t1, PolymorphicLookup = First(t2))", 1, 174, false, false)]
        [InlineData("Filter(t1, PolymorphicLookup = First(t2))", 1, 175, true, false)]
        [InlineData("Filter(t1, PolymorphicLookup = First(t2))", 1, 176, false, true)]

        [InlineData("Filter(t1, PolymorphicLookup <> First(t2))", 2, 177, true, true)]
        [InlineData("Filter(t1, PolymorphicLookup <> First(t2))", 2, 178, false, false)]
        [InlineData("Filter(t1, PolymorphicLookup <> First(t2))", 2, 179, true, false)]
        [InlineData("Filter(t1, PolymorphicLookup <> First(t2))", 2, 180, false, true)]

        [InlineData("Filter(et, Field1 = 200)", 2, 182, true, true)]
        [InlineData("Filter(et, Field1 = 200)", 2, 183, false, false)]
        [InlineData("Filter(et, Field1 = 200)", 2, 184, true, false)]
        [InlineData("Filter(et, Field1 = 200)", 2, 185, false, true)]

        [InlineData("ShowColumns(Filter(et, Field1 = 200), Field1)", 2, 186, true, true)]
        [InlineData("ShowColumns(Filter(et, Field1 = 200), Field1)", 2, 187, false, false)]
        [InlineData("ShowColumns(Filter(et, Field1 = 200), Field1)", 2, 188, true, false)]
        [InlineData("ShowColumns(Filter(et, Field1 = 200), Field1)", 2, 189, false, true)]

        [InlineData("Filter(t1, State = 'State (Locals)'.Active)", 1, 190, true, true)]
        [InlineData("Filter(t1, State = 'State (Locals)'.Active)", 1, 191, false, false)]
        [InlineData("Filter(t1, State = 'State (Locals)'.Active)", 1, 192, true, false)]
        [InlineData("Filter(t1, State = 'State (Locals)'.Active)", 1, 193, false, true)]

        [InlineData("Filter(t1, State = If(1<0, 'State (Locals)'.Active))", 0, 194, true, true)]
        [InlineData("Filter(t1, State = If(1<0, 'State (Locals)'.Active))", 0, 195, false, false)]
        [InlineData("Filter(t1, State = If(1<0, 'State (Locals)'.Active))", 0, 196, true, false)]
        [InlineData("Filter(t1, State = If(1<0, 'State (Locals)'.Active))", 0, 197, false, true)]
        public async Task FilterDelegationAsync(string expr, int expectedRows, int id, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
        {
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "t2");
            map.Add("virtualremote", "t3");
            map.Add("elastictable", "et");
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: cdsNumberIsFloat, policy: policy);

            var opts = parserNumberIsFloatOption ?
                PluginExecutionTests._parserAllowSideEffects_NumberIsFloat :
                PluginExecutionTests._parserAllowSideEffects;

            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_count", FormulaValue.New(100m));

            var inputs = DelegationTestUtility.TransformForWithFunction(expr, expectedWarnings?.Count() ?? 0);

            for (var i = 0; i < inputs.Count(); i++)
            {
                expr = inputs[i];

                var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
                Assert.True(check.IsSuccess);

                var scam = check.ScanDependencies(dv.MetadataCache);

                // compare IR to verify the delegations are happening exactly where we expect
                var irNode = check.ApplyIR();
                var actualIr = check.GetCompactIRString();

                await DelegationTestUtility.CompareSnapShotAsync("FilterDelegation.txt", actualIr, id, i == 1);

                // Validate delegation warnings.
                // error.ToString() will capture warning status, message, and source span.
                var errors = check.ApplyErrors();

                var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

                Assert.Equal(expectedWarnings.Length, errorList.Length);
                for (var j = 0; j < errorList.Length; j++)
                {
                    Assert.Equal(expectedWarnings[j], errorList[j]);
                }

                var run = check.GetEvaluator();

                var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

                // To check error cases.
                if (expectedRows < 0)
                {
                    Assert.IsType<ErrorValue>(result);
                }
                else
                {
                    Assert.IsAssignableFrom<TableValue>(result);
                    Assert.Equal(expectedRows, ((TableValue)result).Rows.Count());
                }
            }
        }
    }
}
