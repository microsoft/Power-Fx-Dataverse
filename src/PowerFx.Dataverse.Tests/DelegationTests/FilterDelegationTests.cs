// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public partial class DelegationTests
    {
        // Table 't1' has
        // 1st item with
        // Price = 100, Old_Price = 200,  Date = Date(2023, 6, 1), DateTime = DateTime(2023, 6, 1, 12, 0, 0), Currency = 100
        // 2nd item with
        // Price = 10
        // 3rd item with
        // Price = -10

        [Theory]
        [TestPriority(1)]

        //Basic case
        [InlineData(1, "Filter(t1, Price < 100)", 2, false, false)]
        [InlineData(2, "Filter(t1, Price < 100)", 2, true, true)]
        [InlineData(3, "Filter(t1, Price < 100)", 2, true, false)]
        [InlineData(4, "Filter(t1, Price < 100)", 2, false, true)]
        [InlineData(5, "Filter(t1, Price <= 100)", 3, false, false)]
        [InlineData(6, "Filter(t1, Price <= 100)", 3, true, true)]
        [InlineData(7, "Filter(t1, Price <= 100)", 3, true, false)]
        [InlineData(8, "Filter(t1, Price <= 100)", 3, false, true)]
        [InlineData(9, "Filter(t1, Price = 100)", 1, false, false)]
        [InlineData(10, "Filter(t1, Price = 100)", 1, true, true)]
        [InlineData(11, "Filter(t1, Price = 100)", 1, true, false)]
        [InlineData(12, "Filter(t1, Price = 100)", 1, false, true)]
        [InlineData(13, "Filter(t1, Price > 100)", 0, false, false)]
        [InlineData(14, "Filter(t1, Price > 100)", 0, true, true)]
        [InlineData(15, "Filter(t1, Price > 100)", 0, true, false)]
        [InlineData(16, "Filter(t1, Price > 100)", 0, false, true)]
        [InlineData(17, "Filter(t1, Price >= 100)", 1, false, false)]
        [InlineData(18, "Filter(t1, Price >= 100)", 1, true, true)]
        [InlineData(19, "Filter(t1, Price >= 100)", 1, true, false)]
        [InlineData(20, "Filter(t1, Price >= 100)", 1, false, true)]
        [InlineData(21, "Filter(t1, Price < Float(120))", 3, false, false)]
        [InlineData(22, "Filter(t1, Price < Float(120))", 3, true, true)]
        [InlineData(23, "Filter(t1, Price < Float(120))", 3, true, false)]
        [InlineData(24, "Filter(t1, Price < Float(120))", 3, false, true)]
        [InlineData(25, "Filter(t1, Price < Decimal(20))", 2, false, false)]
        [InlineData(26, "Filter(t1, Price < Decimal(20))", 2, true, true)]
        [InlineData(27, "Filter(t1, Price < Decimal(20))", 2, true, false)]
        [InlineData(28, "Filter(t1, Price < Decimal(20))", 2, false, true)]
        [InlineData(29, "Filter(t1, Price < Abs(-120))", 3, false, false)]
        [InlineData(30, "Filter(t1, Price < Abs(-120))", 3, true, true)]
        [InlineData(31, "Filter(t1, Price < Abs(-120))", 3, true, false)]
        [InlineData(32, "Filter(t1, Price < Abs(-120))", 3, false, true)]

        // These two tests have Coalesce for numeric literals where the NumberIsFloat version does not.
        // Although not wrong, they should be the same. Being tracked with https://github.com/microsoft/Power-Fx/issues/1609
        // Date
        [InlineData(33, "Filter(t1, Date = Date(2023, 6, 1))", 1, false, false)]
        [InlineData(34, "Filter(t1, Date = Date(2023, 6, 1))", 1, true, true)]
        [InlineData(35, "Filter(t1, Date = Date(2023, 6, 1))", 1, true, false)]
        [InlineData(36, "Filter(t1, Date = Date(2023, 6, 1))", 1, false, true)]

        // DateTime with coercion
        [InlineData(37, "Filter(t1, DateTime = Date(2023, 6, 1))", 0, false, false)]
        [InlineData(38, "Filter(t1, DateTime = Date(2023, 6, 1))", 0, true, true)]
        [InlineData(39, "Filter(t1, DateTime = Date(2023, 6, 1))", 0, true, false)]
        [InlineData(40, "Filter(t1, DateTime = Date(2023, 6, 1))", 0, false, true)]
        [InlineData(41, "With({r: t1}, Filter(r, DateTime = Date(2023, 6, 1)))", 0, false, false)]
        [InlineData(42, "With({r: t1}, Filter(r, DateTime = Date(2023, 6, 1)))", 0, true, true)]
        [InlineData(43, "With({r: t1}, Filter(r, DateTime = Date(2023, 6, 1)))", 0, true, false)]
        [InlineData(44, "With({r: t1}, Filter(r, DateTime = Date(2023, 6, 1)))", 0, false, true)]

        // Order doesn't matter
        [InlineData(45, "Filter(t1, 0 > Price)", 1, false, false)]
        [InlineData(46, "Filter(t1, 0 > Price)", 1, true, true)]
        [InlineData(47, "Filter(t1, 0 > Price)", 1, true, false)]
        [InlineData(48, "Filter(t1, 0 > Price)", 1, false, true)]

        // Variable as arg
        [InlineData(49, "Filter(t1, Price > _count)", 0, false, false)]
        [InlineData(50, "Filter(t1, Price > _count)", 0, true, true)]
        [InlineData(51, "Filter(t1, Price > _count)", 0, true, false)]
        [InlineData(52, "Filter(t1, Price > _count)", 0, false, true)]

        // Function as arg
        [InlineData(53, "Filter(t1, Price > If(1<0,_count, 1))", 2, false, false)]
        [InlineData(54, "Filter(t1, Price > If(1<0,_count, 1))", 2, true, true)]
        [InlineData(55, "Filter(t1, Price > If(1<0,_count, 1))", 2, true, false)]
        [InlineData(56, "Filter(t1, Price > If(1<0,_count, 1))", 2, false, true)]

        // Filter nested in another function both delegated.
        [InlineData(57, "Filter(Filter(t1, Price > 0), Price < 100)", 1, false, false)]
        [InlineData(58, "Filter(Filter(t1, Price > 0), Price < 100)", 1, true, true)]
        [InlineData(59, "Filter(Filter(t1, Price > 0), Price < 100)", 1, true, false)]
        [InlineData(60, "Filter(Filter(t1, Price > 0), Price < 100)", 1, false, true)]

        // Basic case with And
        [InlineData(61, "Filter(t1, Price < 120 And 90 < Price)", 1, false, false)]
        [InlineData(62, "Filter(t1, Price < 120 And 90 < Price)", 1, true, true)]
        [InlineData(63, "Filter(t1, Price < 120 And 90 < Price)", 1, true, false)]
        [InlineData(64, "Filter(t1, Price < 120 And 90 < Price)", 1, false, true)]

        // Basic case with Or
        [InlineData(65, "Filter(t1, Price < 0 Or Price > 90)", 2, false, false)]
        [InlineData(66, "Filter(t1, Price < 0 Or Price > 90)", 2, true, true)]
        [InlineData(67, "Filter(t1, Price < 0 Or Price > 90)", 2, true, false)]
        [InlineData(68, "Filter(t1, Price < 0 Or Price > 90)", 2, false, true)]

        // Delegation Not Allowed

        // predicate that uses function that is not delegable.
        [InlineData(69, "Filter(t1, IsBlank(Price))", 0, false, false)]
        [InlineData(70, "Filter(t1, IsBlank(Price))", 0, true, true)]
        [InlineData(71, "Filter(t1, IsBlank(Price))", 0, true, false)]
        [InlineData(72, "Filter(t1, IsBlank(Price))", 0, false, true)]
        [InlineData(73, "Filter(t1, Price < 200 And IsBlank(Old_Price))", 1, false, false)]
        [InlineData(74, "Filter(t1, Price < 200 And IsBlank(Old_Price))", 1, true, true)]
        [InlineData(75, "Filter(t1, Price < 200 And IsBlank(Old_Price))", 1, true, false)]
        [InlineData(76, "Filter(t1, Price < 200 And IsBlank(Old_Price))", 1, false, true)]

        // predicate that uses function that is not delegable.
        [InlineData(77, "Filter(t1, Price < 120 And IsBlank(_count))", 0, false, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(78, "Filter(t1, Price < 120 And IsBlank(_count))", 0, true, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(79, "Filter(t1, Price < 120 And IsBlank(_count))", 0, true, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(80, "Filter(t1, Price < 120 And IsBlank(_count))", 0, false, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(81, "With({r: t1}, Filter(r, Price < 120 And IsBlank(_count)))", 0, false, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(82, "With({r: t1}, Filter(r, Price < 120 And IsBlank(_count)))", 0, true, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(83, "With({r: t1}, Filter(r, Price < 120 And IsBlank(_count)))", 0, true, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(84, "With({r: t1}, Filter(r, Price < 120 And IsBlank(_count)))", 0, false, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Filter nested in FirstN function. Only FirstN is delegated.
        [InlineData(85, "Filter(FirstN(t1, 100), Price = 100)", 1, false, false, "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(86, "Filter(FirstN(t1, 100), Price = 100)", 1, true, true, "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(87, "Filter(FirstN(t1, 100), Price = 100)", 1, true, false, "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(88, "Filter(FirstN(t1, 100), Price = 100)", 1, false, true, "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(89, "With({r: t1}, Filter(FirstN(r, 100), Price = 100))", 1, false, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(90, "With({r: t1}, Filter(FirstN(r, 100), Price = 100))", 1, true, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(91, "With({r: t1}, Filter(FirstN(r, 100), Price = 100))", 1, true, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(92, "With({r: t1}, Filter(FirstN(r, 100), Price = 100))", 1, false, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(93, "With({r : t1}, Filter(r, Price < 120))", 3, false, false)]
        [InlineData(94, "With({r : t1}, Filter(r, Price < 120))", 3, true, true)]
        [InlineData(95, "With({r : t1}, Filter(r, Price < 120))", 3, true, false)]
        [InlineData(96, "With({r : t1}, Filter(r, Price < 120))", 3, false, true)]

        // Comparing fields can't be delegated.
        [InlineData(97, "Filter(t1, Price < Old_Price)", 2, false, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(98, "Filter(t1, Price < Old_Price)", 2, true, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(99, "Filter(t1, Price < Old_Price)", 2, true, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(100, "Filter(t1, Price < Old_Price)", 2, false, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(101, "With({r: t1}, Filter(r, Price < Old_Price))", 2, false, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(102, "With({r: t1}, Filter(r, Price < Old_Price))", 2, true, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(103, "With({r: t1}, Filter(r, Price < Old_Price))", 2, true, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(104, "With({r: t1}, Filter(r, Price < Old_Price))", 2, false, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]

        // 'in' op is supported.
        [InlineData(105, @"Filter(t1, ""oW1"" in Name)", 1, false, false)]
        [InlineData(106, @"Filter(t1, ""oW1"" in Name)", 1, true, true)]
        [InlineData(107, @"Filter(t1, ""oW1"" in Name)", 1, true, false)]
        [InlineData(108, @"Filter(t1, ""oW1"" in Name)", 1, false, true)]
        [InlineData(109, @"With({r: t1}, Filter(r, ""oW1"" in Name))", 1, false, false)]
        [InlineData(110, @"With({r: t1}, Filter(r, ""oW1"" in Name))", 1, true, true)]
        [InlineData(111, @"With({r: t1}, Filter(r, ""oW1"" in Name))", 1, true, false)]
        [InlineData(112, @"With({r: t1}, Filter(r, ""oW1"" in Name))", 1, false, true)]

        // Error handling
        [InlineData(113, "Filter(t1, Price < 1/0)", -1, false, false)]
        [InlineData(114, "Filter(t1, Price < 1/0)", -1, true, true)]
        [InlineData(115, "Filter(t1, Price < 1/0)", -1, true, false)]
        [InlineData(116, "Filter(t1, Price < 1/0)", -1, false, true)]

        // Blank handling
        [InlineData(117, "Filter(t1, Price < Blank())", 1, false, false)]
        [InlineData(118, "Filter(t1, Price < Blank())", 1, true, true)]
        [InlineData(119, "Filter(t1, Price < Blank())", 1, true, false)]
        [InlineData(120, "Filter(t1, Price < Blank())", 1, false, true)]
        [InlineData(121, "Filter(t1, Price > Blank())", 2, false, false)]
        [InlineData(122, "Filter(t1, Price > Blank())", 2, true, true)]
        [InlineData(123, "Filter(t1, Price > Blank())", 2, true, false)]
        [InlineData(124, "Filter(t1, Price > Blank())", 2, false, true)]
        [InlineData(125, "Filter(t1, Price = Blank())", 0, false, false)]
        [InlineData(126, "Filter(t1, Price = Blank())", 0, true, true)]
        [InlineData(127, "Filter(t1, Price = Blank())", 0, true, false)]
        [InlineData(128, "Filter(t1, Price = Blank())", 0, false, true)]
        [InlineData(129, "Filter(t1, Price <> Blank())", 3, false, false)]
        [InlineData(130, "Filter(t1, Price <> Blank())", 3, true, true)]
        [InlineData(131, "Filter(t1, Price <> Blank())", 3, true, false)]
        [InlineData(132, "Filter(t1, Price <> Blank())", 3, false, true)]
        [InlineData(133, "Filter(t1, Rating <> 'Rating (Locals)'.Hot)", 1, false, false)]
        [InlineData(134, "Filter(t1, Rating <> 'Rating (Locals)'.Hot)", 1, true, true)]
        [InlineData(135, "Filter(t1, Rating <> 'Rating (Locals)'.Hot)", 1, true, false)]
        [InlineData(136, "Filter(t1, Rating <> 'Rating (Locals)'.Hot)", 1, false, true)]
        [InlineData(137, "Filter(t1, Rating = 'Rating (Locals)'.Hot)", 2, false, false)]
        [InlineData(138, "Filter(t1, Rating = 'Rating (Locals)'.Hot)", 2, true, true)]
        [InlineData(139, "Filter(t1, Rating = 'Rating (Locals)'.Hot)", 2, true, false)]
        [InlineData(140, "Filter(t1, Rating = 'Rating (Locals)'.Hot)", 2, false, true)]
        [InlineData(141, "Filter(t1, Currency > 0)", 1, false, false)]
        [InlineData(142, "Filter(t1, Currency > 0)", 1, true, true)]
        [InlineData(143, "Filter(t1, Currency > 0)", 1, true, false)]
        [InlineData(144, "Filter(t1, Currency > 0)", 1, false, true)]
        [InlineData(145, "With({r: t1}, Filter(r, Currency > 0))", 1, false, false)]
        [InlineData(146, "With({r: t1}, Filter(r, Currency > 0))", 1, true, true)]
        [InlineData(147, "With({r: t1}, Filter(r, Currency > 0))", 1, true, false)]
        [InlineData(148, "With({r: t1}, Filter(r, Currency > 0))", 1, false, true)]
        [InlineData(149, "Filter(t1, virtual.'Virtual Data' = 10)", 1, false, false)]
        [InlineData(150, "Filter(t1, virtual.'Virtual Data' = 10)", 1, true, true)]
        [InlineData(151, "Filter(t1, virtual.'Virtual Data' = 10)", 1, true, false)]
        [InlineData(152, "Filter(t1, virtual.'Virtual Data' = 10)", 1, false, true)]
        [InlineData(153, "Filter(t1, virtual.'Virtual Data' <> 10)", 2, false, false)]
        [InlineData(154, "Filter(t1, virtual.'Virtual Data' <> 10)", 2, true, true)]
        [InlineData(155, "Filter(t1, virtual.'Virtual Data' <> 10)", 2, true, false)]
        [InlineData(156, "Filter(t1, virtual.'Virtual Data' <> 10)", 2, false, true)]
        [InlineData(157, "Filter(t1, virtual.'Virtual Data' <> 10 And Price <> 10)", 1, false, false)]
        [InlineData(158, "Filter(t1, virtual.'Virtual Data' <> 10 And Price <> 10)", 1, true, true)]
        [InlineData(159, "Filter(t1, virtual.'Virtual Data' <> 10 And Price <> 10)", 1, true, false)]
        [InlineData(160, "Filter(t1, virtual.'Virtual Data' <> 10 And Price <> 10)", 1, false, true)]
        [InlineData(161, "Filter(t1, IsBlank(virtual.'Virtual Data'))", 2, false, false)]
        [InlineData(162, "Filter(t1, IsBlank(virtual.'Virtual Data'))", 2, true, true)]
        [InlineData(163, "Filter(t1, IsBlank(virtual.'Virtual Data'))", 2, true, false)]
        [InlineData(164, "Filter(t1, IsBlank(virtual.'Virtual Data'))", 2, false, true)]
        [InlineData(165, "Filter(t1, AsType(PolymorphicLookup, t2).Data = 200)", 1, true, true)]
        [InlineData(166, "Filter(t1, AsType(PolymorphicLookup, t2).Data = 200)", 1, false, false)]
        [InlineData(167, "Filter(t1, AsType(PolymorphicLookup, t2).Data = 200)", 1, true, false)]
        [InlineData(168, "Filter(t1, AsType(PolymorphicLookup, t2).Data = 200)", 1, false, true)]
        [InlineData(169, "Filter(t1, AsType(PolymorphicLookup, t2).Data <> 200)", 2, true, true)]
        [InlineData(170, "Filter(t1, AsType(PolymorphicLookup, t2).Data <> 200)", 2, false, false)]
        [InlineData(171, "Filter(t1, AsType(PolymorphicLookup, t2).Data <> 200)", 2, true, false)]
        [InlineData(172, "Filter(t1, AsType(PolymorphicLookup, t2).Data <> 200)", 2, false, true)]
        [InlineData(173, "Filter(t1, PolymorphicLookup = First(t2))", 1, true, true)]
        [InlineData(174, "Filter(t1, PolymorphicLookup = First(t2))", 1, false, false)]
        [InlineData(175, "Filter(t1, PolymorphicLookup = First(t2))", 1, true, false)]
        [InlineData(176, "Filter(t1, PolymorphicLookup = First(t2))", 1, false, true)]
        [InlineData(177, "Filter(t1, PolymorphicLookup <> First(t2))", 2, true, true)]
        [InlineData(178, "Filter(t1, PolymorphicLookup <> First(t2))", 2, false, false)]
        [InlineData(179, "Filter(t1, PolymorphicLookup <> First(t2))", 2, true, false)]
        [InlineData(180, "Filter(t1, PolymorphicLookup <> First(t2))", 2, false, true)]
        [InlineData(182, "Filter(et, Field1 = 200)", 2, true, true)]
        [InlineData(183, "Filter(et, Field1 = 200)", 2, false, false)]
        [InlineData(184, "Filter(et, Field1 = 200)", 2, true, false)]
        [InlineData(185, "Filter(et, Field1 = 200)", 2, false, true)]
        [InlineData(186, "ShowColumns(Filter(et, Field1 = 200), Field1)", 2, true, true)]
        [InlineData(187, "ShowColumns(Filter(et, Field1 = 200), Field1)", 2, false, false)]
        [InlineData(188, "ShowColumns(Filter(et, Field1 = 200), Field1)", 2, true, false)]
        [InlineData(189, "ShowColumns(Filter(et, Field1 = 200), Field1)", 2, false, true)]
        [InlineData(190, "Filter(t1, State = 'State (Locals)'.Active)", 1, true, true)]
        [InlineData(191, "Filter(t1, State = 'State (Locals)'.Active)", 1, false, false)]
        [InlineData(192, "Filter(t1, State = 'State (Locals)'.Active)", 1, true, false)]
        [InlineData(193, "Filter(t1, State = 'State (Locals)'.Active)", 1, false, true)]
        [InlineData(194, "Filter(t1, State = If(1<0, 'State (Locals)'.Active))", 0, true, true)]
        [InlineData(195, "Filter(t1, State = If(1<0, 'State (Locals)'.Active))", 0, false, false)]
        [InlineData(196, "Filter(t1, State = If(1<0, 'State (Locals)'.Active))", 0, true, false)]
        [InlineData(197, "Filter(t1, State = If(1<0, 'State (Locals)'.Active))", 0, false, true)]
        [InlineData(198, "Filter(Distinct(t1, Price), Value > 5)", 2, false, true)]
        [InlineData(199, "Filter(Sort(t1, Price), Price > 5)", 2, false, true)]
        [InlineData(200, "Filter(SortByColumns(t1, Price), Price > 5)", 2, false, true)]
        [InlineData(201, "Filter(ForAll(t1, Price), Value > 5)", 2, false, true)]
        [InlineData(202, "Filter(ForAll(t1, { Xyz: Price }), Xyz > 5)", 2, false, true)]
        [InlineData(203, "Distinct(Filter(Distinct(t1, Price), Value > 5), Value)", 2, false, true)]
        [InlineData(204, "Distinct(Filter(Sort(t1, Price), Price > 5), Price)", 2, false, true)]
        [InlineData(205, "Distinct(Filter(SortByColumns(t1, Price), Price > 5), Price)", 2, false, true)]
        [InlineData(206, "Distinct(Filter(ForAll(t1, Price), Value > 5), Value)", 2, false, true)]
        [InlineData(207, "Distinct(Filter(ForAll(t1, { Xyz: Price }), Xyz > 5), Xyz)", 2, false, true)]

        // 'exactin' op is not supported.
        [InlineData(208, @"Filter(t1, ""oW1"" exactin Name)", 0, false, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(209, @"Filter(t1, ""oW1"" exactin Name)", 0, true, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(210, @"Filter(t1, ""oW1"" exactin Name)", 0, true, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(211, @"Filter(t1, ""oW1"" exactin Name)", 0, false, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(212, @"With({r: t1}, Filter(r, ""oW1"" exactin Name))", 0, false, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(213, @"With({r: t1}, Filter(r, ""oW1"" exactin Name))", 0, true, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(214, @"With({r: t1}, Filter(r, ""oW1"" exactin Name))", 0, true, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(215, @"With({r: t1}, Filter(r, ""oW1"" exactin Name))", 0, false, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData(216, @"Filter(t1, ""o"" & ""W1"" in Name)", 1, false, false)]
        [InlineData(217, @"Filter(t1, ""o"" & ""W1"" in Name)", 1, true, true)]
        [InlineData(218, @"Filter(t1, ""o"" & ""W1"" in Name)", 1, true, false)]
        [InlineData(219, @"Filter(t1, ""o"" & ""W1"" in Name)", 1, false, true)]
        [InlineData(220, @"Filter(t1, 1 in Name)", 2, false, false)]
        [InlineData(221, @"Filter(t1, 1 in Name)", 2, true, true)]
        [InlineData(222, @"Filter(t1, 1 in Name)", 2, true, false)]
        [InlineData(223, @"Filter(t1, 1 in Name)", 2, false, true)]
        [InlineData(224, @"Filter(t1, Name in ""oW1"")", 0, false, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(225, @"Filter(t1, Name in ""oW1"")", 0, true, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(226, @"Filter(t1, Name in ""oW1"")", 0, true, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(227, @"Filter(t1, Name in ""oW1"")", 0, false, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(228, @"Filter(t1, Name in [""oW1"", ""oW2""])", 0, false, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(229, @"Filter(t1, ""1"" in Price)", 3, false, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(230, @"Filter(t1, ""1"" in Price)", 3, true, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(231, @"Filter(t1, ""1"" in Price)", 3, true, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(232, @"Filter(t1, ""1"" in Price)", 3, false, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        
        [InlineData(233, @"Filter(t1, Not(IsBlank(Price)))", 3, false, true)]
        [InlineData(234, @"Filter(t1, Not(IsBlank(Price)))", 3, false, true)]
        [InlineData(235, @"Filter(t1, Not(IsBlank(Price)))", 3, false, true)]
        [InlineData(236, @"Filter(t1, Not(IsBlank(Price)))", 3, false, true)]

        [InlineData(237, @"Filter(t1, ThisRecord.virtual.virtualremoteid = GUID(""00000000-0000-0000-0000-000000000006""))", 1, false, false)]
        [InlineData(238, @"Filter(t1, ThisRecord.virtual.virtualremoteid = GUID(""00000000-0000-0000-0000-000000000006""))", 1, true, true)]
        [InlineData(239, @"Filter(t1, ThisRecord.virtual.virtualremoteid = GUID(""00000000-0000-0000-0000-000000000006""))", 1, true, false)]
        [InlineData(240, @"Filter(t1, ThisRecord.virtual.virtualremoteid = GUID(""00000000-0000-0000-0000-000000000006""))", 1, false, true)]

        [InlineData(241, "Filter(t1, Price < 200 And Not(IsBlank(Old_Price)))", 1, false, false)]
        [InlineData(242, "Filter(t1, Price < 200 And Not(IsBlank(Old_Price)))", 1, true, true)]
        [InlineData(243, "Filter(t1, Price < 200 And Not(IsBlank(Old_Price)))", 1, true, false)]
        [InlineData(244, "Filter(t1, Price < 200 And Not(IsBlank(Old_Price)))", 1, false, true)]

        // predicate that uses function that is not delegable.
        [InlineData(245, "Filter(t1, Price < 120 And Not(IsBlank(_count)))", 3, false, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(246, "Filter(t1, Price < 120 And Not(IsBlank(_count)))", 3, true, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(247, "Filter(t1, Price < 120 And Not(IsBlank(_count)))", 3, true, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(248, "Filter(t1, Price < 120 And Not(IsBlank(_count)))", 3, false, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData(249, "Filter(t1, Price < 200 And !IsBlank(Old_Price))", 1, false, false)]
        [InlineData(250, "Filter(t1, Price < 200 And !IsBlank(Old_Price))", 1, true, true)]
        [InlineData(251, "Filter(t1, Price < 200 And !IsBlank(Old_Price))", 1, true, false)]
        [InlineData(252, "Filter(t1, Price < 200 And !IsBlank(Old_Price))", 1, false, true)]

        [InlineData(253, "Filter(t1, StartsWith(ThisRecord.Name, \"r\"))", 2, false, false)]
        [InlineData(254, "Filter(t1, StartsWith(ThisRecord.Name, \"r\"))", 2, true, true)]
        [InlineData(255, "Filter(t1, StartsWith(ThisRecord.Name, \"r\"))", 2, true, false)]
        [InlineData(256, "Filter(t1, StartsWith(ThisRecord.Name, \"r\"))", 2, false, true)]

        [InlineData(257, "Filter(t1, EndsWith(ThisRecord.Name, \"1\"))", 2, false, false)]
        [InlineData(258, "Filter(t1, EndsWith(ThisRecord.Name, \"1\"))", 2, true, true)]
        [InlineData(259, "Filter(t1, EndsWith(ThisRecord.Name, \"1\"))", 2, true, false)]
        [InlineData(260, "Filter(t1, EndsWith(ThisRecord.Name, \"1\"))", 2, false, true)]

        [InlineData(261, "Filter(t1, StartsWith(ThisRecord.Name, \"r\") And EndsWith(ThisRecord.Name, \"1\"))", 1, false, false)]
        [InlineData(262, "Filter(t1, StartsWith(ThisRecord.Name, \"r\") And EndsWith(ThisRecord.Name, \"1\"))", 1, true, true)]
        [InlineData(263, "Filter(t1, StartsWith(ThisRecord.Name, \"r\") And EndsWith(ThisRecord.Name, \"1\"))", 1, true, false)]
        [InlineData(264, "Filter(t1, StartsWith(ThisRecord.Name, \"r\") And EndsWith(ThisRecord.Name, \"1\"))", 1, false, true)]

        [InlineData(265, "Filter(t1, StartsWith(\"r\", ThisRecord.Name))", 0, false, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(266, "Filter(t1, StartsWith(\"r\", ThisRecord.Name))", 0, true, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(267, "Filter(t1, StartsWith(\"r\", ThisRecord.Name))", 0, true, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(268, "Filter(t1, StartsWith(\"r\", ThisRecord.Name))", 0, false, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData(269, "Filter(t1, EndsWith(\"1\", ThisRecord.Name))", 0, false, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(270, "Filter(t1, EndsWith(\"1\", ThisRecord.Name))", 0, true, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(271, "Filter(t1, EndsWith(\"1\", ThisRecord.Name))", 0, true, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(272, "Filter(t1, EndsWith(\"1\", ThisRecord.Name))", 0, false, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData(273, "Filter(t1, new_datetime > 0)", 1, false, false)]
        [InlineData(274, "Filter(t1, Hour(Date) = 2)", 0, false, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        public async Task FilterDelegationAsync(int id, string expr, int expectedRows, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
        {
            await DelegationTestAsync(id, "FilterDelegation.txt", expr, expectedRows, null, null, cdsNumberIsFloat, parserNumberIsFloatOption, null, false, true, true, expectedWarnings);
        }

        [Fact]
        public void Delegation_DateTest()
        {
            SymbolTable st = new SymbolTable() { DebugName = "Delegable_1" }; // Hack on DebugName to make delegation work
            RecordType rt = RecordType.Empty().Add("Date", FormulaType.Date);
            st.AddVariable("MyTable", new TestTableValue("MyTable", rt, null, new List<DelegationOperator>() { DelegationOperator.Eq, DelegationOperator.Lt, DelegationOperator.Le }).Type);

            Engine engine = new Engine(new PowerFxConfig());
            engine.EnableDelegation();

            CheckResult checkResult = engine.Check("Filter(MyTable, ThisRecord.Date < DateAdd(Now(), 30, TimeUnit.Days))", new ParserOptions() { AllowsSideEffects = true }, st);

            Assert.Empty(checkResult.Errors);
            var actualIr = checkResult.GetCompactIRString();

            Assert.Equal<object>("__retrieveMultiple(MyTable, __lt(MyTable, {fieldFunctions:Table(), fieldName:Date}, DateAdd(Now(), Float(30), (TimeUnit).Days)), __noop(), {}, 1000, )", actualIr);
        }
    }
}
