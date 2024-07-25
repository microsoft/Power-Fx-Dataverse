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
        // Although not wrong, they should be the same.  Being tracked with https://github.com/microsoft/Power-Fx/issues/1609
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

        //Order doesn't matter
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


        // Not All binary op are supported.
        [InlineData(105, "Filter(t1, \"row1\" in Name)", 1, false, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(106, "Filter(t1, \"row1\" in Name)", 1, true, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(107, "Filter(t1, \"row1\" in Name)", 1, true, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(108, "Filter(t1, \"row1\" in Name)", 1, false, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData(109, "With({r: t1}, Filter(r, \"row1\" in Name))", 1, false, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(110, "With({r: t1}, Filter(r, \"row1\" in Name))", 1, true, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(111, "With({r: t1}, Filter(r, \"row1\" in Name))", 1, true, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(112, "With({r: t1}, Filter(r, \"row1\" in Name))", 1, false, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]

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
        public async Task FilterDelegationAsync(int id, string expr, int expectedRows, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
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

            var inputs = DelegationTestUtility.TransformForWithFunction(expr, expectedWarnings?.Length ?? 0);

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

                var result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

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

        [Fact]
        public void Delegation_DateTest()
        {
            SymbolTable st = new SymbolTable() { DebugName = "Delegable_1" }; // Hack on DebugName to make delegation work
            st.AddVariable("MyTable", TableType.Empty().Add("Date", FormulaType.Date));
            Engine engine = new Engine(new PowerFxConfig());
            engine.EnableDelegation();

            CheckResult checkResult = engine.Check("Filter(MyTable, ThisRecord.Date < DateAdd(Now(), 30, TimeUnit.Days))", new ParserOptions() { AllowsSideEffects = true }, st);

            Assert.Empty(checkResult.Errors);
            var actualIr = checkResult.GetCompactIRString();

            Assert.Equal<object>("__retrieveMultiple(MyTable, __lt(MyTable, Date, DateAdd(Now(), Float(30), (TimeUnit).Days)), __noop(), 1000, )", actualIr);
        }
    }
}
