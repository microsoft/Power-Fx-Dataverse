// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.PowerFx.Types;
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
        [InlineData(1, "LookUp(t1, Price = 255).Price", null, true, true)]
        [InlineData(2, "LookUp(t1, Price = 255).Price", null, false, false)]
        [InlineData(3, "LookUp(t1, Price = 255).Price", null, true, false)]
        [InlineData(4, "LookUp(t1, Price = 255).Price", null, false, true)]
        [InlineData(5, "LookUp(t1, IsBlank(Price)).Price", null, true, true)]

        // can't delegate IsBlank, because inside is non delegable.
        [InlineData(6, "LookUp(t1, IsBlank(Price * Price)).Price", null, true, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(7, "LookUp(t1, IsBlank(LookUp(t1, IsBlank(Price)))).Price", 100.0, true, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(8, "LookUp(t1, Integer = 255).Price", null, true, true)]
        [InlineData(9, "LookUp(t1, Integer = 255).Price", null, false, false)]
        [InlineData(10, "LookUp(t1, Integer = 255).Price", null, true, false)]
        [InlineData(11, "LookUp(t1, Integer = 255).Price", null, false, true)]
        [InlineData(12, "LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\")).Price", 100.0, true, true)]

        //Basic case with And and Or
        [InlineData(13, "LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\") And Price > 0).Price", 100.0, true, true)]
        [InlineData(14, "LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\") And Price > 0).Price", 100.0, false, false)]
        [InlineData(15, "LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\") And Price > 0).Price", 100.0, true, false)]
        [InlineData(16, "LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\") And Price > 0).Price", 100.0, false, false)]
        [InlineData(17, "LookUp(t1, LocalId=GUID(\"00000000-0000-0000-0000-000000000001\") Or Price > 0).Price", 100.0, true, true)]
        [InlineData(18, "LookUp(t1, LocalId=GUID(\"00000000-0000-0000-0000-000000000001\") Or Price > 0).Price", 100.0, false, false)]
        [InlineData(19, "LookUp(t1, LocalId=GUID(\"00000000-0000-0000-0000-000000000001\") Or Price > 0).Price", 100.0, true, false)]
        [InlineData(20, "LookUp(t1, LocalId=GUID(\"00000000-0000-0000-0000-000000000001\") Or Price > 0).Price", 100.0, false, true)]

        // variable
        [InlineData(21, "LookUp(t1, LocalId=_g1).Price", 100.0, true, true)]

        // Date
        [InlineData(22, "LookUp(t1, Date = Date(2023, 6, 1)).Price", 100.0, true, true)]

        // These three tests have Coalesce for numeric literals where the NumberIsFloat version does not.
        // Although not wrong, they should be the same.  Being tracked with https://github.com/microsoft/Power-Fx/issues/1609
        [InlineData(23, "LookUp(t1, Date = Date(2023, 6, 1)).Price", 100.0, false, false)]

        // DateTime with coercion
        [InlineData(24, "LookUp(t1, DateTime = Date(2023, 6, 1)).Price", null, true, true)]
        [InlineData(25, "LookUp(t1, DateTime = DateTime(2023, 6, 1, 12, 0, 0)).Price", 100.0, true, true)]
        [InlineData(26, "LookUp(t1, DateTime = DateTime(2023, 6, 1, 12, 0, 0)).Price", 100.0, false, false)]

        // reversed order still ok
        [InlineData(27, "LookUp(t1, _g1 = LocalId).Price", 100.0, true, true)]

        // explicit ThisRecord is ok. IR will handle.
        [InlineData(28, "LookUp(t1, ThisRecord.LocalId=_g1).Price", 100.0, true, true)]

        // Alias is ok. IR will handle.
        [InlineData(29, "LookUp(t1 As XYZ, XYZ.LocalId=_g1).Price", 100.0, true, true)]

        // lambda uses ThisRecord.Price, can't delegate
        [InlineData(30, "LookUp(t1, LocalId=If(Price>50, _g1, _gMissing)).Price", 100.0, true, true, "Warning 22-27: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData(31, "LookUp(t1, LocalId=If(Price>50, _g1, _gMissing)).Price", 100.0, false, false, "Warning 22-27: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData(32, "LookUp(t1, LocalId=If(Price>50, _g1, _gMissing)).Price", 100.0, true, false, "Warning 22-27: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData(33, "LookUp(t1, LocalId=If(Price>50, _g1, _gMissing)).Price", 100.0, false, true, "Warning 22-27: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData(34, "With({r: t1}, LookUp(r, LocalId=If(Price>50, _g1, _gMissing)).Price)", 100.0, true, true, "Warning 35-40: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData(35, "With({r: t1}, LookUp(r, LocalId=If(Price>50, _g1, _gMissing)).Price)", 100.0, false, false, "Warning 35-40: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData(36, "With({r: t1}, LookUp(r, LocalId=If(Price>50, _g1, _gMissing)).Price)", 100.0, true, false, "Warning 35-40: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData(37, "With({r: t1}, LookUp(r, LocalId=If(Price>50, _g1, _gMissing)).Price)", 100.0, false, true, "Warning 35-40: Can't delegate LookUp: Expression compares multiple fields.")]

        // On non primary field.
        [InlineData(38, "LookUp(t1, Price > 50).Price", 100.0, true, true)]
        [InlineData(39, "LookUp(t1, Price > 50).Price", 100.0, false, false)]
        [InlineData(40, "LookUp(t1, Price > 50).Price", 100.0, true, false)]
        [InlineData(41, "LookUp(t1, Price > 50).Price", 100.0, false, true)]

        // successful with complex expression
        [InlineData(42, "LookUp(t1, LocalId=If(true, _g1, _gMissing)).Price", 100.0, true, true)]

        // nested delegation, both delegated.
        [InlineData(43, "LookUp(t1, LocalId=LookUp(t1, LocalId=_g1).LocalId).Price", 100.0, true, true)]

        // Can't delegate if Table Arg is delegated.
        [InlineData(44, "LookUp(Filter(t1, Price = 100), localid=_g1).Price", 100.0, true, true, "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(45, "LookUp(Filter(t1, Price = 100), localid=_g1).Price", 100.0, false, false, "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(46, "LookUp(Filter(t1, Price = 100), localid=_g1).Price", 100.0, true, false, "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(47, "LookUp(Filter(t1, Price = 100), localid=_g1).Price", 100.0, false, true, "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(48, "With({r: t1}, LookUp(Filter(r, Price = 100), localid=_g1).Price)", 100.0, true, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(49, "With({r: t1}, LookUp(Filter(r, Price = 100), localid=_g1).Price)", 100.0, false, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(50, "With({r: t1}, LookUp(Filter(r, Price = 100), localid=_g1).Price)", 100.0, true, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(51, "With({r: t1}, LookUp(Filter(r, Price = 100), localid=_g1).Price)", 100.0, false, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Can't delegate if Table Arg is delegated.
        [InlineData(52, "LookUp(FirstN(t1, 1), localid=_g1).Price", 100.0, true, true, "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(53, "With({r: t1}, LookUp(FirstN(r, 1), localid=_g1).Price)", 100.0, true, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Can Delegate on non primary-key field.
        [InlineData(54, "LookUp(t1, LocalId=LookUp(t1, ThisRecord.Price>50).LocalId).Price", 100.0, true, true)]
        [InlineData(55, "LookUp(t1, LocalId=LookUp(t1, ThisRecord.Price>50).LocalId).Price", 100.0, false, false)]
        [InlineData(56, "LookUp(t1, LocalId=LookUp(t1, ThisRecord.Price>50).LocalId).Price", 100.0, true, false)]
        [InlineData(57, "LookUp(t1, LocalId=LookUp(t1, ThisRecord.Price>50).LocalId).Price", 100.0, false, true)]
        [InlineData(58, "LookUp(t1, LocalId=First([_g1,_gMissing]).Value).Price", 100.0, true, true)]

        // unsupported function, can't yet delegate
        [InlineData(59, "Last(t1).Price", -10.0, true, true, "Warning 5-7: This operation on table 'local' may not work if it has more than 999 rows.")]

        // unsupported function, can't yet delegate
        [InlineData(60, "CountRows(t1)", 3.0, true, true, "Warning 10-12: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Functions like IsBlank, Collect,Patch, shouldn't require delegation. Ensure no warnings.
        [InlineData(61, "IsBlank(t1)", false, true, true)]
        [InlineData(62, "IsBlank(Filter(t1, 1=1))", false, true, true, "Warning 15-17: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(63, "IsBlank(Filter(t1, 1=1))", false, false, false, "Warning 15-17: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(64, "IsBlank(Filter(t1, 1=1))", false, true, false, "Warning 15-17: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(65, "IsBlank(Filter(t1, 1=1))", false, false, true, "Warning 15-17: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Collect shouldn't give warnings.
        [InlineData(66, "Collect(t1, { Price : 200}).Price", 200.0, true, true)]
        [InlineData(67, "Collect(t1, { Price : 200}).Price", 200.0, false, false)]
        [InlineData(68, "Collect(t1, { Price : 200}).Price", 200.0, true, false)]
        [InlineData(69, "Collect(t1, { Price : 200}).Price", 200.0, false, true)]

        // $$$ Confirm is NotFound Error or Blank? // delegated, but not found is Error
        [InlineData(70, "IsError(LookUp(t1, LocalId=If(false, _g1, _gMissing)))", true, true, true)]

        // $$$ Does using fakeT1, same as t1, cause warnings since it's not delegated?
        [InlineData(71, "LookUp(fakeT1, LocalId=_g1).Price", 100.0, true, true)]
        [InlineData(72, "LookUp(t1, LocalId=LocalId).Price", 100.0, true, true, "Warning 18-19: This predicate will always be true. Did you mean to use ThisRecord or [@ ]?", "Warning 19-26: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData(73, "With({r: t1}, LookUp(r, LocalId=LocalId).Price)", 100.0, true, true, "Warning 31-32: This predicate will always be true. Did you mean to use ThisRecord or [@ ]?", "Warning 32-39: Can't delegate LookUp: Expression compares multiple fields.")]

        // Error Handling
        [InlineData(74, "LookUp(t1, Price = If(1/0, 255)).Price", typeof(ErrorValue), true, true)]
        [InlineData(75, "LookUp(t1, Price = If(1/0, 255)).Price", typeof(ErrorValue), false, false)]
        [InlineData(76, "LookUp(t1, Price = If(1/0, 255)).Price", typeof(ErrorValue), true, false)]
        [InlineData(77, "LookUp(t1, Price = If(1/0, 255)).Price", typeof(ErrorValue), false, true)]

        // Blank Handling
        [InlineData(78, "LookUp(t1, Price = Blank()).Price", null, true, true)]
        [InlineData(79, "LookUp(t1, Price = Blank()).Price", null, false, false)]
        [InlineData(80, "LookUp(t1, Price = Blank()).Price", null, true, false)]
        [InlineData(81, "LookUp(t1, Price = Blank()).Price", null, false, true)]
        [InlineData(82, "LookUp(t1, Price <> Blank()).Price", 100.0, true, true)]
        [InlineData(83, "LookUp(t1, Price <> Blank()).Price", 100.0, false, false)]
        [InlineData(84, "LookUp(t1, Price <> Blank()).Price", 100.0, true, false)]
        [InlineData(85, "LookUp(t1, Price <> Blank()).Price", 100.0, false, true)]
        [InlineData(86, "LookUp(t1, Price < Blank()).Price", -10.0, true, true)]
        [InlineData(87, "LookUp(t1, Price < Blank()).Price", -10.0, false, false)]
        [InlineData(88, "LookUp(t1, Price < Blank()).Price", -10.0, true, false)]
        [InlineData(89, "LookUp(t1, Price < Blank()).Price", -10.0, false, true)]
        [InlineData(90, "LookUp(t1, Currency > 0).Price", 100.0, true, true)]
        [InlineData(91, "LookUp(t1, Currency > 0).Price", 100.0, false, false)]
        [InlineData(92, "LookUp(t1, Currency > 0).Price", 100.0, true, false)]
        [InlineData(93, "LookUp(t1, Currency > 0).Price", 100.0, false, true)]
        [InlineData(94, "With({r: t1}, LookUp(r, Currency > 0).Price)", 100.0, true, true)]
        [InlineData(95, "With({r: t1}, LookUp(r, Currency > 0).Price)", 100.0, false, false)]
        [InlineData(96, "With({r: t1}, LookUp(r, Currency > 0).Price)", 100.0, true, false)]
        [InlineData(97, "With({r: t1}, LookUp(r, Currency > 0).Price)", 100.0, false, true)]
        [InlineData(98, "With({r : t1}, LookUp(r, LocalId=_g1).Price)", 100.0, true, true)]
        [InlineData(99, "With({r : Filter(t1, Price < 120)}, LookUp(r, Price > 90).Price)", 100.0, false, false, "Warning 17-19: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(100, "With({r: t1} ,LookUp(r, localid=GUID(\"00000000-0000-0000-0000-000000000001\")).Price)", 100.0, true, true)]
        [InlineData(101, "LookUp(t1, virtual.'Virtual Data' = 10).Price", 100.0, true, true)]
        [InlineData(102, "LookUp(t1, virtual.'Virtual Data' = 10).Price", 100.0, false, false)]
        [InlineData(103, "LookUp(t1, virtual.'Virtual Data' = 10).Price", 100.0, true, false)]
        [InlineData(104, "LookUp(t1, virtual.'Virtual Data' = 10).Price", 100.0, false, true)]
        [InlineData(105, "LookUp(t1, virtual.'Virtual Data' <> 10).Price", 10.0, true, true)]
        [InlineData(106, "LookUp(t1, virtual.'Virtual Data' <> 10).Price", 10.0, false, false)]
        [InlineData(107, "LookUp(t1, virtual.'Virtual Data' <> 10).Price", 10.0, true, false)]
        [InlineData(108, "LookUp(t1, virtual.'Virtual Data' <> 10).Price", 10.0, false, true)]
        [InlineData(109, "LookUp(t1, virtual.'Virtual Data' <> 10 And Price <> 10).Price", -10.0, true, true)]
        [InlineData(110, "LookUp(t1, virtual.'Virtual Data' <> 10 And Price <> 10).Price", -10.0, false, false)]
        [InlineData(111, "LookUp(t1, virtual.'Virtual Data' <> 10 And Price <> 10).Price", -10.0, true, false)]
        [InlineData(112, "LookUp(t1, virtual.'Virtual Data' <> 10 And Price <> 10).Price", -10.0, false, true)]
        [InlineData(113, "LookUp(t1, IsBlank(virtual.'Virtual Data')).Price", 10.0, true, true)]
        [InlineData(114, "LookUp(t1, AsType(PolymorphicLookup, t2).Data = 200).Price", 100.0, true, true)]
        [InlineData(115, "LookUp(t1, AsType(PolymorphicLookup, t2).Data = 200).Price", 100.0, false, false)]
        [InlineData(116, "LookUp(t1, AsType(PolymorphicLookup, t2).Data = 200).Price", 100.0, true, false)]
        [InlineData(117, "LookUp(t1, AsType(PolymorphicLookup, t2).Data = 200).Price", 100.0, false, true)]
        [InlineData(118, "LookUp(t1, PolymorphicLookup = First(t2)).Price", 100.0, true, true)]
        [InlineData(119, "LookUp(t1, PolymorphicLookup = First(t2)).Price", 100.0, false, false)]
        [InlineData(120, "LookUp(t1, PolymorphicLookup = First(t2)).Price", 100.0, true, false)]
        [InlineData(121, "LookUp(t1, PolymorphicLookup = First(t2)).Price", 100.0, false, true)]
        [InlineData(122, "LookUp(t1, PolymorphicLookup <> First(t2)).Price", 10.0, true, true)]
        [InlineData(123, "LookUp(t1, PolymorphicLookup <> First(t2)).Price", 10.0, false, false)]
        [InlineData(124, "LookUp(t1, PolymorphicLookup <> First(t2)).Price", 10.0, true, false)]
        [InlineData(125, "LookUp(t1, PolymorphicLookup <> First(t2)).Price", 10.0, false, true)]
        [InlineData(126, "LookUp(Distinct(t1, new_quantity), Value < 20).Value", 10.0, true, true)]
        [InlineData(127, "LookUp(Distinct(t1, new_quantity), Value < 20).Value", 10.0, false, false)]
        [InlineData(128, "LookUp(Distinct(t1, new_quantity), Value < 20).Value", 10.0, true, false)]
        [InlineData(129, "LookUp(Distinct(t1, new_quantity), Value < 20).Value", 10.0, false, true)]

        // Blank handling for retrieveGUID.
        [InlineData(130, "LookUp(t1, localid= If(1<0, GUID(\"00000000-0000-0000-0000-000000000001\"))).Price", null, true, true)]

        // Error Handling.
        [InlineData(131, "LookUp(t1, localid= If(1/0, GUID(\"00000000-0000-0000-0000-000000000001\"))).Price", typeof(ErrorValue), true, true)]
        [InlineData(132, "LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, true, true)]
        [InlineData(133, "LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, false, false)]
        [InlineData(134, "LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, true, false)]
        [InlineData(135, "LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, false, true)]
        [InlineData(136, "LookUp(et, etid = GUID(\"00000000-0000-0000-0000-000000000007\") And 'Partition Id' = \"p1\").Field1", 200.0, true, true)]
        [InlineData(137, "LookUp(et, etid = GUID(\"00000000-0000-0000-0000-000000000007\") And 'Partition Id' = \"p1\").Field1", 200.0, false, false)]
        [InlineData(138, "LookUp(et, etid = GUID(\"00000000-0000-0000-0000-000000000007\") And 'Partition Id' = \"p1\").Field1", 200.0, true, false)]
        [InlineData(139, "LookUp(et, etid = GUID(\"00000000-0000-0000-0000-000000000007\") And 'Partition Id' = \"p1\").Field1", 200.0, false, true)]

        // This delegates to retrieve single(slower api) because we only do point delegation when partionId is in equality.
        [InlineData(140, "LookUp(et, 'Partition Id' <> \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", null, true, true)]
        [InlineData(141, "LookUp(et, 'Partition Id' <> \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", null, false, false)]
        [InlineData(142, "LookUp(et, 'Partition Id' <> \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", null, true, false)]
        [InlineData(143, "LookUp(et, 'Partition Id' <> \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", null, false, true)]

        // This delegates to retrieve single(slower api) because we only do point delegation when partionId is present.
        [InlineData(144, "LookUp(et, 'Partition Id' = \"p1\" And Field1 > 199).Field1", 200.0, true, true)]
        [InlineData(145, "LookUp(et, 'Partition Id' = \"p1\" And Field1 > 199).Field1", 200.0, false, false)]
        [InlineData(146, "LookUp(et, 'Partition Id' = \"p1\" And Field1 > 199).Field1", 200.0, true, false)]
        [InlineData(147, "LookUp(et, 'Partition Id' = \"p1\" And Field1 > 199).Field1", 200.0, false, true)]

        // This delegates to retrieve single(slower api) because we only do point delegation when only partionId and primary id is in equality.
        [InlineData(148, "LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\") And Field1 > 199).Field1", 200.0, true, true)]
        [InlineData(149, "LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\") And Field1 > 199).Field1", 200.0, false, false)]
        [InlineData(150, "LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\") And Field1 > 199).Field1", 200.0, true, false)]
        [InlineData(151, "LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\") And Field1 > 199).Field1", 200.0, false, true)]

        // This delegates to retrieve single(slower api) because we only do point delegation when partionId is in equality only once.
        [InlineData(152, "LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\") And 'Partition Id' = \"p2\").Field1", null, true, true)]
        [InlineData(153, "LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\") And 'Partition Id' = \"p2\").Field1", null, false, false)]
        [InlineData(154, "LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\") And 'Partition Id' = \"p2\").Field1", null, true, false)]
        [InlineData(155, "LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\") And 'Partition Id' = \"p2\").Field1", null, false, true)]

        // This delegates to retrieve single(slower api) because we only do point delegation when partionId is in "And" condition only.
        [InlineData(156, "LookUp(et, 'Partition Id' = \"p1\" Or etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, true, true)]
        [InlineData(157, "LookUp(et, 'Partition Id' = \"p1\" Or etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, false, false)]
        [InlineData(158, "LookUp(et, 'Partition Id' = \"p1\" Or etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, true, false)]
        [InlineData(159, "LookUp(et, 'Partition Id' = \"p1\" Or etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, false, true)]

        // successful point delegation with complex expression.
        [InlineData(160, "LookUp(et, 'Partition Id' = If(1<0, \"p1\") And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, true, true)]
        [InlineData(161, "LookUp(et, 'Partition Id' = If(1<0, \"p1\") And  etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, false, false)]
        [InlineData(162, "LookUp(et, 'Partition Id' = If(1<0, \"p1\") And  etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, true, false)]
        [InlineData(163, "LookUp(et, 'Partition Id' = If(1<0, \"p1\") And  etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, false, true)]
        [InlineData(164, "LookUp(et, 'Partition Id' = LookUp(t1, Name = \"p1\").Name And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, true, true)]
        [InlineData(165, "LookUp(et, 'Partition Id' = LookUp(t1, Name = \"p1\").Name And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, false, false)]
        [InlineData(166, "LookUp(et, 'Partition Id' = LookUp(t1, Name = \"p1\").Name And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, true, true)]
        [InlineData(167, "LookUp(et, 'Partition Id' = LookUp(t1, Name = \"p1\").Name And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, false, false)]
        [InlineData(168, "LookUp(t1, State = 'State (Locals)'.Active).Price", 100.0, true, true)]
        [InlineData(169, "LookUp(t1, State = 'State (Locals)'.Active).Price", 100.0, false, false)]
        [InlineData(170, "LookUp(t1, State = 'State (Locals)'.Active).Price", 100.0, true, false)]
        [InlineData(171, "LookUp(t1, State = 'State (Locals)'.Active).Price", 100.0, false, true)]
        [InlineData(172, "LookUp(t1, State = If(1<0, 'State (Locals)'.Active)).Price", null, true, true)]
        [InlineData(173, "LookUp(t1, State = If(1<0, 'State (Locals)'.Active)).Price", null, false, false)]
        [InlineData(174, "LookUp(t1, State = If(1<0, 'State (Locals)'.Active)).Price", null, true, false)]
        [InlineData(175, "LookUp(t1, State = If(1<0, 'State (Locals)'.Active)).Price", null, false, true)]
        [InlineData(176, "LookUp(t1, Quantity = 20).'Elastic Ref'.Field1", 200.0, true, true)]
        [InlineData(177, "LookUp(t1, Quantity = 20).'Elastic Ref'.Field1", 200.0, false, false)]
        [InlineData(178, "LookUp(t1, Quantity = 20).'Elastic Ref'.Field1", 200.0, true, false)]
        [InlineData(179, "LookUp(t1, Quantity = 20).'Elastic Ref'.Field1", 200.0, false, true)]

        [InlineData(180, "LookUp(SortByColumns(t1, Price, SortOrder.Descending), Quantity = 20).'Elastic Ref'.Field1", 200.0, false, true)]
        [InlineData(181, "LookUp(ForAll(t1, {a: Price, b: Quantity}), b = 20).a", 100.0, false, true, "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData(182, "LookUp(t1, DateAdd(DateTime, -2) < DateTime)", null, true, true, "Warning 33-34: Can't delegate LtDateTime: Expression compares multiple fields.", "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData(183, @"LookUp(t1, ""o"" & ""W1"" in Name).Price", 100.0, false, false)]
        [InlineData(184, @"LookUp(t1, ""o"" & ""W1"" in Name).Price", 100.0, true, true)]
        [InlineData(185, @"LookUp(t1, ""o"" & ""W1"" in Name).Price", 100.0, true, false)]
        [InlineData(186, @"LookUp(t1, ""o"" & ""W1"" in Name).Price", 100.0, false, true)]
        [InlineData(187, @"LookUp(t1, 1 in Name).Price", 100.0, false, false)]
        [InlineData(188, @"LookUp(t1, 1 in Name).Price", 100.0, true, true)]
        [InlineData(189, @"LookUp(t1, 1 in Name).Price", 100.0, true, false)]
        [InlineData(190, @"LookUp(t1, 1 in Name).Price", 100.0, false, true)]        
        [InlineData(191, @"LookUp(t1, ""oW1"" in Name).Price", 100.0, false, false)]
        [InlineData(192, @"LookUp(t1, ""oW1"" in Name).Price", 100.0, true, true)]
        [InlineData(193, @"LookUp(t1, ""oW1"" in Name).Price", 100.0, true, false)]
        [InlineData(194, @"LookUp(t1, ""oW1"" in Name).Price", 100.0, false, true)]
        [InlineData(195, @"With({r: t1}, LookUp(r, ""oW1"" in Name)).Price", 100.0, false, false)]
        [InlineData(196, @"With({r: t1}, LookUp(r, ""oW1"" in Name)).Price", 100.0, true, true)]
        [InlineData(197, @"With({r: t1}, LookUp(r, ""oW1"" in Name)).Price", 100.0, true, false)]
        [InlineData(198, @"With({r: t1}, LookUp(r, ""oW1"" in Name)).Price", 100.0, false, true)]
        [InlineData(199, @"LookUp(t1, ""oW1"" exactin Name)", null, false, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(200, @"LookUp(t1, ""oW1"" exactin Name)", null, true, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(201, @"LookUp(t1, ""oW1"" exactin Name)", null, true, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(202, @"LookUp(t1, ""oW1"" exactin Name)", null, false, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(203, @"With({r: t1}, LookUp(r, ""oW1"" exactin Name))", null, false, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(204, @"With({r: t1}, LookUp(r, ""oW1"" exactin Name))", null, true, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(205, @"With({r: t1}, LookUp(r, ""oW1"" exactin Name))", null, true, false, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(206, @"With({r: t1}, LookUp(r, ""oW1"" exactin Name))", null, false, true, "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData(207, "LookUp(t1, ThisRecord.virtual.virtualremoteid = GUID(\"00000000-0000-0000-0000-000000000006\")).new_price", 100.0, false, false)]
        [InlineData(208, "LookUp(t1, ThisRecord.virtual.virtualremoteid = GUID(\"00000000-0000-0000-0000-000000000006\")).new_price", 100.0, true, true)]
        [InlineData(209, "LookUp(t1, ThisRecord.virtual.virtualremoteid = GUID(\"00000000-0000-0000-0000-000000000006\")).new_price", 100.0, true, false)]
        [InlineData(210, "LookUp(t1, ThisRecord.virtual.virtualremoteid = GUID(\"00000000-0000-0000-0000-000000000006\")).new_price", 100.0, false, true)]

        [InlineData(211, @"LookUp(t1, Not(IsBlank(Price))).Price", 100.0, false, true)]
        [InlineData(212, @"LookUp(t1, Not(IsBlank(Price))).Price", 100.0, false, true)]
        [InlineData(213, @"LookUp(t1, Not(IsBlank(Price))).Price", 100.0, false, true)]
        [InlineData(214, @"LookUp(t1, Not(IsBlank(Price))).Price", 100.0, false, true)]

        [InlineData(215, "LookUp(t1, Price < 200 And Not(IsBlank(Old_Price))).Price", 100.0, false, false)]
        [InlineData(216, "LookUp(t1, Price < 200 And Not(IsBlank(Old_Price))).Price", 100.0, true, true)]
        [InlineData(217, "LookUp(t1, Price < 200 And Not(IsBlank(Old_Price))).Price", 100.0, true, false)]
        [InlineData(218, "LookUp(t1, Price < 200 And Not(IsBlank(Old_Price))).Price", 100.0, false, true)]

        // predicate that uses function that is not delegable.
        [InlineData(219, "LookUp(t1, Price < 120 And Not(IsBlank(_count))).Price", 100.0, false, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(220, "LookUp(t1, Price < 120 And Not(IsBlank(_count))).Price", 100.0, true, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(221, "LookUp(t1, Price < 120 And Not(IsBlank(_count))).Price", 100.0, true, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(222, "LookUp(t1, Price < 120 And Not(IsBlank(_count))).Price", 100.0, false, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        public async Task LookUpDelegationAsync(int id, string expr, object expected, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
        {
            await DelegationTestAsync(
                id,
                "LookUpDelegation.txt",
                expr,
                -2,
                expected,
                result =>
                {
                    object res = result.ToObject();

                    if (expected is decimal && res is double dbl)
                    {
                        res = new decimal(dbl);
                    }

                    if (expected is double && res is decimal dec)
                    {
                        res = (double)dec;
                    }

                    return res;
                },
                cdsNumberIsFloat,
                parserNumberIsFloatOption,
                null,
                false,
                true,
                true,
                expectedWarnings);
        }

        [Theory]
        [InlineData("LookUp(t1, LocalId=If(Price>50, _g1, _gMissing)).Price", "Warning 22-27: Não é possível delegar LookUp: a expressão compara vários campos.")]
        [InlineData("LookUp(t1, LocalId=LocalId).Price", "Warning 18-19: Este predicado será sempre verdadeiro. Você quis usar ThisRecord ou [@ ]?", "Warning 19-26: Não é possível delegar LookUp: a expressão compara vários campos.")]
        [InlineData("LookUp(Filter(t1, 1=1), localid=_g1).Price", "Warning 14-16: Esta operação na tabela \"local\" poderá não funcionar se tiver mais de 999 linhas.")]
        public void LookUpDelegationWarningLocaleTest(string expr, params string[] expectedWarnings)
        {
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "t2");
            map.Add("virtualremote", "t3");
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels(policy: policy);

            var opts = PluginExecutionTests._parserAllowSideEffects;
            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_g1", FormulaValue.New(PluginExecutionTests._g1)); // matches entity
            engine1.UpdateVariable("_gMissing", FormulaValue.New(Guid.Parse("00000000-0000-0000-9999-000000000001"))); // no match

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.True(check.IsSuccess, string.Join("\r\n", check.Errors.Select(ee => ee.Message)));

            var errors_pt_br = check.GetErrorsInLocale(culture: CultureInfo.CreateSpecificCulture("pt-BR"));

            var errorList = errors_pt_br.Select(x => x.ToString()).OrderBy(x => x).ToArray();

            Assert.Equal(expectedWarnings.Length, errorList.Length);
            for (var i = 0; i < errorList.Length; i++)
            {
                Assert.Equal(expectedWarnings[i], errorList[i]);
            }
        }
    }
}
