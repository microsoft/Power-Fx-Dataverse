using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using System.Threading;
using Xunit;
using System.Threading.Tasks;
using System.Globalization;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class LookUpDelegationTests
    {
        // Table 't1' has
        // 1st item with
        // Price = 100, Old_Price = 200,  Date = Date(2023, 6, 1), DateTime = DateTime(2023, 6, 1, 12, 0, 0)
        // 2nd item with
        // Price = 10
        // 3rd item with
        // Price = -10

        [Theory]

        //Basic case
        [InlineData("LookUp(t1, Price = 255).Price",
            null,
            1,
            true,
            true)]
        [InlineData("LookUp(t1, Price = 255).Price",
            null,
            2,
            false,
            false)]
        [InlineData("LookUp(t1, Price = 255).Price",
            null,
            3,
            true,
            false)]
        [InlineData("LookUp(t1, Price = 255).Price",
            null,
            4,
            false,
            true)]

        [InlineData("LookUp(t1, IsBlank(Price)).Price",
            null,
            5,
            true,
            true)]

        // can't delegate IsBlank, because inside is non delegable.
        [InlineData("LookUp(t1, IsBlank(Price * Price)).Price",
            null,
            6,
            true,
            true,
            "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData("LookUp(t1, IsBlank(LookUp(t1, IsBlank(Price)))).Price",
            100.0,
            7,
            true,
            true,
            "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData("LookUp(t1, Integer = 255).Price",
            null,
            8,
            true,
            true)]
        [InlineData("LookUp(t1, Integer = 255).Price",
            null,
            9,
            false,
            false)]
        [InlineData("LookUp(t1, Integer = 255).Price",
            null,
            10,
            true,
            false)]
        [InlineData("LookUp(t1, Integer = 255).Price",
            null,
            11,
            false,
            true)]

        [InlineData("LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\")).Price",
            100.0,
            12,
            true,
            true)]

        //Basic case with And and Or
        [InlineData("LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\") And Price > 0).Price",
            100.0,
            13,
            true,
            true)]
        [InlineData("LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\") And Price > 0).Price",
            100.0,
            14,
            false,
            false)]
        [InlineData("LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\") And Price > 0).Price",
            100.0,
            15,
            true,
            false)]
        [InlineData("LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\") And Price > 0).Price",
            100.0,
            16,
            false,
            true)]

        [InlineData("LookUp(t1, LocalId=GUID(\"00000000-0000-0000-0000-000000000001\") Or Price > 0).Price",
            100.0,
            17,
            true,
            true)]
        [InlineData("LookUp(t1, LocalId=GUID(\"00000000-0000-0000-0000-000000000001\") Or Price > 0).Price",
            100.0,
            18,
            false,
            false)]
        [InlineData("LookUp(t1, LocalId=GUID(\"00000000-0000-0000-0000-000000000001\") Or Price > 0).Price",
            100.0,
            19,
            true,
            false)]
        [InlineData("LookUp(t1, LocalId=GUID(\"00000000-0000-0000-0000-000000000001\") Or Price > 0).Price",
            100.0,
            20,
            false,
            true)]

        // variable
        [InlineData("LookUp(t1, LocalId=_g1).Price",
            100.0,
            21,
            true,
            true)]

        // Date
        [InlineData("LookUp(t1, Date = Date(2023, 6, 1)).Price",
            100.0,
            22,
            true,
            true)]

        // These three tests have Coalesce for numeric literals where the NumberIsFloat version does not.
        // Although not wrong, they should be the same.  Being tracked with https://github.com/microsoft/Power-Fx/issues/1609
        [InlineData("LookUp(t1, Date = Date(2023, 6, 1)).Price",
            100.0,
            23,
            false,
            false)]

        // DateTime with coercion
        [InlineData("LookUp(t1, DateTime = Date(2023, 6, 1)).Price",
             null,
            24,
            true,
            true)]

        [InlineData("LookUp(t1, DateTime = DateTime(2023, 6, 1, 12, 0, 0)).Price",
             100.0,
            25,
            true,
            true)]

        [InlineData("LookUp(t1, DateTime = DateTime(2023, 6, 1, 12, 0, 0)).Price",
             100.0,
            26,
            false,
            false)]

        // reversed order still ok 
        [InlineData("LookUp(t1, _g1 = LocalId).Price",
            100.0,
            27,
            true,
            true)]

        // explicit ThisRecord is ok. IR will handle. 
        [InlineData("LookUp(t1, ThisRecord.LocalId=_g1).Price",
            100.0,
            28,
            true,
            true)]

        // Alias is ok. IR will handle. 
        [InlineData("LookUp(t1 As XYZ, XYZ.LocalId=_g1).Price",
            100.0,
            29,
            true,
            true)] // variable

        // lambda uses ThisRecord.Price, can't delegate
        [InlineData("LookUp(t1, LocalId=If(Price>50, _g1, _gMissing)).Price",
            100.0,
            30,
            true,
            true,
            "Warning 22-27: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData("LookUp(t1, LocalId=If(Price>50, _g1, _gMissing)).Price",
            100.0,
            31,
            false,
            false,
            "Warning 22-27: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData("LookUp(t1, LocalId=If(Price>50, _g1, _gMissing)).Price",
            100.0,
            32,
            true,
            false,
            "Warning 22-27: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData("LookUp(t1, LocalId=If(Price>50, _g1, _gMissing)).Price",
            100.0,
            33,
            false,
            true,
            "Warning 22-27: Can't delegate LookUp: Expression compares multiple fields.")]

        [InlineData("With({r: t1}, LookUp(r, LocalId=If(Price>50, _g1, _gMissing)).Price)",
            100.0,
            34,
            true,
            true,
            "Warning 35-40: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData("With({r: t1}, LookUp(r, LocalId=If(Price>50, _g1, _gMissing)).Price)",
            100.0,
            35,
            false,
            false,
            "Warning 35-40: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData("With({r: t1}, LookUp(r, LocalId=If(Price>50, _g1, _gMissing)).Price)",
            100.0,
            36,
            true,
            false,
            "Warning 35-40: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData("With({r: t1}, LookUp(r, LocalId=If(Price>50, _g1, _gMissing)).Price)",
            100.0,
            37,
            false,
            true,
            "Warning 35-40: Can't delegate LookUp: Expression compares multiple fields.")]

        // On non primary field.
        [InlineData("LookUp(t1, Price > 50).Price",
            100.0,
            38,
            true,
            true)]
        [InlineData("LookUp(t1, Price > 50).Price",
            100.0,
            39,
            false,
            false)]
        [InlineData("LookUp(t1, Price > 50).Price",
            100.0,
            40,
            true,
            false)]
        [InlineData("LookUp(t1, Price > 50).Price",
            100.0,
            41,
            false,
            true)]

        // successful with complex expression
        [InlineData("LookUp(t1, LocalId=If(true, _g1, _gMissing)).Price",
            100.0,
            42,
            true,
            true)]

        // nested delegation, both delegated.
        [InlineData("LookUp(t1, LocalId=LookUp(t1, LocalId=_g1).LocalId).Price",
            100.0,
            43,
            true,
            true)]

        // Can't delegate if Table Arg is delegated.
        [InlineData("LookUp(Filter(t1, Price = 100), localid=_g1).Price",
            100.0,
            44,
            true,
            true,
            "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows."
        )]
        [InlineData("LookUp(Filter(t1, Price = 100), localid=_g1).Price",
            100.0,
            45,
            false,
            false,
            "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows."
        )]
        [InlineData("LookUp(Filter(t1, Price = 100), localid=_g1).Price",
            100.0,
            46,
            true,
            false,
            "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows."
        )]
        [InlineData("LookUp(Filter(t1, Price = 100), localid=_g1).Price",
            100.0,
            47,
            false,
            true,
            "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows."
        )]

        [InlineData("With({r: t1}, LookUp(Filter(r, Price = 100), localid=_g1).Price)",
            100.0,
            48,
            true,
            true,
            "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows."
        )]
        [InlineData("With({r: t1}, LookUp(Filter(r, Price = 100), localid=_g1).Price)",
            100.0,
            49,
            false,
            false,
            "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows."
        )]
        [InlineData("With({r: t1}, LookUp(Filter(r, Price = 100), localid=_g1).Price)",
            100.0,
            50,
            true,
            false,
            "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows."
        )]
        [InlineData("With({r: t1}, LookUp(Filter(r, Price = 100), localid=_g1).Price)",
            100.0,
            51,
            false,
            true,
            "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows."
        )]

        // Can't delegate if Table Arg is delegated.
        [InlineData("LookUp(FirstN(t1, 1), localid=_g1).Price",
            100.0,
            52,
            true,
            true,
            "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData("With({r: t1}, LookUp(FirstN(r, 1), localid=_g1).Price)",
            100.0,
            53,
            true,
            true,
            "Warning 9-11: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Can Delegate on non primary-key field.
        [InlineData("LookUp(t1, LocalId=LookUp(t1, ThisRecord.Price>50).LocalId).Price",
            100.0,
            54,
            true,
            true)]
        [InlineData("LookUp(t1, LocalId=LookUp(t1, ThisRecord.Price>50).LocalId).Price",
            100.0,
            55,
            false,
            false)]
        [InlineData("LookUp(t1, LocalId=LookUp(t1, ThisRecord.Price>50).LocalId).Price",
            100.0,
            56,
            true,
            false)]
        [InlineData("LookUp(t1, LocalId=LookUp(t1, ThisRecord.Price>50).LocalId).Price",
            100.0,
            57,
            false,
            true)]

        [InlineData("LookUp(t1, LocalId=First([_g1,_gMissing]).Value).Price",
            100.0,
            58,
            true,
            true)]

        // unsupported function, can't yet delegate
        [InlineData("Last(t1).Price",
            -10.0,
            59,
            true,
            true,
            "Warning 5-7: This operation on table 'local' may not work if it has more than 999 rows."
            )]

        // unsupported function, can't yet delegate
        [InlineData("CountRows(t1)",
            3.0,
            60,
            true,
            true,
            "Warning 10-12: This operation on table 'local' may not work if it has more than 999 rows."
            )]

        // Functions like IsBlank, Collect,Patch, shouldn't require delegation. Ensure no warnings. 
        [InlineData("IsBlank(t1)",
            false, // nothing to delegate
            61,
            true,
            true
            )]

        [InlineData("IsBlank(Filter(t1, 1=1))",
            false, // nothing to delegate
            62,
            true,
            true,
            "Warning 15-17: This operation on table 'local' may not work if it has more than 999 rows."
            )]
        [InlineData("IsBlank(Filter(t1, 1=1))",
            false, // nothing to delegate
            63,
            false,
            false,
            "Warning 15-17: This operation on table 'local' may not work if it has more than 999 rows."
            )]
        [InlineData("IsBlank(Filter(t1, 1=1))",
            false, // nothing to delegate
            64,
            true,
            false,
            "Warning 15-17: This operation on table 'local' may not work if it has more than 999 rows."
            )]
        [InlineData("IsBlank(Filter(t1, 1=1))",
            false, // nothing to delegate
            65,
            false,
            true,
            "Warning 15-17: This operation on table 'local' may not work if it has more than 999 rows."
            )]

        [InlineData("Collect(t1, { Price : 200}).Price",
            200.0, // Collect shouldn't give warnings. 
            66,
            true,
            true)]
        [InlineData("Collect(t1, { Price : 200}).Price",
            200.0, // Collect shouldn't give warnings. 
            67,
            false,
            false)]
        [InlineData("Collect(t1, { Price : 200}).Price",
            200.0, // Collect shouldn't give warnings. 
            68,
            true,
            false)]
        [InlineData("Collect(t1, { Price : 200}).Price",
            200.0, // Collect shouldn't give warnings. 
            69,
            false,
            true)]

        // $$$ Confirm is NotFound Error or Blank? 
        [InlineData("IsError(LookUp(t1, LocalId=If(false, _g1, _gMissing)))",
            true, // delegated, but not found is Error
            70,
            true,
            true)]

        // $$$ Does using fakeT1, same as t1, cause warnings since it's not delegated?
        [InlineData("LookUp(fakeT1, LocalId=_g1).Price",
            100.0,
            71,
            true,
            true)] // variable

        [InlineData("LookUp(t1, LocalId=LocalId).Price",
            100.0,
            72,
            true,
            true,
            "Warning 18-19: This predicate will always be true. Did you mean to use ThisRecord or [@ ]?",
            "Warning 19-26: Can't delegate LookUp: Expression compares multiple fields.")] // variable

        [InlineData("With({r: t1}, LookUp(r, LocalId=LocalId).Price)",
            100.0,
            73,
            true,
            true,
            "Warning 31-32: This predicate will always be true. Did you mean to use ThisRecord or [@ ]?",
            "Warning 32-39: Can't delegate LookUp: Expression compares multiple fields.")] // variable

        // Error Handling
        [InlineData("LookUp(t1, Price = If(1/0, 255)).Price",
            typeof(ErrorValue),
            74,
            true,
            true)]
        [InlineData("LookUp(t1, Price = If(1/0, 255)).Price",
            typeof(ErrorValue),
            75,
            false,
            false)]
        [InlineData("LookUp(t1, Price = If(1/0, 255)).Price",
            typeof(ErrorValue),
            76,
            true,
            false)]
        [InlineData("LookUp(t1, Price = If(1/0, 255)).Price",
            typeof(ErrorValue),
            77,
            false,
            true)]

        // Blank Handling
        [InlineData("LookUp(t1, Price = Blank()).Price",
            null,
            78,
            true,
            true)]
        [InlineData("LookUp(t1, Price = Blank()).Price",
            null,
            79,
            false,
            false)]
        [InlineData("LookUp(t1, Price = Blank()).Price",
            null,
            80,
            true,
            false)]
        [InlineData("LookUp(t1, Price = Blank()).Price",
            null,
            81,
            false,
            true)]

        [InlineData("LookUp(t1, Price <> Blank()).Price",
            100.0,
            82,
            true,
            true)]
        [InlineData("LookUp(t1, Price <> Blank()).Price",
            100.0,
            83,
            false,
            false)]
        [InlineData("LookUp(t1, Price <> Blank()).Price",
            100.0,
            84,
            true,
            false)]
        [InlineData("LookUp(t1, Price <> Blank()).Price",
            100.0,
            85,
            false,
            true)]

        [InlineData("LookUp(t1, Price < Blank()).Price",
            -10.0,
            86,
            true,
            true)]
        [InlineData("LookUp(t1, Price < Blank()).Price",
            -10.0,
            87,
            false,
            false)]
        [InlineData("LookUp(t1, Price < Blank()).Price",
            -10.0,
            88,
            true,
            false)]
        [InlineData("LookUp(t1, Price < Blank()).Price",
            -10.0,
            89,
            false,
            true)]

        [InlineData("LookUp(t1, Currency > 0).Price",
            100.0,
            90,
            true,
            true)]
        [InlineData("LookUp(t1, Currency > 0).Price",
            100.0,
            91,
            false,
            false)]
        [InlineData("LookUp(t1, Currency > 0).Price",
            100.0,
            92,
            true,
            false)]
        [InlineData("LookUp(t1, Currency > 0).Price",
            100.0,
            93,
            false,
            true)]

        [InlineData("With({r: t1}, LookUp(r, Currency > 0).Price)",
            100.0,
            94,
            true,
            true)]
        [InlineData("With({r: t1}, LookUp(r, Currency > 0).Price)",
            100.0,
            95,
            false,
            false)]
        [InlineData("With({r: t1}, LookUp(r, Currency > 0).Price)",
            100.0,
            96,
            true,
            false)]
        [InlineData("With({r: t1}, LookUp(r, Currency > 0).Price)",
            100.0,
            97,
            false,
            true)]

        [InlineData("With({r : t1}, LookUp(r, LocalId=_g1).Price)",
            100.0,
            98,
            true,
            true)]

        [InlineData("With({r : Filter(t1, Price < 120)}, LookUp(r, Price > 90).Price)",
            100.0,
            99,
            false,
            false,
            "Warning 17-19: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData("With({r: t1} ,LookUp(r, localid=GUID(\"00000000-0000-0000-0000-000000000001\")).Price)",
            100.0,
            100,
            true,
            true)]

        [InlineData("LookUp(t1, virtual.'Virtual Data' = 10).Price",
            100.0,
            101,
            true,
            true)]
        [InlineData("LookUp(t1, virtual.'Virtual Data' = 10).Price",
            100.0,
            102,
            false,
            false)]
        [InlineData("LookUp(t1, virtual.'Virtual Data' = 10).Price",
            100.0,
            103,
            true,
            false)]
        [InlineData("LookUp(t1, virtual.'Virtual Data' = 10).Price",
            100.0,
            104,
            false,
            true)]

        [InlineData("LookUp(t1, virtual.'Virtual Data' <> 10).Price",
            10.0,
            105,
            true,
            true)]
        [InlineData("LookUp(t1, virtual.'Virtual Data' <> 10).Price",
            10.0,
            106,
            false,
            false)]
        [InlineData("LookUp(t1, virtual.'Virtual Data' <> 10).Price",
            10.0,
            107,
            true,
            false)]
        [InlineData("LookUp(t1, virtual.'Virtual Data' <> 10).Price",
            10.0,
            108,
            false,
            true)]

        [InlineData("LookUp(t1, virtual.'Virtual Data' <> 10 And Price <> 10).Price",
            -10.0,
            109,
            true,
            true)]
        [InlineData("LookUp(t1, virtual.'Virtual Data' <> 10 And Price <> 10).Price",
            -10.0,
            110,
            false,
            false)]
        [InlineData("LookUp(t1, virtual.'Virtual Data' <> 10 And Price <> 10).Price",
            -10.0,
            111,
            true,
            false)]
        [InlineData("LookUp(t1, virtual.'Virtual Data' <> 10 And Price <> 10).Price",
            -10.0,
            112,
            false,
            true)]

        [InlineData("LookUp(t1, IsBlank(virtual.'Virtual Data')).Price",
            10.0,
            113,
            true,
            true)]

        [InlineData("LookUp(t1, AsType(PolymorphicLookup, t2).Data = 200).Price",
            100.0,
            114,
            true,
            true)]
        [InlineData("LookUp(t1, AsType(PolymorphicLookup, t2).Data = 200).Price",
            100.0,
            115,
            false,
            false)]
        [InlineData("LookUp(t1, AsType(PolymorphicLookup, t2).Data = 200).Price",
            100.0,
            116,
            true,
            false)]
        [InlineData("LookUp(t1, AsType(PolymorphicLookup, t2).Data = 200).Price",
            100.0,
            117,
            false,
            true)]

        [InlineData("LookUp(t1, PolymorphicLookup = First(t2)).Price",
            100.0,
            118,
            true,
            true)]
        [InlineData("LookUp(t1, PolymorphicLookup = First(t2)).Price",
            100.0,
            119,
            false,
            false)]
        [InlineData("LookUp(t1, PolymorphicLookup = First(t2)).Price",
            100.0,
            120,
            true,
            false)]
        [InlineData("LookUp(t1, PolymorphicLookup = First(t2)).Price",
            100.0,
            121,
            false,
            true)]

        [InlineData("LookUp(t1, PolymorphicLookup <> First(t2)).Price",
            10.0,
            122,
            true,
            true)]
        [InlineData("LookUp(t1, PolymorphicLookup <> First(t2)).Price",
            10.0,
            123,
            false,
            false)]
        [InlineData("LookUp(t1, PolymorphicLookup <> First(t2)).Price",
            10.0,
            124,
            true,
            false)]
        [InlineData("LookUp(t1, PolymorphicLookup <> First(t2)).Price",
            10.0,
            125,
            false,
            true)]

        [InlineData("LookUp(Distinct(t1, new_quantity), Value < 20).Value",
            10.0,
            126,
            true,
            true)]
        [InlineData("LookUp(Distinct(t1, new_quantity), Value < 20).Value",
            10.0,
            127,
            false,
            false)]
        [InlineData("LookUp(Distinct(t1, new_quantity), Value < 20).Value",
            10.0,
            128,
            true,
            false)]
        [InlineData("LookUp(Distinct(t1, new_quantity), Value < 20).Value",
            10.0,
            129,
            false,
            true)]

        // Blank handling for retrieveGUID.
        [InlineData("LookUp(t1, localid= If(1<0, GUID(\"00000000-0000-0000-0000-000000000001\"))).Price",
            null,
            130,
            true,
            true)]

        // Error Handling.
        [InlineData("LookUp(t1, localid= If(1/0, GUID(\"00000000-0000-0000-0000-000000000001\"))).Price",
            typeof(ErrorValue),
            131,
            true,
            true)]

        [InlineData("LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, 132, true, true)]
        [InlineData("LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, 133, false, false)]
        [InlineData("LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, 134, true, false)]
        [InlineData("LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, 135, false, true)]

        [InlineData("LookUp(et, etid = GUID(\"00000000-0000-0000-0000-000000000007\") And 'Partition Id' = \"p1\").Field1", 200.0, 136, true, true)]
        [InlineData("LookUp(et, etid = GUID(\"00000000-0000-0000-0000-000000000007\") And 'Partition Id' = \"p1\").Field1", 200.0, 137, false, false)]
        [InlineData("LookUp(et, etid = GUID(\"00000000-0000-0000-0000-000000000007\") And 'Partition Id' = \"p1\").Field1", 200.0, 138, true, false)]
        [InlineData("LookUp(et, etid = GUID(\"00000000-0000-0000-0000-000000000007\") And 'Partition Id' = \"p1\").Field1", 200.0, 139, false, true)]

        // This delegates to retrieve single(slower api) because we only do point delegation when partionId is in equality.
        [InlineData("LookUp(et, 'Partition Id' <> \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", null, 140, true, true)]
        [InlineData("LookUp(et, 'Partition Id' <> \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", null, 141, false, false)]
        [InlineData("LookUp(et, 'Partition Id' <> \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", null, 142, true, false)]
        [InlineData("LookUp(et, 'Partition Id' <> \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", null, 143, false, true)]

        // This delegates to retrieve single(slower api) because we only do point delegation when partionId is present.
        [InlineData("LookUp(et, 'Partition Id' = \"p1\" And Field1 > 199).Field1", 200.0, 144, true, true)]
        [InlineData("LookUp(et, 'Partition Id' = \"p1\" And Field1 > 199).Field1", 200.0, 145, false, false)]
        [InlineData("LookUp(et, 'Partition Id' = \"p1\" And Field1 > 199).Field1", 200.0, 146, true, false)]
        [InlineData("LookUp(et, 'Partition Id' = \"p1\" And Field1 > 199).Field1", 200.0, 147, false, true)]

        // This delegates to retrieve single(slower api) because we only do point delegation when only partionId and primary id is in equality.
        [InlineData("LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\") And Field1 > 199).Field1", 200.0, 148, true, true)]
        [InlineData("LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\") And Field1 > 199).Field1", 200.0, 149, false, false)]
        [InlineData("LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\") And Field1 > 199).Field1", 200.0, 150, true, false)]
        [InlineData("LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\") And Field1 > 199).Field1", 200.0, 151, false, true)]

        // This delegates to retrieve single(slower api) because we only do point delegation when partionId is in equality only once.
        [InlineData("LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\") And 'Partition Id' = \"p2\").Field1", null, 152, true, true)]
        [InlineData("LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\") And 'Partition Id' = \"p2\").Field1", null, 153, false, false)]
        [InlineData("LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\") And 'Partition Id' = \"p2\").Field1", null, 154, true, false)]
        [InlineData("LookUp(et, 'Partition Id' = \"p1\" And etid = GUID(\"00000000-0000-0000-0000-000000000007\") And 'Partition Id' = \"p2\").Field1", null, 155, false, true)]

        // This delegates to retrieve single(slower api) because we only do point delegation when partionId is in "And" condition only.
        [InlineData("LookUp(et, 'Partition Id' = \"p1\" Or etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, 156, true, true)]
        [InlineData("LookUp(et, 'Partition Id' = \"p1\" Or etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, 157, false, false)]
        [InlineData("LookUp(et, 'Partition Id' = \"p1\" Or etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, 158, true, false)]
        [InlineData("LookUp(et, 'Partition Id' = \"p1\" Or etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, 159, false, true)]

        // successful point delegation with complex expression.
        [InlineData("LookUp(et, 'Partition Id' = If(1<0, \"p1\") And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, 160, true, true)]
        [InlineData("LookUp(et, 'Partition Id' = If(1<0, \"p1\") And  etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, 161, false, false)]
        [InlineData("LookUp(et, 'Partition Id' = If(1<0, \"p1\") And  etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, 162, true, false)]
        [InlineData("LookUp(et, 'Partition Id' = If(1<0, \"p1\") And  etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, 163, false, true)]

        [InlineData("LookUp(et, 'Partition Id' = LookUp(t1, Name = \"p1\").Name And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, 164, true, true)]
        [InlineData("LookUp(et, 'Partition Id' = LookUp(t1, Name = \"p1\").Name And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, 165, false, false)]
        [InlineData("LookUp(et, 'Partition Id' = LookUp(t1, Name = \"p1\").Name And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, 166, true, false)]
        [InlineData("LookUp(et, 'Partition Id' = LookUp(t1, Name = \"p1\").Name And etid = GUID(\"00000000-0000-0000-0000-000000000007\")).Field1", 200.0, 167, false, true)]

        [InlineData("LookUp(t1, State = 'State (Locals)'.Active).Price", 100.0, 168, true, true)]
        [InlineData("LookUp(t1, State = 'State (Locals)'.Active).Price", 100.0, 169, false, false)]
        [InlineData("LookUp(t1, State = 'State (Locals)'.Active).Price", 100.0, 170, true, false)]
        [InlineData("LookUp(t1, State = 'State (Locals)'.Active).Price", 100.0, 171, false, true)]

        [InlineData("LookUp(t1, State = If(1<0, 'State (Locals)'.Active)).Price", null, 172, true, true)]
        [InlineData("LookUp(t1, State = If(1<0, 'State (Locals)'.Active)).Price", null, 173, false, false)]
        [InlineData("LookUp(t1, State = If(1<0, 'State (Locals)'.Active)).Price", null, 174, true, false)]
        [InlineData("LookUp(t1, State = If(1<0, 'State (Locals)'.Active)).Price", null, 175, false, true)]
        public async Task LookUpDelegationAsync(string expr, object expected, int id, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
        {
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "t2");
            map.Add("virtualremote", "t3");
            map.Add("elastictable", "et");
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, EntityLookup el) =
                PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: cdsNumberIsFloat, policy: policy);
            var tableT1Type = dv.GetRecordType("local");

            var opts = parserNumberIsFloatOption ?
                PluginExecutionTests._parserAllowSideEffects_NumberIsFloat :
                PluginExecutionTests._parserAllowSideEffects;

            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_g1", FormulaValue.New(PluginExecutionTests._g1)); // matches entity
            engine1.UpdateVariable("_gMissing", FormulaValue.New(Guid.Parse("00000000-0000-0000-9999-000000000001"))); // no match

            // Add a variable with same table type.
            // But it's not in the same symbol table, so we can't delegate this. 
            // Previously this was UpdateVariable, but UpdateVariable no longer supports dataverse tables (by design).
            var fakeSymbolTable = new SymbolTable();
            var fakeSlot = fakeSymbolTable.AddVariable("fakeT1", tableT1Type.ToTable());

            var fakeTableValue = new DataverseTableValue(tableT1Type, dv, dv.GetMetadataOrThrow("local"));
            var allSymbols = ReadOnlySymbolTable.Compose(fakeSymbolTable, dv.Symbols);

            var inputs = DelegationTestUtility.TransformForWithFunction(expr, expectedWarnings?.Count() ?? 0);

            for(var i = 0; i < inputs.Count; i++)
            {
                expr = inputs[i];

                var check = engine1.Check(expr, options: opts, symbolTable: allSymbols);
                Assert.True(check.IsSuccess, string.Join("\r\n", check.Errors.Select(ee => ee.Message)));

                // comapre IR to verify the delegations are happening exactly where we expect 
                var irNode = check.ApplyIR();
                var actualIr = check.GetCompactIRString();

                await DelegationTestUtility.CompareSnapShotAsync("LookUpDelegation.txt", actualIr, id, i == 1);

                // Validate delegation warnings.
                // error.ToString() will capture warning status, message, and source span. 
                var errors = check.ApplyErrors();

                var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

                Assert.Equal(expectedWarnings.Length, errorList.Length);
                for (var j = 0; j < errorList.Length; j++)
                {
                    Assert.Equal(expectedWarnings[j], errorList[j]);
                }

                var scan = check.ScanDependencies(dv.MetadataCache);

                // Can still run and verify results. 
                var run = check.GetEvaluator();

                // Place a reference to tableT1 in the fakeT1 symbol values and compose in
                var fakeSymbolValues = new SymbolValues(fakeSymbolTable);
                fakeSymbolValues.Set(fakeSlot, fakeTableValue);
                var allValues = ReadOnlySymbolValues.Compose(fakeSymbolValues, dv.SymbolValues);

                var result = run.EvalAsync(CancellationToken.None, allValues).Result;

                if (expected is null)
                {
                    Assert.IsType<BlankValue>(result);
                }

                if (expected is Type expectedType)
                {
                    Assert.IsType<ErrorValue>(result);
                }
                else
                {
                    if (cdsNumberIsFloat && parserNumberIsFloatOption ||
                        cdsNumberIsFloat && !parserNumberIsFloatOption)
                    {
                        Assert.Equal(expected, result.ToObject());
                    }
                    else if (cdsNumberIsFloat && !parserNumberIsFloatOption)
                    {
                        Assert.Equal(expected, result.ToObject());
                    }
                    else
                    {
                        Assert.Equal(expected is double dexp ? new decimal(dexp) : expected, result.ToObject());
                    }
                }
            }
        }

        [Theory]
        [InlineData("LookUp(t1, LocalId=If(Price>50, _g1, _gMissing)).Price",
            "Warning 22-27: Não é possível delegar LookUp: a expressão compara vários campos.")]
        [InlineData("LookUp(t1, LocalId=LocalId).Price",
            "Warning 18-19: Este predicado será sempre verdadeiro. Você quis usar ThisRecord ou [@ ]?",
            "Warning 19-26: Não é possível delegar LookUp: a expressão compara vários campos.")]
        [InlineData("LookUp(Filter(t1, 1=1), localid=_g1).Price",
            "Warning 14-16: Esta operação na tabela \"local\" poderá não funcionar se tiver mais de 999 linhas."
            )]
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
