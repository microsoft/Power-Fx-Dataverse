using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using System.Threading;
using Xunit;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class DelegationTests
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
            "(__retrieveSingle(t1, __eq(t1, new_price, 255))).new_price",
            true,
            true)]
        [InlineData("LookUp(t1, Price = 255).Price",
            null,
            "(__retrieveSingle(t1, __eq(t1, new_price, 255))).new_price",
            false,
            false)]
        [InlineData("LookUp(t1, Price = 255).Price",
            null,
            "(__retrieveSingle(t1, __eq(t1, new_price, Float(255)))).new_price",
            true,
            false)]
        [InlineData("LookUp(t1, Price = 255).Price",
            null,
            "(__retrieveSingle(t1, __eq(t1, new_price, 255))).new_price",
            false,
            true)]

        [InlineData("LookUp(t1, IsBlank(Price)).Price",
            null,
            "(__retrieveSingle(t1, __eq(t1, new_price, Blank()))).new_price",
            true,
            true)]

        // can't delegate IsBlank, because inside is non delegable.
        [InlineData("LookUp(t1, IsBlank(Price * Price)).Price",
            null,
            "(LookUp(t1, (IsBlank(MulNumbers(new_price,new_price))))).new_price",
            true,
            true,
            "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData("LookUp(t1, IsBlank(LookUp(t1, IsBlank(Price)))).Price",
            100.0,
            "(LookUp(t1, (IsBlank(__retrieveSingle(t1, __eq(t1, new_price, Blank())))))).new_price",
            true,
            true,
            "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData("LookUp(t1, Integer = 255).Price",
            null,
            "(__retrieveSingle(t1, __eq(t1, new_int, 255))).new_price",
            true,
            true)]
        [InlineData("LookUp(t1, Integer = 255).Price",
            null,
            "(__retrieveSingle(t1, __eq(t1, new_int, 255))).new_price",
            false,
            false)]
        [InlineData("LookUp(t1, Integer = 255).Price",
            null,
            "(__retrieveSingle(t1, __eq(t1, new_int, Float(255)))).new_price",
            true,
            false)]
        [InlineData("LookUp(t1, Integer = 255).Price",
            null,
            "(__retrieveSingle(t1, __eq(t1, new_int, 255))).new_price",
            false,
            true)]

        [InlineData("LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\")).Price",
            100.0,
            "(__retrieveGUID(t1, GUID(00000000-0000-0000-0000-000000000001))).new_price",
            true,
            true)]

        //Basic case with And and Or
        [InlineData("LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\") And Price > 0).Price",
            100.0,
            "(__retrieveSingle(t1, __and(__eq(t1, localid, GUID(00000000-0000-0000-0000-000000000001)), __gt(t1, new_price, 0)))).new_price",
            true,
            true)]
        [InlineData("LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\") And Price > 0).Price",
            100.0,
            "(__retrieveSingle(t1, __and(__eq(t1, localid, GUID(00000000-0000-0000-0000-000000000001)), __gt(t1, new_price, 0)))).new_price",
            false,
            false)]
        [InlineData("LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\") And Price > 0).Price",
            100.0,
            "(__retrieveSingle(t1, __and(__eq(t1, localid, GUID(00000000-0000-0000-0000-000000000001)), __gt(t1, new_price, Float(0))))).new_price",
            true,
            false)]
        [InlineData("LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\") And Price > 0).Price",
            100.0,
            "(__retrieveSingle(t1, __and(__eq(t1, localid, GUID(00000000-0000-0000-0000-000000000001)), __gt(t1, new_price, 0)))).new_price",
            false,
            true)]

        [InlineData("LookUp(t1, LocalId=GUID(\"00000000-0000-0000-0000-000000000001\") Or Price > 0).Price",
            100.0,
            "(__retrieveSingle(t1, __or(__eq(t1, localid, GUID(00000000-0000-0000-0000-000000000001)), __gt(t1, new_price, 0)))).new_price",
            true,
            true)]
        [InlineData("LookUp(t1, LocalId=GUID(\"00000000-0000-0000-0000-000000000001\") Or Price > 0).Price",
            100.0,
            "(__retrieveSingle(t1, __or(__eq(t1, localid, GUID(00000000-0000-0000-0000-000000000001)), __gt(t1, new_price, 0)))).new_price",
            false,
            false)]
        [InlineData("LookUp(t1, LocalId=GUID(\"00000000-0000-0000-0000-000000000001\") Or Price > 0).Price",
            100.0,
            "(__retrieveSingle(t1, __or(__eq(t1, localid, GUID(00000000-0000-0000-0000-000000000001)), __gt(t1, new_price, Float(0))))).new_price",
            true,
            false)]
        [InlineData("LookUp(t1, LocalId=GUID(\"00000000-0000-0000-0000-000000000001\") Or Price > 0).Price",
            100.0,
            "(__retrieveSingle(t1, __or(__eq(t1, localid, GUID(00000000-0000-0000-0000-000000000001)), __gt(t1, new_price, 0)))).new_price",
            false,
            true)]

        // variable
        [InlineData("LookUp(t1, LocalId=_g1).Price",
            100.0,
            "(__retrieveGUID(t1, _g1)).new_price",
            true,
            true)]

        // Date
        [InlineData("LookUp(t1, Date = Date(2023, 6, 1)).Price",
            100.0,
            "(__retrieveSingle(t1, __eq(t1, new_date, Date(2023, 6, 1)))).new_price",
            true,
            true)]

        // These three tests have Coalesce for numeric literals where the NumberIsFloat version does not.
        // Although not wrong, they should be the same.  Being tracked with https://github.com/microsoft/Power-Fx/issues/1609
        [InlineData("LookUp(t1, Date = Date(2023, 6, 1)).Price",
            100.0,
            "(__retrieveSingle(t1, __eq(t1, new_date, Date(Coalesce(Float(2023), 0), Coalesce(Float(6), 0), Coalesce(Float(1), 0))))).new_price",
            false,
            false)]

        // DateTime with coercion
        [InlineData("LookUp(t1, DateTime = Date(2023, 6, 1)).Price",
             null,
            "(__retrieveSingle(t1, __eq(t1, new_datetime, DateToDateTime(Date(2023, 6, 1))))).new_price",
            true,
            true)]

        [InlineData("LookUp(t1, DateTime = DateTime(2023, 6, 1, 12, 0, 0)).Price",
             100.0,
            "(__retrieveSingle(t1, __eq(t1, new_datetime, DateTime(2023, 6, 1, 12, 0, 0)))).new_price",
            true,
            true)]

        [InlineData("LookUp(t1, DateTime = DateTime(2023, 6, 1, 12, 0, 0)).Price",
             100.0,
            "(__retrieveSingle(t1, __eq(t1, new_datetime, DateTime(Coalesce(Float(2023), 0), Coalesce(Float(6), 0), Coalesce(Float(1), 0), Coalesce(Float(12), 0), Coalesce(Float(0), 0), Coalesce(Float(0), 0))))).new_price",
            false,
            false)]

        // reversed order still ok 
        [InlineData("LookUp(t1, _g1 = LocalId).Price",
            100.0,
            "(__retrieveGUID(t1, _g1)).new_price",
            true,
            true)]

        // explicit ThisRecord is ok. IR will handle. 
        [InlineData("LookUp(t1, ThisRecord.LocalId=_g1).Price",
            100.0,
            "(__retrieveGUID(t1, _g1)).new_price",
            true,
            true)]

        // Alias is ok. IR will handle. 
        [InlineData("LookUp(t1 As XYZ, XYZ.LocalId=_g1).Price",
            100.0,
            "(__retrieveGUID(t1, _g1)).new_price",
            true,
            true)] // variable

        // lambda uses ThisRecord.Price, can't delegate
        [InlineData("LookUp(t1, LocalId=If(Price>50, _g1, _gMissing)).Price",
            100.0,
            "(LookUp(t1, (EqGuid(localid,If(GtNumbers(new_price,50), (_g1), (_gMissing)))))).new_price",
            true,
            true,
            "Warning 22-27: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData("LookUp(t1, LocalId=If(Price>50, _g1, _gMissing)).Price",
            100.0,
            "(LookUp(t1, (EqGuid(localid,If(GtDecimals(new_price,50), (_g1), (_gMissing)))))).new_price",
            false,
            false,
            "Warning 22-27: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData("LookUp(t1, LocalId=If(Price>50, _g1, _gMissing)).Price",
            100.0,
            "(LookUp(t1, (EqGuid(localid,If(GtNumbers(new_price,Float(50)), (_g1), (_gMissing)))))).new_price",
            true,
            false,
            "Warning 22-27: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData("LookUp(t1, LocalId=If(Price>50, _g1, _gMissing)).Price",
            100.0,
            "(LookUp(t1, (EqGuid(localid,If(GtNumbers(Value(new_price),50), (_g1), (_gMissing)))))).new_price",
            false,
            true,
            "Warning 22-27: Can't delegate LookUp: Expression compares multiple fields.")]

        [InlineData("With({r:t1}, LookUp(r, LocalId=If(Price>50, _g1, _gMissing)).Price)",
            100.0,
            "With({r:t1}, ((LookUp(r, (EqGuid(localid,If(GtNumbers(new_price,50), (_g1), (_gMissing)))))).new_price))",
            true,
            true,
            "Warning 34-39: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData("With({r:t1}, LookUp(r, LocalId=If(Price>50, _g1, _gMissing)).Price)",
            100.0,
            "With({r:t1}, ((LookUp(r, (EqGuid(localid,If(GtDecimals(new_price,50), (_g1), (_gMissing)))))).new_price))",
            false,
            false,
            "Warning 34-39: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData("With({r:t1}, LookUp(r, LocalId=If(Price>50, _g1, _gMissing)).Price)",
            100.0,
            "With({r:t1}, ((LookUp(r, (EqGuid(localid,If(GtNumbers(new_price,Float(50)), (_g1), (_gMissing)))))).new_price))",
            true,
            false,
            "Warning 34-39: Can't delegate LookUp: Expression compares multiple fields.")]
        [InlineData("With({r:t1}, LookUp(r, LocalId=If(Price>50, _g1, _gMissing)).Price)",
            100.0,
            "With({r:t1}, ((LookUp(r, (EqGuid(localid,If(GtNumbers(Value(new_price),50), (_g1), (_gMissing)))))).new_price))",
            false,
            true,
            "Warning 34-39: Can't delegate LookUp: Expression compares multiple fields.")]

        // On non primary field.
        [InlineData("LookUp(t1, Price > 50).Price",
            100.0,
            "(__retrieveSingle(t1, __gt(t1, new_price, 50))).new_price",
            true,
            true)]
        [InlineData("LookUp(t1, Price > 50).Price",
            100.0,
            "(__retrieveSingle(t1, __gt(t1, new_price, 50))).new_price",
            false,
            false)]
        [InlineData("LookUp(t1, Price > 50).Price",
            100.0,
            "(__retrieveSingle(t1, __gt(t1, new_price, Float(50)))).new_price",
            true,
            false)]
        [InlineData("LookUp(t1, Price > 50).Price",
            100.0,
            "(__retrieveSingle(t1, __gt(t1, new_price, 50))).new_price",
            false,
            true)]

        // successful with complex expression
        [InlineData("LookUp(t1, LocalId=If(true, _g1, _gMissing)).Price",
            100.0,
            "(__retrieveGUID(t1, If(True, (_g1), (_gMissing)))).new_price",
            true,
            true)]

        // nested delegation, both delegated.
        [InlineData("LookUp(t1, LocalId=LookUp(t1, LocalId=_g1).LocalId).Price",
            100.0,
            "(__retrieveGUID(t1, (__retrieveGUID(t1, _g1)).localid)).new_price",
            true,
            true)]

        // Can't delegate if Table Arg is delegated.
        [InlineData("LookUp(Filter(t1, Price = 100), localid=_g1).Price",
            100.0,
            "(LookUp(__retrieveMultiple(t1, __eq(t1, new_price, 100), 999), (EqGuid(localid,_g1)))).new_price",
            true,
            true,
            "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows."
        )]
        [InlineData("LookUp(Filter(t1, Price = 100), localid=_g1).Price",
            100.0,
            "(LookUp(__retrieveMultiple(t1, __eq(t1, new_price, 100), 999), (EqGuid(localid,_g1)))).new_price",
            false,
            false,
            "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows."
        )]
        [InlineData("LookUp(Filter(t1, Price = 100), localid=_g1).Price",
            100.0,
            "(LookUp(__retrieveMultiple(t1, __eq(t1, new_price, Float(100)), 999), (EqGuid(localid,_g1)))).new_price",
            true,
            false,
            "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows."
        )]
        [InlineData("LookUp(Filter(t1, Price = 100), localid=_g1).Price",
            100.0,
            "(LookUp(__retrieveMultiple(t1, __eq(t1, new_price, 100), 999), (EqGuid(localid,_g1)))).new_price",
            false,
            true,
            "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows."
        )]

        [InlineData("With({r:t1}, LookUp(Filter(r, Price = 100), localid=_g1).Price)",
            100.0,
            "With({r:t1}, ((LookUp(__retrieveMultiple(t1, __eq(t1, new_price, 100), 999), (EqGuid(localid,_g1)))).new_price))",
            true,
            true,
            "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows."
        )]
        [InlineData("With({r:t1}, LookUp(Filter(r, Price = 100), localid=_g1).Price)",
            100.0,
            "With({r:t1}, ((LookUp(__retrieveMultiple(t1, __eq(t1, new_price, 100), 999), (EqGuid(localid,_g1)))).new_price))",
            false,
            false,
            "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows."
        )]
        [InlineData("With({r:t1}, LookUp(Filter(r, Price = 100), localid=_g1).Price)",
            100.0,
            "With({r:t1}, ((LookUp(__retrieveMultiple(t1, __eq(t1, new_price, Float(100)), 999), (EqGuid(localid,_g1)))).new_price))",
            true,
            false,
            "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows."
        )]
        [InlineData("With({r:t1}, LookUp(Filter(r, Price = 100), localid=_g1).Price)",
            100.0,
            "With({r:t1}, ((LookUp(__retrieveMultiple(t1, __eq(t1, new_price, 100), 999), (EqGuid(localid,_g1)))).new_price))",
            false,
            true,
            "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows."
        )]

        // Can't delegate if Table Arg is delegated.
        [InlineData("LookUp(FirstN(t1, 1), localid=_g1).Price",
            100.0,
            "(LookUp(__retrieveMultiple(t1, __noFilter(), 1), (EqGuid(localid,_g1)))).new_price",
            true,
            true,
            "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData("With({r:t1}, LookUp(FirstN(r, 1), localid=_g1).Price)",
            100.0,
            "With({r:t1}, ((LookUp(__retrieveMultiple(t1, __noFilter(), 1), (EqGuid(localid,_g1)))).new_price))",
            true,
            true,
            "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Can Delegate on non primary-key field.
        [InlineData("LookUp(t1, LocalId=LookUp(t1, ThisRecord.Price>50).LocalId).Price",
            100.0,
            "(__retrieveGUID(t1, (__retrieveSingle(t1, __gt(t1, new_price, 50))).localid)).new_price",
            true,
            true)]
        [InlineData("LookUp(t1, LocalId=LookUp(t1, ThisRecord.Price>50).LocalId).Price",
            100.0,
            "(__retrieveGUID(t1, (__retrieveSingle(t1, __gt(t1, new_price, 50))).localid)).new_price",
            false,
            false)]
        [InlineData("LookUp(t1, LocalId=LookUp(t1, ThisRecord.Price>50).LocalId).Price",
            100.0,
            "(__retrieveGUID(t1, (__retrieveSingle(t1, __gt(t1, new_price, Float(50)))).localid)).new_price",
            true,
            false)]
        [InlineData("LookUp(t1, LocalId=LookUp(t1, ThisRecord.Price>50).LocalId).Price",
            100.0,
            "(__retrieveGUID(t1, (__retrieveSingle(t1, __gt(t1, new_price, 50))).localid)).new_price",
            false,
            true)]

        [InlineData("LookUp(t1, LocalId=First([_g1,_gMissing]).Value).Price",
            100.0,
            "(__retrieveGUID(t1, (First(Table({Value:_g1}, {Value:_gMissing}))).Value)).new_price",
            true,
            true)]

        // unsupported function, can't yet delegate
        [InlineData("Last(t1).Price",
            -10.0,
            "(Last(t1)).new_price",
            true,
            true,
            "Warning 5-7: This operation on table 'local' may not work if it has more than 999 rows."
            )]

        // unsupported function, can't yet delegate
        [InlineData("CountRows(t1)",
            3.0,
            "CountRows(t1)",
            true,
            true,
            "Warning 10-12: This operation on table 'local' may not work if it has more than 999 rows."
            )]

        // Functions like IsBlank, Collect,Patch, shouldn't require delegation. Ensure no warnings. 
        [InlineData("IsBlank(t1)",
            false, // nothing to delegate
            "IsBlank(t1)",
            true,
            true
            )]

        [InlineData("IsBlank(Filter(t1, 1=1))",
            false, // nothing to delegate
            "IsBlank(Filter(t1, (EqNumbers(1,1))))",
            true,
            true,
            "Warning 15-17: This operation on table 'local' may not work if it has more than 999 rows."
            )]
        [InlineData("IsBlank(Filter(t1, 1=1))",
            false, // nothing to delegate
            "IsBlank(Filter(t1, (EqDecimals(1,1))))",
            false,
            false,
            "Warning 15-17: This operation on table 'local' may not work if it has more than 999 rows."
            )]
        [InlineData("IsBlank(Filter(t1, 1=1))",
            false, // nothing to delegate
            "IsBlank(Filter(t1, (EqDecimals(1,1))))",
            true,
            false,
            "Warning 15-17: This operation on table 'local' may not work if it has more than 999 rows."
            )]
        [InlineData("IsBlank(Filter(t1, 1=1))",
            false, // nothing to delegate
            "IsBlank(Filter(t1, (EqNumbers(1,1))))",
            false,
            true,
            "Warning 15-17: This operation on table 'local' may not work if it has more than 999 rows."
            )]

        [InlineData("Collect(t1, { Price : 200}).Price",
            200.0, // Collect shouldn't give warnings. 
            "(Collect((t1), {new_price:200})).new_price",
            true,
            true)]
        [InlineData("Collect(t1, { Price : 200}).Price",
            200.0, // Collect shouldn't give warnings. 
            "(Collect((t1), {new_price:200})).new_price",
            false,
            false)]
        [InlineData("Collect(t1, { Price : 200}).Price",
            200.0, // Collect shouldn't give warnings. 
            "(Collect((t1), RecordToRecord([new_price:Float(new_price)],({new_price:200}))).new_price",
            true,
            false)]
        [InlineData("Collect(t1, { Price : 200}).Price",
            200.0, // Collect shouldn't give warnings. 
            "(Collect((t1), RecordToRecord([new_price:Decimal(new_price)],({new_price:200}))).new_price",
            false,
            true)]

        // $$$ Confirm is NotFound Error or Blank? 
        [InlineData("IsError(LookUp(t1, LocalId=If(false, _g1, _gMissing)))",
            true, // delegated, but not found is Error
            "IsError(__retrieveGUID(t1, If(False, (_g1), (_gMissing))))",
            true,
            true)]

        // $$$ Does using fakeT1, same as t1, cause warnings since it's not delegated?
        [InlineData("LookUp(fakeT1, LocalId=_g1).Price",
            100.0,
            "(LookUp(fakeT1, (EqGuid(localid,_g1)))).new_price",
            true,
            true)] // variable

        [InlineData("LookUp(t1, LocalId=LocalId).Price",
            100.0,
            "(LookUp(t1, (EqGuid(localid,localid)))).new_price",
            true,
            true,
            "Warning 18-19: This predicate will always be true. Did you mean to use ThisRecord or [@ ]?",
            "Warning 19-26: Can't delegate LookUp: Expression compares multiple fields.")] // variable

        [InlineData("With({r:t1}, LookUp(r, LocalId=LocalId).Price)",
            100.0,
            "With({r:t1}, ((LookUp(r, (EqGuid(localid,localid)))).new_price))",
            true,
            true,
            "Warning 30-31: This predicate will always be true. Did you mean to use ThisRecord or [@ ]?",
            "Warning 31-38: Can't delegate LookUp: Expression compares multiple fields.")] // variable

        // Error Handling
        [InlineData("LookUp(t1, Price = If(1/0, 255)).Price",
            typeof(ErrorValue),
            "(__retrieveSingle(t1, __eq(t1, new_price, If(NumberToBoolean(DivNumbers(1,0)), (255))))).new_price",
            true,
            true)]
        [InlineData("LookUp(t1, Price = If(1/0, 255)).Price",
            typeof(ErrorValue),
            "(__retrieveSingle(t1, __eq(t1, new_price, If(DecimalToBoolean(DivDecimals(1,0)), (255))))).new_price",
            false,
            false)]
        [InlineData("LookUp(t1, Price = If(1/0, 255)).Price",
            typeof(ErrorValue),
            "(__retrieveSingle(t1, __eq(t1, new_price, Float(If(DecimalToBoolean(DivDecimals(1,0)), (255)))))).new_price",
            true,
            false)]
        [InlineData("LookUp(t1, Price = If(1/0, 255)).Price",
            typeof(ErrorValue),
            "(__retrieveSingle(t1, __eq(t1, new_price, If(NumberToBoolean(DivNumbers(1,0)), (255))))).new_price",
            false,
            true)]

        // Blank Handling
        [InlineData("LookUp(t1, Price = Blank()).Price",
            null,
            "(__retrieveSingle(t1, __eq(t1, new_price, Blank()))).new_price",
            true,
            true)]
        [InlineData("LookUp(t1, Price = Blank()).Price",
            null,
            "(__retrieveSingle(t1, __eq(t1, new_price, Blank()))).new_price",
            false,
            false)]
        [InlineData("LookUp(t1, Price = Blank()).Price",
            null,
            "(__retrieveSingle(t1, __eq(t1, new_price, Blank()))).new_price",
            true,
            false)]
        [InlineData("LookUp(t1, Price = Blank()).Price",
            null,
            "(__retrieveSingle(t1, __eq(t1, new_price, Blank()))).new_price",
            false,
            true)]

        [InlineData("LookUp(t1, Price <> Blank()).Price",
            100.0,
            "(__retrieveSingle(t1, __neq(t1, new_price, Blank()))).new_price",
            true,
            true)]
        [InlineData("LookUp(t1, Price <> Blank()).Price",
            100.0,
            "(__retrieveSingle(t1, __neq(t1, new_price, Blank()))).new_price",
            false,
            false)]
        [InlineData("LookUp(t1, Price <> Blank()).Price",
            100.0,
            "(__retrieveSingle(t1, __neq(t1, new_price, Blank()))).new_price",
            true,
            false)]
        [InlineData("LookUp(t1, Price <> Blank()).Price",
            100.0,
            "(__retrieveSingle(t1, __neq(t1, new_price, Blank()))).new_price",
            false,
            true)]

        [InlineData("LookUp(t1, Price < Blank()).Price",
            -10.0,
            "(__retrieveSingle(t1, __lt(t1, new_price, Blank()))).new_price",
            true,
            true)]
        [InlineData("LookUp(t1, Price < Blank()).Price",
            -10.0,
            "(__retrieveSingle(t1, __lt(t1, new_price, Blank()))).new_price",
            false,
            false)]
        [InlineData("LookUp(t1, Price < Blank()).Price",
            -10.0,
            "(__retrieveSingle(t1, __lt(t1, new_price, Blank()))).new_price",
            true,
            false)]
        [InlineData("LookUp(t1, Price < Blank()).Price",
            -10.0,
            "(__retrieveSingle(t1, __lt(t1, new_price, Blank()))).new_price",
            false,
            true)]

        [InlineData("LookUp(t1, Currency > 0).Price",
            100.0,
            "(__retrieveSingle(t1, __gt(t1, new_currency, 0))).new_price",
            true,
            true)]
        [InlineData("LookUp(t1, Currency > 0).Price",
            100.0,
            "(__retrieveSingle(t1, __gt(t1, new_currency, 0))).new_price",
            false,
            false)]
        [InlineData("LookUp(t1, Currency > 0).Price",
            100.0,
            "(__retrieveSingle(t1, __gt(t1, new_currency, Float(0)))).new_price",
            true,
            false)]
        [InlineData("LookUp(t1, Currency > 0).Price",
            100.0,
            "(__retrieveSingle(t1, __gt(t1, new_currency, 0))).new_price",
            false,
            true)]

        [InlineData("With({r:t1}, LookUp(r, Currency > 0).Price)",
            100.0,
            "With({r:t1}, ((__retrieveSingle(t1, __gt(t1, new_currency, 0))).new_price))",
            true,
            true)]
        [InlineData("With({r:t1}, LookUp(r, Currency > 0).Price)",
            100.0,
            "With({r:t1}, ((__retrieveSingle(t1, __gt(t1, new_currency, 0))).new_price))",
            false,
            false)]
        [InlineData("With({r:t1}, LookUp(r, Currency > 0).Price)",
            100.0,
            "With({r:t1}, ((__retrieveSingle(t1, __gt(t1, new_currency, Float(0)))).new_price))",
            true,
            false)]
        [InlineData("With({r:t1}, LookUp(r, Currency > 0).Price)",
            100.0,
            "With({r:t1}, ((__retrieveSingle(t1, __gt(t1, new_currency, 0))).new_price))",
            false,
            true)]

        [InlineData("With({r : t1}, LookUp(r, LocalId=_g1).Price)",
            100.0,
            "With({r:t1}, ((__retrieveGUID(t1, _g1)).new_price))",
            true,
            true)]

        [InlineData("With({r : Filter(t1, Price < 120)}, LookUp(r, Price > 90).Price)",
            100.0,
            "With({r:__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)}, ((LookUp(__retrieveMultiple(t1, __lt(t1, new_price, 120), 999), (GtDecimals(new_price,90)))).new_price))",
            false,
            false,
            "Warning 17-19: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData("With({r: t1} ,LookUp(r, localid=GUID(\"00000000-0000-0000-0000-000000000001\")).Price)",
            100.0,
            "With({r:t1}, ((__retrieveGUID(t1, GUID(00000000-0000-0000-0000-000000000001))).new_price))",
            true,
            true)]

        [InlineData("LookUp(t1, virtual.'Virtual Data' = 10).Price",
            100.0,
            "(__retrieveSingle(t1, __eq(t1, vdata, 10, Table({Value:virtual})))).new_price",
            true,
            true)]
        [InlineData("LookUp(t1, virtual.'Virtual Data' = 10).Price",
            100.0,
            "(__retrieveSingle(t1, __eq(t1, vdata, 10, Table({Value:virtual})))).new_price",
            false,
            false)]
        [InlineData("LookUp(t1, virtual.'Virtual Data' = 10).Price",
            100.0,
            "(__retrieveSingle(t1, __eq(t1, vdata, Float(10), Table({Value:virtual})))).new_price",
            true,
            false)]
        [InlineData("LookUp(t1, virtual.'Virtual Data' = 10).Price",
            100.0,
            "(__retrieveSingle(t1, __eq(t1, vdata, 10, Table({Value:virtual})))).new_price",
            false,
            true)]

        [InlineData("LookUp(t1, virtual.'Virtual Data' <> 10).Price",
            10.0,
            "(__retrieveSingle(t1, __neq(t1, vdata, 10, Table({Value:virtual})))).new_price",
            true,
            true)]
        [InlineData("LookUp(t1, virtual.'Virtual Data' <> 10).Price",
            10.0,
            "(__retrieveSingle(t1, __neq(t1, vdata, 10, Table({Value:virtual})))).new_price",
            false,
            false)]
        [InlineData("LookUp(t1, virtual.'Virtual Data' <> 10).Price",
            10.0,
            "(__retrieveSingle(t1, __neq(t1, vdata, Float(10), Table({Value:virtual})))).new_price",
            true,
            false)]
        [InlineData("LookUp(t1, virtual.'Virtual Data' <> 10).Price",
            10.0,
            "(__retrieveSingle(t1, __neq(t1, vdata, 10, Table({Value:virtual})))).new_price",
            false,
            true)]

        [InlineData("LookUp(t1, virtual.'Virtual Data' <> 10 And Price <> 10).Price",
            -10.0,
            "(__retrieveSingle(t1, __and(__neq(t1, vdata, 10, Table({Value:virtual})), __neq(t1, new_price, 10)))).new_price",
            true,
            true)]
        [InlineData("LookUp(t1, virtual.'Virtual Data' <> 10 And Price <> 10).Price",
            -10.0,
            "(__retrieveSingle(t1, __and(__neq(t1, vdata, 10, Table({Value:virtual})), __neq(t1, new_price, 10)))).new_price",
            false,
            false)]
        [InlineData("LookUp(t1, virtual.'Virtual Data' <> 10 And Price <> 10).Price",
            -10.0,
            "(__retrieveSingle(t1, __and(__neq(t1, vdata, Float(10), Table({Value:virtual})), __neq(t1, new_price, Float(10))))).new_price",
            true,
            false)]
        [InlineData("LookUp(t1, virtual.'Virtual Data' <> 10 And Price <> 10).Price",
            -10.0,
            "(__retrieveSingle(t1, __and(__neq(t1, vdata, 10, Table({Value:virtual})), __neq(t1, new_price, 10)))).new_price",
            false,
            true)]

        [InlineData("LookUp(t1, IsBlank(virtual.'Virtual Data')).Price",
            10.0,
            "(__retrieveSingle(t1, __eq(t1, vdata, Blank(), Table({Value:virtual})))).new_price",
            true,
            true)]

        [InlineData("LookUp(t1, AsType(PolymorphicLookup, t2).Data = 200).Price",
            100.0,
            "(__retrieveSingle(t1, __eq(t1, data, 200, Table({Value:new_polyfield_t2_t1})))).new_price",
            true,
            true)]
        [InlineData("LookUp(t1, AsType(PolymorphicLookup, t2).Data = 200).Price",
            100.0,
            "(__retrieveSingle(t1, __eq(t1, data, 200, Table({Value:new_polyfield_t2_t1})))).new_price",
            false,
            false)]
        [InlineData("LookUp(t1, AsType(PolymorphicLookup, t2).Data = 200).Price",
            100.0,
            "(__retrieveSingle(t1, __eq(t1, data, Float(200), Table({Value:new_polyfield_t2_t1})))).new_price",
            true,
            false)]
        [InlineData("LookUp(t1, AsType(PolymorphicLookup, t2).Data = 200).Price",
            100.0,
            "(__retrieveSingle(t1, __eq(t1, data, 200, Table({Value:new_polyfield_t2_t1})))).new_price",
            false,
            true)]

        public void LookUpDelegation(string expr, object expected, string expectedIr, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";
            (DataverseConnection dv, EntityLookup el) = 
                PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: cdsNumberIsFloat);
            var tableT1 = dv.AddTable(displayName, logicalName);
            dv.AddTable("t2", "remote");

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
            var fakeSlot = fakeSymbolTable.AddVariable("fakeT1", tableT1.Type);
            var allSymbols = ReadOnlySymbolTable.Compose(fakeSymbolTable, dv.Symbols);

            var inputs = TransformForWithFunction(expr, expectedIr, expectedWarnings?.Count() ?? 0);

            foreach(var input in inputs)
            {
                expr = input.Item1;
                expectedIr = input.Item2;

                var check = engine1.Check(expr, options: opts, symbolTable: allSymbols);
                Assert.True(check.IsSuccess, string.Join("\r\n", check.Errors.Select(ee => ee.Message)));

                // comapre IR to verify the delegations are happening exactly where we expect 
                var irNode = check.ApplyIR();
                var actualIr = check.GetCompactIRString();
                Assert.Equal(expectedIr, actualIr);

                // Validate delegation warnings.
                // error.ToString() will capture warning status, message, and source span. 
                var errors = check.ApplyErrors();

                var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

                Assert.Equal(expectedWarnings.Length, errorList.Length);
                for (int i = 0; i < errorList.Length; i++)
                {
                    Assert.Equal(expectedWarnings[i], errorList[i]);
                }

                // Can still run and verify results. 
                var run = check.GetEvaluator();

                // Place a reference to tableT1 in the fakeT1 symbol values and compose in
                var fakeSymbolValues = new SymbolValues(fakeSymbolTable);
                fakeSymbolValues.Set(fakeSlot, tableT1);
                var allValues = SymbolValues.Compose(fakeSymbolValues, dv.SymbolValues);

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
                    if ((cdsNumberIsFloat && parserNumberIsFloatOption) ||
                        (cdsNumberIsFloat && !parserNumberIsFloatOption))
                    {
                        Assert.Equal(expected, result.ToObject());
                    }
                    else if(cdsNumberIsFloat && !parserNumberIsFloatOption)
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
        
        // Table 't1' has
        // 1st item with
        // Price = 100, Old_Price = 200,  Date = Date(2023, 6, 1), DateTime = DateTime(2023, 6, 1, 12, 0, 0)
        // 2nd item with
        // Price = 10
        // 3rd item with
        // Price = -10

        [Theory]

        // Basic case.
        [InlineData("FirstN(t1, 2)", 2, "__retrieveMultiple(t1, __noFilter(), Float(2))", false, false)]
        [InlineData("FirstN(t1, 2)", 2, "__retrieveMultiple(t1, __noFilter(), 2)", true, true)]
        [InlineData("FirstN(t1, 2)", 2, "__retrieveMultiple(t1, __noFilter(), Float(2))", true, false)]
        [InlineData("FirstN(t1, 2)", 2, "__retrieveMultiple(t1, __noFilter(), 2)", false, true)]

        // Variable as arg 
        [InlineData("FirstN(t1, _count)", 3, "__retrieveMultiple(t1, __noFilter(), Float(_count))", false, false)]
        [InlineData("FirstN(t1, _count)", 3, "__retrieveMultiple(t1, __noFilter(), Value(_count))", true, true)]
        [InlineData("FirstN(t1, _count)", 3, "__retrieveMultiple(t1, __noFilter(), Float(_count))", true, false)]
        [InlineData("FirstN(t1, _count)", 3, "__retrieveMultiple(t1, __noFilter(), Value(_count))", false, true)]

        // Function as arg 
        [InlineData("FirstN(t1, If(1<0,_count, 1))", 1, "__retrieveMultiple(t1, __noFilter(), Float(If(LtDecimals(1,0), (_count), (1))))", false, false)]
        [InlineData("FirstN(t1, If(1<0,_count, 1))", 1, "__retrieveMultiple(t1, __noFilter(), Value(If(LtNumbers(1,0), (_count), (Decimal(1)))))", true, true)]
        [InlineData("FirstN(t1, If(1<0,_count, 1))", 1, "__retrieveMultiple(t1, __noFilter(), Float(If(LtDecimals(1,0), (_count), (1))))", true, false)]
        [InlineData("FirstN(t1, If(1<0,_count, 1))", 1, "__retrieveMultiple(t1, __noFilter(), Value(If(LtNumbers(1,0), (_count), (Decimal(1)))))", false, true)]

        // Filter inside FirstN, both can be cominded (vice versa isn't true)
        [InlineData("FirstN(Filter(t1, Price > 90), 10)", 1, "__retrieveMultiple(t1, __gt(t1, new_price, 90), Float(10))", false, false)]
        [InlineData("FirstN(Filter(t1, Price > 90), 10)", 1, "__retrieveMultiple(t1, __gt(t1, new_price, 90), 10)", true, true)]
        [InlineData("FirstN(Filter(t1, Price > 90), 10)", 1, "__retrieveMultiple(t1, __gt(t1, new_price, Float(90)), Float(10))", true, false)]
        [InlineData("FirstN(Filter(t1, Price > 90), 10)", 1, "__retrieveMultiple(t1, __gt(t1, new_price, 90), 10)", false, true)]

        // Aliasing prevents delegation. 
        [InlineData("With({r : t1}, FirstN(r, Float(100)))", 3, "With({r:t1}, (__retrieveMultiple(t1, __noFilter(), Float(100))))", false, false)]
        [InlineData("With({r : t1}, FirstN(r, 100))", 3, "With({r:t1}, (__retrieveMultiple(t1, __noFilter(), 100)))", true, true)]
        [InlineData("With({r : t1}, FirstN(r, 100))", 3, "With({r:t1}, (__retrieveMultiple(t1, __noFilter(), Float(100))))", true, false)]
        [InlineData("With({r : t1}, FirstN(r, 100))", 3, "With({r:t1}, (__retrieveMultiple(t1, __noFilter(), 100)))", false, true)]

        // Error handling

        // Error propagates
        [InlineData("FirstN(t1, 1/0)", -1, "__retrieveMultiple(t1, __noFilter(), Float(DivDecimals(1,0)))", false, false)]
        [InlineData("FirstN(t1, 1/0)", -1, "__retrieveMultiple(t1, __noFilter(), DivNumbers(1,0))", true, true)]
        [InlineData("FirstN(t1, 1/0)", -1, "__retrieveMultiple(t1, __noFilter(), Float(DivDecimals(1,0)))", true, false)]
        [InlineData("FirstN(t1, 1/0)", -1, "__retrieveMultiple(t1, __noFilter(), DivNumbers(1,0))", false, true)]

        // Blank is treated as 0.
        [InlineData("FirstN(t1, If(1<0, 1))", 0, "__retrieveMultiple(t1, __noFilter(), Float(If(LtDecimals(1,0), (1))))", false, false)]
        [InlineData("FirstN(t1, If(1<0, 1))", 0, "__retrieveMultiple(t1, __noFilter(), If(LtNumbers(1,0), (1)))", true,  true)]
        [InlineData("FirstN(t1, If(1<0, 1))", 0, "__retrieveMultiple(t1, __noFilter(), Float(If(LtDecimals(1,0), (1))))", true,  false)]
        [InlineData("FirstN(t1, If(1<0, 1))", 0, "__retrieveMultiple(t1, __noFilter(), If(LtNumbers(1,0), (1)))", false,  true)]

        //Inserts default second arg.
        [InlineData("FirstN(t1)", 1, "__retrieveMultiple(t1, __noFilter(), 1)", false, false)]
        [InlineData("FirstN(t1)", 1, "__retrieveMultiple(t1, __noFilter(), 1)", true, true)]
        [InlineData("FirstN(t1)", 1, "__retrieveMultiple(t1, __noFilter(), 1)", true, false)]
        [InlineData("FirstN(t1)", 1, "__retrieveMultiple(t1, __noFilter(), 1)", false, true)]
        public void FirstNDelegation(string expr, int expectedRows, string expectedIr, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: cdsNumberIsFloat); 
            var tableT1 = dv.AddTable(displayName, logicalName);

            var opts = parserNumberIsFloatOption ?
                PluginExecutionTests._parserAllowSideEffects_NumberIsFloat :
                PluginExecutionTests._parserAllowSideEffects;

            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            config.Features.FirstLastNRequiresSecondArguments = false;
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_count", FormulaValue.New(100));

            var inputs = TransformForWithFunction(expr, expectedIr, expectedWarnings?.Count() ?? 0);

            foreach (var input in inputs)
            {
                expr = input.Item1;
                expectedIr = input.Item2;

                var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
                Assert.True(check.IsSuccess);

                // compare IR to verify the delegations are happening exactly where we expect 
                var irNode = check.ApplyIR();
                var actualIr = check.GetCompactIRString();
                Assert.Equal(expectedIr, actualIr);

                // Validate delegation warnings.
                // error.ToString() will capture warning status, message, and source span. 
                var errors = check.ApplyErrors();

                var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

                Assert.Equal(expectedWarnings.Length, errorList.Length);
                for (int i = 0; i < errorList.Length; i++)
                {
                    Assert.Equal(expectedWarnings[i], errorList[i]);
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

        // Table 't1' has
        // 1st item with
        // Price = 100, Old_Price = 200,  Date = Date(2023, 6, 1), DateTime = DateTime(2023, 6, 1, 12, 0, 0), Currency = 100
        // 2nd item with
        // Price = 10
        // 3rd item with
        // Price = -10

        [Theory]

        //Basic case 
        [InlineData("Filter(t1, Price < 100)", 2, "__retrieveMultiple(t1, __lt(t1, new_price, 100), 999)", false, false)]
        [InlineData("Filter(t1, Price < 100)", 2, "__retrieveMultiple(t1, __lt(t1, new_price, 100), 999)", true, true)]
        [InlineData("Filter(t1, Price < 100)", 2, "__retrieveMultiple(t1, __lt(t1, new_price, Float(100)), 999)", true, false)]
        [InlineData("Filter(t1, Price < 100)", 2, "__retrieveMultiple(t1, __lt(t1, new_price, 100), 999)", false, true)]

        [InlineData("Filter(t1, Price <= 100)", 3, "__retrieveMultiple(t1, __lte(t1, new_price, 100), 999)", false, false)]
        [InlineData("Filter(t1, Price <= 100)", 3, "__retrieveMultiple(t1, __lte(t1, new_price, 100), 999)", true, true)]
        [InlineData("Filter(t1, Price <= 100)", 3, "__retrieveMultiple(t1, __lte(t1, new_price, Float(100)), 999)", true, false)]
        [InlineData("Filter(t1, Price <= 100)", 3, "__retrieveMultiple(t1, __lte(t1, new_price, 100), 999)", false, true)]

        [InlineData("Filter(t1, Price = 100)", 1, "__retrieveMultiple(t1, __eq(t1, new_price, 100), 999)", false, false)]
        [InlineData("Filter(t1, Price = 100)", 1, "__retrieveMultiple(t1, __eq(t1, new_price, 100), 999)", true, true)]
        [InlineData("Filter(t1, Price = 100)", 1, "__retrieveMultiple(t1, __eq(t1, new_price, Float(100)), 999)", true, false)]
        [InlineData("Filter(t1, Price = 100)", 1, "__retrieveMultiple(t1, __eq(t1, new_price, 100), 999)", false, true)]

        [InlineData("Filter(t1, Price > 100)", 0, "__retrieveMultiple(t1, __gt(t1, new_price, 100), 999)", false, false)]
        [InlineData("Filter(t1, Price > 100)", 0, "__retrieveMultiple(t1, __gt(t1, new_price, 100), 999)", true, true)]
        [InlineData("Filter(t1, Price > 100)", 0, "__retrieveMultiple(t1, __gt(t1, new_price, Float(100)), 999)", true, false)]
        [InlineData("Filter(t1, Price > 100)", 0, "__retrieveMultiple(t1, __gt(t1, new_price, 100), 999)", false, true)]

        [InlineData("Filter(t1, Price >= 100)", 1, "__retrieveMultiple(t1, __gte(t1, new_price, 100), 999)", false, false)]
        [InlineData("Filter(t1, Price >= 100)", 1, "__retrieveMultiple(t1, __gte(t1, new_price, 100), 999)", true, true)]
        [InlineData("Filter(t1, Price >= 100)", 1, "__retrieveMultiple(t1, __gte(t1, new_price, Float(100)), 999)", true, false)]
        [InlineData("Filter(t1, Price >= 100)", 1, "__retrieveMultiple(t1, __gte(t1, new_price, 100), 999)", false, true)]

        [InlineData("Filter(t1, Price < Float(120))", 3, "__retrieveMultiple(t1, __lt(t1, new_price, Float(120)), 999)", false, false)]
        [InlineData("Filter(t1, Price < Float(120))", 3, "__retrieveMultiple(t1, __lt(t1, new_price, Float(120)), 999)", true, true)]
        [InlineData("Filter(t1, Price < Float(120))", 3, "__retrieveMultiple(t1, __lt(t1, new_price, Float(120)), 999)", true, false)]
        [InlineData("Filter(t1, Price < Float(120))", 3, "__retrieveMultiple(t1, __lt(t1, new_price, Float(120)), 999)", false, true)]

        [InlineData("Filter(t1, Price < Decimal(20))", 2, "__retrieveMultiple(t1, __lt(t1, new_price, Decimal(20)), 999)", false, false)]
        [InlineData("Filter(t1, Price < Decimal(20))", 2, "__retrieveMultiple(t1, __lt(t1, new_price, Value(Decimal(20))), 999)", true, true)]
        [InlineData("Filter(t1, Price < Decimal(20))", 2, "__retrieveMultiple(t1, __lt(t1, new_price, Float(Decimal(20))), 999)", true, false)]
        [InlineData("Filter(t1, Price < Decimal(20))", 2, "__retrieveMultiple(t1, __lt(t1, new_price, Decimal(20)), 999)", false, true)]

        [InlineData("Filter(t1, Price < Abs(-120))", 3, "__retrieveMultiple(t1, __lt(t1, new_price, Abs(Coalesce(NegateDecimal(120), 0))), 999)", false, false)]
        [InlineData("Filter(t1, Price < Abs(-120))", 3, "__retrieveMultiple(t1, __lt(t1, new_price, Abs(Coalesce(Negate(120), 0))), 999)", true, true)]
        [InlineData("Filter(t1, Price < Abs(-120))", 3, "__retrieveMultiple(t1, __lt(t1, new_price, Float(Abs(Coalesce(NegateDecimal(120), 0)))), 999)", true, false)]
        [InlineData("Filter(t1, Price < Abs(-120))", 3, "__retrieveMultiple(t1, __lt(t1, new_price, Abs(Coalesce(Negate(120), 0))), 999)", false, true)]

        // These two tests have Coalesce for numeric literals where the NumberIsFloat version does not.
        // Although not wrong, they should be the same.  Being tracked with https://github.com/microsoft/Power-Fx/issues/1609
        // Date
        [InlineData("Filter(t1, Date = Date(2023, 6, 1))", 1, "__retrieveMultiple(t1, __eq(t1, new_date, Date(Coalesce(Float(2023), 0), Coalesce(Float(6), 0), Coalesce(Float(1), 0))), 999)", false, false)]
        [InlineData("Filter(t1, Date = Date(2023, 6, 1))", 1, "__retrieveMultiple(t1, __eq(t1, new_date, Date(2023, 6, 1)), 999)", true, true)]
        [InlineData("Filter(t1, Date = Date(2023, 6, 1))", 1, "__retrieveMultiple(t1, __eq(t1, new_date, Date(Coalesce(Float(2023), 0), Coalesce(Float(6), 0), Coalesce(Float(1), 0))), 999)", true, false)]
        [InlineData("Filter(t1, Date = Date(2023, 6, 1))", 1, "__retrieveMultiple(t1, __eq(t1, new_date, Date(2023, 6, 1)), 999)", false, true)]

        // DateTime with coercion
        [InlineData("Filter(t1, DateTime = Date(2023, 6, 1))", 0, "__retrieveMultiple(t1, __eq(t1, new_datetime, DateToDateTime(Date(Coalesce(Float(2023), 0), Coalesce(Float(6), 0), Coalesce(Float(1), 0)))), 999)", false, false)]
        [InlineData("Filter(t1, DateTime = Date(2023, 6, 1))", 0, "__retrieveMultiple(t1, __eq(t1, new_datetime, DateToDateTime(Date(2023, 6, 1))), 999)", true, true)]
        [InlineData("Filter(t1, DateTime = Date(2023, 6, 1))", 0, "__retrieveMultiple(t1, __eq(t1, new_datetime, DateToDateTime(Date(Coalesce(Float(2023), 0), Coalesce(Float(6), 0), Coalesce(Float(1), 0)))), 999)", true, false)]
        [InlineData("Filter(t1, DateTime = Date(2023, 6, 1))", 0, "__retrieveMultiple(t1, __eq(t1, new_datetime, DateToDateTime(Date(2023, 6, 1))), 999)", false, true)]

        [InlineData("With({r:t1}, Filter(r, DateTime = Date(2023, 6, 1)))", 0, "With({r:t1}, (__retrieveMultiple(t1, __eq(t1, new_datetime, DateToDateTime(Date(Coalesce(Float(2023), 0), Coalesce(Float(6), 0), Coalesce(Float(1), 0)))), 999)))", false, false)]
        [InlineData("With({r:t1}, Filter(r, DateTime = Date(2023, 6, 1)))", 0, "With({r:t1}, (__retrieveMultiple(t1, __eq(t1, new_datetime, DateToDateTime(Date(2023, 6, 1))), 999)))", true, true)]
        [InlineData("With({r:t1}, Filter(r, DateTime = Date(2023, 6, 1)))", 0, "With({r:t1}, (__retrieveMultiple(t1, __eq(t1, new_datetime, DateToDateTime(Date(Coalesce(Float(2023), 0), Coalesce(Float(6), 0), Coalesce(Float(1), 0)))), 999)))", true, false)]
        [InlineData("With({r:t1}, Filter(r, DateTime = Date(2023, 6, 1)))", 0, "With({r:t1}, (__retrieveMultiple(t1, __eq(t1, new_datetime, DateToDateTime(Date(2023, 6, 1))), 999)))", false, true)]

        //Order doesn't matter
        [InlineData("Filter(t1, 0 > Price)", 1, "__retrieveMultiple(t1, __lt(t1, new_price, 0), 999)", false, false)]
        [InlineData("Filter(t1, 0 > Price)", 1, "__retrieveMultiple(t1, __lt(t1, new_price, 0), 999)", true, true)]
        [InlineData("Filter(t1, 0 > Price)", 1, "__retrieveMultiple(t1, __lt(t1, new_price, Float(0)), 999)", true, false)]
        [InlineData("Filter(t1, 0 > Price)", 1, "__retrieveMultiple(t1, __lt(t1, new_price, 0), 999)", false, true)]

        // Variable as arg 
        [InlineData("Filter(t1, Price > _count)", 0, "__retrieveMultiple(t1, __gt(t1, new_price, _count), 999)", false, false)]
        [InlineData("Filter(t1, Price > _count)", 0, "__retrieveMultiple(t1, __gt(t1, new_price, Value(_count)), 999)", true, true)]
        [InlineData("Filter(t1, Price > _count)", 0, "__retrieveMultiple(t1, __gt(t1, new_price, Float(_count)), 999)", true, false)]
        [InlineData("Filter(t1, Price > _count)", 0, "__retrieveMultiple(t1, __gt(t1, new_price, _count), 999)", false, true)]

        // Function as arg 
        [InlineData("Filter(t1, Price > If(1<0,_count, 1))", 2, "__retrieveMultiple(t1, __gt(t1, new_price, If(LtDecimals(1,0), (_count), (1))), 999)", false, false)]
        [InlineData("Filter(t1, Price > If(1<0,_count, 1))", 2, "__retrieveMultiple(t1, __gt(t1, new_price, Value(If(LtNumbers(1,0), (_count), (Decimal(1))))), 999)", true, true)]
        [InlineData("Filter(t1, Price > If(1<0,_count, 1))", 2, "__retrieveMultiple(t1, __gt(t1, new_price, Float(If(LtDecimals(1,0), (_count), (1)))), 999)", true, false)]
        [InlineData("Filter(t1, Price > If(1<0,_count, 1))", 2, "__retrieveMultiple(t1, __gt(t1, new_price, If(LtNumbers(1,0), (_count), (Decimal(1)))), 999)", false, true)]

        // Filter nested in another function both delegated.
        [InlineData("Filter(Filter(t1, Price > 0), Price < 100)", 1, "__retrieveMultiple(t1, __and(__gt(t1, new_price, 0), __lt(t1, new_price, 100)), 999)", false, false)]
        [InlineData("Filter(Filter(t1, Price > 0), Price < 100)", 1, "__retrieveMultiple(t1, __and(__gt(t1, new_price, 0), __lt(t1, new_price, 100)), 999)", true, true)]
        [InlineData("Filter(Filter(t1, Price > 0), Price < 100)", 1, "__retrieveMultiple(t1, __and(__gt(t1, new_price, Float(0)), __lt(t1, new_price, Float(100))), 999)", true, false)]
        [InlineData("Filter(Filter(t1, Price > 0), Price < 100)", 1, "__retrieveMultiple(t1, __and(__gt(t1, new_price, 0), __lt(t1, new_price, 100)), 999)", false, true)]

        // Basic case with And
        [InlineData("Filter(t1, Price < 120 And 90 < Price)", 1, "__retrieveMultiple(t1, __and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)), 999)", false, false)]
        [InlineData("Filter(t1, Price < 120 And 90 < Price)", 1, "__retrieveMultiple(t1, __and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)), 999)", true, true)]
        [InlineData("Filter(t1, Price < 120 And 90 < Price)", 1, "__retrieveMultiple(t1, __and(__lt(t1, new_price, Float(120)), __gt(t1, new_price, Float(90))), 999)", true, false)]
        [InlineData("Filter(t1, Price < 120 And 90 < Price)", 1, "__retrieveMultiple(t1, __and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)), 999)", false, true)]

        // Basic case with Or
        [InlineData("Filter(t1, Price < 0 Or Price > 90)", 2, "__retrieveMultiple(t1, __or(__lt(t1, new_price, 0), __gt(t1, new_price, 90)), 999)", false, false)]
        [InlineData("Filter(t1, Price < 0 Or Price > 90)", 2, "__retrieveMultiple(t1, __or(__lt(t1, new_price, 0), __gt(t1, new_price, 90)), 999)", true, true)]
        [InlineData("Filter(t1, Price < 0 Or Price > 90)", 2, "__retrieveMultiple(t1, __or(__lt(t1, new_price, Float(0)), __gt(t1, new_price, Float(90))), 999)", true, false)]
        [InlineData("Filter(t1, Price < 0 Or Price > 90)", 2, "__retrieveMultiple(t1, __or(__lt(t1, new_price, 0), __gt(t1, new_price, 90)), 999)", false, true)]


        // Delegation Not Allowed 

        // predicate that uses function that is not delegable.
        [InlineData("Filter(t1, IsBlank(Price))", 0, "__retrieveMultiple(t1, __eq(t1, new_price, Blank()), 999)", false, false)]
        [InlineData("Filter(t1, IsBlank(Price))", 0, "__retrieveMultiple(t1, __eq(t1, new_price, Blank()), 999)", true, true)]
        [InlineData("Filter(t1, IsBlank(Price))", 0, "__retrieveMultiple(t1, __eq(t1, new_price, Blank()), 999)", true, false)]
        [InlineData("Filter(t1, IsBlank(Price))", 0, "__retrieveMultiple(t1, __eq(t1, new_price, Blank()), 999)", false, true)]

        [InlineData("Filter(t1, Price < 200 And IsBlank(Old_Price))", 1, "__retrieveMultiple(t1, __and(__lt(t1, new_price, 200), __eq(t1, old_price, Blank())), 999)", false, false)]
        [InlineData("Filter(t1, Price < 200 And IsBlank(Old_Price))", 1, "__retrieveMultiple(t1, __and(__lt(t1, new_price, 200), __eq(t1, old_price, Blank())), 999)", true, true)]
        [InlineData("Filter(t1, Price < 200 And IsBlank(Old_Price))", 1, "__retrieveMultiple(t1, __and(__lt(t1, new_price, Float(200)), __eq(t1, old_price, Blank())), 999)", true, false)]
        [InlineData("Filter(t1, Price < 200 And IsBlank(Old_Price))", 1, "__retrieveMultiple(t1, __and(__lt(t1, new_price, 200), __eq(t1, old_price, Blank())), 999)", false, true)]

        // predicate that uses function that is not delegable.
        [InlineData("Filter(t1, Price < 120 And IsBlank(_count))", 0, "Filter(t1, (And(LtDecimals(new_price,120), (IsBlank(_count)))))", false, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(t1, Price < 120 And IsBlank(_count))", 0, "Filter(t1, (And(LtNumbers(new_price,120), (IsBlank(_count)))))", true, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(t1, Price < 120 And IsBlank(_count))", 0, "Filter(t1, (And(LtNumbers(new_price,Float(120)), (IsBlank(_count)))))", true, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(t1, Price < 120 And IsBlank(_count))", 0, "Filter(t1, (And(LtNumbers(Value(new_price),120), (IsBlank(_count)))))", false, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData("With({r:t1}, Filter(r, Price < 120 And IsBlank(_count)))", 0, "With({r:t1}, (Filter(r, (And(LtDecimals(new_price,120), (IsBlank(_count)))))))", false, false, "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r:t1}, Filter(r, Price < 120 And IsBlank(_count)))", 0, "With({r:t1}, (Filter(r, (And(LtNumbers(new_price,120), (IsBlank(_count)))))))", true, true, "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r:t1}, Filter(r, Price < 120 And IsBlank(_count)))", 0, "With({r:t1}, (Filter(r, (And(LtNumbers(new_price,Float(120)), (IsBlank(_count)))))))", true, false, "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r:t1}, Filter(r, Price < 120 And IsBlank(_count)))", 0, "With({r:t1}, (Filter(r, (And(LtNumbers(Value(new_price),120), (IsBlank(_count)))))))", false, true, "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Filter nested in FirstN function. Only FirstN is delegated.
        [InlineData("Filter(FirstN(t1, 100), Price = 100)", 1, "Filter(__retrieveMultiple(t1, __noFilter(), Float(100)), (EqDecimals(new_price,100)))", false, false, "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(FirstN(t1, 100), Price = 100)", 1, "Filter(__retrieveMultiple(t1, __noFilter(), 100), (EqNumbers(new_price,100)))", true, true, "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(FirstN(t1, 100), Price = 100)", 1, "Filter(__retrieveMultiple(t1, __noFilter(), Float(100)), (EqNumbers(new_price,Float(100))))", true, false, "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(FirstN(t1, 100), Price = 100)", 1, "Filter(__retrieveMultiple(t1, __noFilter(), 100), (EqNumbers(Value(new_price),100)))", false, true, "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData("With({r:t1}, Filter(FirstN(r, 100), Price = 100))", 1, "With({r:t1}, (Filter(__retrieveMultiple(t1, __noFilter(), Float(100)), (EqDecimals(new_price,100)))))", false, false, "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r:t1}, Filter(FirstN(r, 100), Price = 100))", 1, "With({r:t1}, (Filter(__retrieveMultiple(t1, __noFilter(), 100), (EqNumbers(new_price,100)))))", true, true, "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r:t1}, Filter(FirstN(r, 100), Price = 100))", 1, "With({r:t1}, (Filter(__retrieveMultiple(t1, __noFilter(), Float(100)), (EqNumbers(new_price,Float(100))))))", true, false, "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r:t1}, Filter(FirstN(r, 100), Price = 100))", 1, "With({r:t1}, (Filter(__retrieveMultiple(t1, __noFilter(), 100), (EqNumbers(Value(new_price),100)))))", false, true, "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData("With({r : t1}, Filter(r, Price < 120))", 3, "With({r:t1}, (__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)))", false, false)]
        [InlineData("With({r : t1}, Filter(r, Price < 120))", 3, "With({r:t1}, (__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)))", true, true)]
        [InlineData("With({r : t1}, Filter(r, Price < 120))", 3, "With({r:t1}, (__retrieveMultiple(t1, __lt(t1, new_price, Float(120)), 999)))", true, false)]
        [InlineData("With({r : t1}, Filter(r, Price < 120))", 3, "With({r:t1}, (__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)))", false, true)]

        // Comparing fields can't be delegated.
        [InlineData("Filter(t1, Price < Old_Price)", 2, "Filter(t1, (LtDecimals(new_price,old_price)))", false, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(t1, Price < Old_Price)", 2, "Filter(t1, (LtNumbers(new_price,old_price)))", true, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(t1, Price < Old_Price)", 2, "Filter(t1, (LtNumbers(new_price,old_price)))", true, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(t1, Price < Old_Price)", 2, "Filter(t1, (LtDecimals(new_price,old_price)))", false, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData("With({r:t1}, Filter(r, Price < Old_Price))", 2, "With({r:t1}, (Filter(r, (LtDecimals(new_price,old_price)))))", false, false, "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r:t1}, Filter(r, Price < Old_Price))", 2, "With({r:t1}, (Filter(r, (LtNumbers(new_price,old_price)))))", true, true, "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r:t1}, Filter(r, Price < Old_Price))", 2, "With({r:t1}, (Filter(r, (LtNumbers(new_price,old_price)))))", true, false, "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r:t1}, Filter(r, Price < Old_Price))", 2, "With({r:t1}, (Filter(r, (LtDecimals(new_price,old_price)))))", false, true, "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows.")]


        // Not All binary op are supported.
        [InlineData("Filter(t1, \"row1\" in Name)", 1, "Filter(t1, (InText(row1,new_name)))", false, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(t1, \"row1\" in Name)", 1, "Filter(t1, (InText(row1,new_name)))", true, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(t1, \"row1\" in Name)", 1, "Filter(t1, (InText(row1,new_name)))", true, false, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("Filter(t1, \"row1\" in Name)", 1, "Filter(t1, (InText(row1,new_name)))", false, true, "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        [InlineData("With({r:t1}, Filter(r, \"row1\" in Name))", 1, "With({r:t1}, (Filter(r, (InText(row1,new_name)))))", false, false, "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r:t1}, Filter(r, \"row1\" in Name))", 1, "With({r:t1}, (Filter(r, (InText(row1,new_name)))))", true, true, "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r:t1}, Filter(r, \"row1\" in Name))", 1, "With({r:t1}, (Filter(r, (InText(row1,new_name)))))", true, false, "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r:t1}, Filter(r, \"row1\" in Name))", 1, "With({r:t1}, (Filter(r, (InText(row1,new_name)))))", false, true, "Warning 8-10: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Error handling
        [InlineData("Filter(t1, Price < 1/0)", -1, "__retrieveMultiple(t1, __lt(t1, new_price, DivDecimals(1,0)), 999)", false, false)]
        [InlineData("Filter(t1, Price < 1/0)", -1, "__retrieveMultiple(t1, __lt(t1, new_price, DivNumbers(1,0)), 999)", true, true)]
        [InlineData("Filter(t1, Price < 1/0)", -1, "__retrieveMultiple(t1, __lt(t1, new_price, Float(DivDecimals(1,0))), 999)", true, false)]
        [InlineData("Filter(t1, Price < 1/0)", -1, "__retrieveMultiple(t1, __lt(t1, new_price, DivNumbers(1,0)), 999)", false, true)]
        // Blank handling
        [InlineData("Filter(t1, Price < Blank())", 1, "__retrieveMultiple(t1, __lt(t1, new_price, Blank()), 999)", false, false)]
        [InlineData("Filter(t1, Price < Blank())", 1, "__retrieveMultiple(t1, __lt(t1, new_price, Blank()), 999)", true, true)]
        [InlineData("Filter(t1, Price < Blank())", 1, "__retrieveMultiple(t1, __lt(t1, new_price, Blank()), 999)", true, false)]
        [InlineData("Filter(t1, Price < Blank())", 1, "__retrieveMultiple(t1, __lt(t1, new_price, Blank()), 999)", false, true)]

        [InlineData("Filter(t1, Price > Blank())", 2, "__retrieveMultiple(t1, __gt(t1, new_price, Blank()), 999)", false, false)]
        [InlineData("Filter(t1, Price > Blank())", 2, "__retrieveMultiple(t1, __gt(t1, new_price, Blank()), 999)", true, true)]
        [InlineData("Filter(t1, Price > Blank())", 2, "__retrieveMultiple(t1, __gt(t1, new_price, Blank()), 999)", true, false)]
        [InlineData("Filter(t1, Price > Blank())", 2, "__retrieveMultiple(t1, __gt(t1, new_price, Blank()), 999)", false, true)]

        [InlineData("Filter(t1, Price = Blank())", 0, "__retrieveMultiple(t1, __eq(t1, new_price, Blank()), 999)", false, false)]
        [InlineData("Filter(t1, Price = Blank())", 0, "__retrieveMultiple(t1, __eq(t1, new_price, Blank()), 999)", true, true)]
        [InlineData("Filter(t1, Price = Blank())", 0, "__retrieveMultiple(t1, __eq(t1, new_price, Blank()), 999)", true, false)]
        [InlineData("Filter(t1, Price = Blank())", 0, "__retrieveMultiple(t1, __eq(t1, new_price, Blank()), 999)", false, true)]

        [InlineData("Filter(t1, Price <> Blank())", 3, "__retrieveMultiple(t1, __neq(t1, new_price, Blank()), 999)", false, false)]
        [InlineData("Filter(t1, Price <> Blank())", 3, "__retrieveMultiple(t1, __neq(t1, new_price, Blank()), 999)", true, true)]
        [InlineData("Filter(t1, Price <> Blank())", 3, "__retrieveMultiple(t1, __neq(t1, new_price, Blank()), 999)", true, false)]
        [InlineData("Filter(t1, Price <> Blank())", 3, "__retrieveMultiple(t1, __neq(t1, new_price, Blank()), 999)", false, true)]

        [InlineData("Filter(t1, Rating <> 'Rating (Locals)'.Hot)", 1, "__retrieveMultiple(t1, __neq(t1, rating, (local_rating_optionSet).1), 999)", false, false)]
        [InlineData("Filter(t1, Rating <> 'Rating (Locals)'.Hot)", 1, "__retrieveMultiple(t1, __neq(t1, rating, (local_rating_optionSet).1), 999)", true, true)]
        [InlineData("Filter(t1, Rating <> 'Rating (Locals)'.Hot)", 1, "__retrieveMultiple(t1, __neq(t1, rating, (local_rating_optionSet).1), 999)", true, false)]
        [InlineData("Filter(t1, Rating <> 'Rating (Locals)'.Hot)", 1, "__retrieveMultiple(t1, __neq(t1, rating, (local_rating_optionSet).1), 999)", false, true)]

        [InlineData("Filter(t1, Rating = 'Rating (Locals)'.Hot)", 2, "__retrieveMultiple(t1, __eq(t1, rating, (local_rating_optionSet).1), 999)", false, false)]
        [InlineData("Filter(t1, Rating = 'Rating (Locals)'.Hot)", 2, "__retrieveMultiple(t1, __eq(t1, rating, (local_rating_optionSet).1), 999)", true, true)]
        [InlineData("Filter(t1, Rating = 'Rating (Locals)'.Hot)", 2, "__retrieveMultiple(t1, __eq(t1, rating, (local_rating_optionSet).1), 999)", true, false)]
        [InlineData("Filter(t1, Rating = 'Rating (Locals)'.Hot)", 2, "__retrieveMultiple(t1, __eq(t1, rating, (local_rating_optionSet).1), 999)", false, true)]

        [InlineData("Filter(t1, Currency > 0)", 1, "__retrieveMultiple(t1, __gt(t1, new_currency, 0), 999)", false, false)]
        [InlineData("Filter(t1, Currency > 0)", 1, "__retrieveMultiple(t1, __gt(t1, new_currency, 0), 999)", true, true)]
        [InlineData("Filter(t1, Currency > 0)", 1, "__retrieveMultiple(t1, __gt(t1, new_currency, Float(0)), 999)", true, false)]
        [InlineData("Filter(t1, Currency > 0)", 1, "__retrieveMultiple(t1, __gt(t1, new_currency, 0), 999)", false, true)]

        [InlineData("With({r:t1}, Filter(r, Currency > 0))", 1, "With({r:t1}, (__retrieveMultiple(t1, __gt(t1, new_currency, 0), 999)))", false, false)]
        [InlineData("With({r:t1}, Filter(r, Currency > 0))", 1, "With({r:t1}, (__retrieveMultiple(t1, __gt(t1, new_currency, 0), 999)))", true, true)]
        [InlineData("With({r:t1}, Filter(r, Currency > 0))", 1, "With({r:t1}, (__retrieveMultiple(t1, __gt(t1, new_currency, Float(0)), 999)))", true, false)]
        [InlineData("With({r:t1}, Filter(r, Currency > 0))", 1, "With({r:t1}, (__retrieveMultiple(t1, __gt(t1, new_currency, 0), 999)))", false, true)]

        [InlineData("Filter(t1, virtual.'Virtual Data' = 10)", 1, "__retrieveMultiple(t1, __eq(t1, vdata, 10, Table({Value:virtual})), 999)", false, false)]
        [InlineData("Filter(t1, virtual.'Virtual Data' = 10)", 1, "__retrieveMultiple(t1, __eq(t1, vdata, 10, Table({Value:virtual})), 999)", true, true)]
        [InlineData("Filter(t1, virtual.'Virtual Data' = 10)", 1, "__retrieveMultiple(t1, __eq(t1, vdata, Float(10), Table({Value:virtual})), 999)", true, false)]
        [InlineData("Filter(t1, virtual.'Virtual Data' = 10)", 1, "__retrieveMultiple(t1, __eq(t1, vdata, 10, Table({Value:virtual})), 999)", false, true)]

        [InlineData("Filter(t1, virtual.'Virtual Data' <> 10)", 2, "__retrieveMultiple(t1, __neq(t1, vdata, 10, Table({Value:virtual})), 999)", false, false)]
        [InlineData("Filter(t1, virtual.'Virtual Data' <> 10)", 2, "__retrieveMultiple(t1, __neq(t1, vdata, 10, Table({Value:virtual})), 999)", true, true)]
        [InlineData("Filter(t1, virtual.'Virtual Data' <> 10)", 2, "__retrieveMultiple(t1, __neq(t1, vdata, Float(10), Table({Value:virtual})), 999)", true, false)]
        [InlineData("Filter(t1, virtual.'Virtual Data' <> 10)", 2, "__retrieveMultiple(t1, __neq(t1, vdata, 10, Table({Value:virtual})), 999)", false, true)]

        [InlineData("Filter(t1, virtual.'Virtual Data' <> 10 And Price <> 10)", 1, "__retrieveMultiple(t1, __and(__neq(t1, vdata, 10, Table({Value:virtual})), __neq(t1, new_price, 10)), 999)", false, false)]
        [InlineData("Filter(t1, virtual.'Virtual Data' <> 10 And Price <> 10)", 1, "__retrieveMultiple(t1, __and(__neq(t1, vdata, 10, Table({Value:virtual})), __neq(t1, new_price, 10)), 999)", true, true)]
        [InlineData("Filter(t1, virtual.'Virtual Data' <> 10 And Price <> 10)", 1, "__retrieveMultiple(t1, __and(__neq(t1, vdata, Float(10), Table({Value:virtual})), __neq(t1, new_price, Float(10))), 999)", true, false)]
        [InlineData("Filter(t1, virtual.'Virtual Data' <> 10 And Price <> 10)", 1, "__retrieveMultiple(t1, __and(__neq(t1, vdata, 10, Table({Value:virtual})), __neq(t1, new_price, 10)), 999)", false, true)]

        [InlineData("Filter(t1, IsBlank(virtual.'Virtual Data'))", 2, "__retrieveMultiple(t1, __eq(t1, vdata, Blank(), Table({Value:virtual})), 999)", false, false)]
        [InlineData("Filter(t1, IsBlank(virtual.'Virtual Data'))", 2, "__retrieveMultiple(t1, __eq(t1, vdata, Blank(), Table({Value:virtual})), 999)", true, true)]
        [InlineData("Filter(t1, IsBlank(virtual.'Virtual Data'))", 2, "__retrieveMultiple(t1, __eq(t1, vdata, Blank(), Table({Value:virtual})), 999)", true, false)]
        [InlineData("Filter(t1, IsBlank(virtual.'Virtual Data'))", 2, "__retrieveMultiple(t1, __eq(t1, vdata, Blank(), Table({Value:virtual})), 999)", false, true)]

        [InlineData("Filter(t1, AsType(PolymorphicLookup, t2).Data = 200)", 1, "__retrieveMultiple(t1, __eq(t1, data, 200, Table({Value:new_polyfield_t2_t1})), 999)", true, true)]
        [InlineData("Filter(t1, AsType(PolymorphicLookup, t2).Data = 200)", 1, "__retrieveMultiple(t1, __eq(t1, data, 200, Table({Value:new_polyfield_t2_t1})), 999)", false, false)]
        [InlineData("Filter(t1, AsType(PolymorphicLookup, t2).Data = 200)", 1, "__retrieveMultiple(t1, __eq(t1, data, Float(200), Table({Value:new_polyfield_t2_t1})), 999)", true, false)]
        [InlineData("Filter(t1, AsType(PolymorphicLookup, t2).Data = 200)", 1, "__retrieveMultiple(t1, __eq(t1, data, 200, Table({Value:new_polyfield_t2_t1})), 999)", false, true)]

        [InlineData("Filter(t1, AsType(PolymorphicLookup, t2).Data <> 200)", 2, "__retrieveMultiple(t1, __neq(t1, data, 200, Table({Value:new_polyfield_t2_t1})), 999)", true, true)]
        [InlineData("Filter(t1, AsType(PolymorphicLookup, t2).Data <> 200)", 2, "__retrieveMultiple(t1, __neq(t1, data, 200, Table({Value:new_polyfield_t2_t1})), 999)", false, false)]
        [InlineData("Filter(t1, AsType(PolymorphicLookup, t2).Data <> 200)", 2, "__retrieveMultiple(t1, __neq(t1, data, Float(200), Table({Value:new_polyfield_t2_t1})), 999)", true, false)]
        [InlineData("Filter(t1, AsType(PolymorphicLookup, t2).Data <> 200)", 2, "__retrieveMultiple(t1, __neq(t1, data, 200, Table({Value:new_polyfield_t2_t1})), 999)", false, true)]
        public void FilterDelegation(string expr, int expectedRows, string expectedIr, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: cdsNumberIsFloat);

            var tableT1 = dv.AddTable(displayName, logicalName);
            var tableT2 = dv.AddTable("t2", "remote");

            var opts = parserNumberIsFloatOption ?
                PluginExecutionTests._parserAllowSideEffects_NumberIsFloat :
                PluginExecutionTests._parserAllowSideEffects;

            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_count", FormulaValue.New(100m));

            var inputs = TransformForWithFunction(expr, expectedIr, expectedWarnings?.Count() ?? 0);

            foreach (var input in inputs)
            {
                expr = input.Item1;
                expectedIr = input.Item2;

                var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
                Assert.True(check.IsSuccess);

                // compare IR to verify the delegations are happening exactly where we expect 
                var irNode = check.ApplyIR();
                var actualIr = check.GetCompactIRString();
                Assert.Equal(expectedIr, actualIr);

                // Validate delegation warnings.
                // error.ToString() will capture warning status, message, and source span. 
                var errors = check.ApplyErrors();

                var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

                Assert.Equal(expectedWarnings.Length, errorList.Length);
                for (int i = 0; i < errorList.Length; i++)
                {
                    Assert.Equal(expectedWarnings[i], errorList[i]);
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

        // Table 't1' has
        // 1st item with
        // Price = 100, Old_Price = 200,  Date = Date(2023, 6, 1), DateTime = DateTime(2023, 6, 1, 12, 0, 0)
        // 2nd item with
        // Price = 10
        // 3rd item with
        // Price = -10

        [Theory]

        //Basic case 
        [InlineData("First(t1).Price", 100.0, "(__retrieveSingle(t1, __noFilter())).new_price", false, false)]
        [InlineData("First(t1).Price", 100.0, "(__retrieveSingle(t1, __noFilter())).new_price", true, true)]
        [InlineData("First(t1).Price", 100.0, "(__retrieveSingle(t1, __noFilter())).new_price", true, false)]
        [InlineData("First(t1).Price", 100.0, "(__retrieveSingle(t1, __noFilter())).new_price", false, true)]

        // Filter inside FirstN, both can be combined *(vice versa isn't true)*
        [InlineData("First(Filter(t1, Price < 100)).Price", 10.0, "(__retrieveSingle(t1, __lt(t1, new_price, 100))).new_price", false, false)]
        [InlineData("First(Filter(t1, Price < 100)).Price", 10.0, "(__retrieveSingle(t1, __lt(t1, new_price, 100))).new_price", true, true)]
        [InlineData("First(Filter(t1, Price < 100)).Price", 10.0, "(__retrieveSingle(t1, __lt(t1, new_price, Float(100)))).new_price", true, false)]
        [InlineData("First(Filter(t1, Price < 100)).Price", 10.0, "(__retrieveSingle(t1, __lt(t1, new_price, 100))).new_price", false, true)]

        [InlineData("First(FirstN(t1, 2)).Price", 100.0, "(__retrieveSingle(t1, __noFilter())).new_price", false, false)]
        [InlineData("First(FirstN(t1, 2)).Price", 100.0, "(__retrieveSingle(t1, __noFilter())).new_price", true, true)]
        [InlineData("First(FirstN(t1, 2)).Price", 100.0, "(__retrieveSingle(t1, __noFilter())).new_price", true, false)]
        [InlineData("First(FirstN(t1, 2)).Price", 100.0, "(__retrieveSingle(t1, __noFilter())).new_price", false, true)]
        public void FirstDelegation(string expr, object expected, string expectedIr, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: cdsNumberIsFloat);

            var tableT1 = dv.AddTable(displayName, logicalName);

            var opts = parserNumberIsFloatOption ?
                PluginExecutionTests._parserAllowSideEffects_NumberIsFloat :
                PluginExecutionTests._parserAllowSideEffects;

            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_count", FormulaValue.New(100));

            var inputs = TransformForWithFunction(expr, expectedIr, expectedWarnings?.Count() ?? 0);

            foreach (var input in inputs)
            {
                expr = input.Item1;
                expectedIr = input.Item2;

                var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
                Assert.True(check.IsSuccess);

                // compare IR to verify the delegations are happening exactly where we expect 
                var irNode = check.ApplyIR();
                var actualIr = check.GetCompactIRString();
                Assert.Equal(expectedIr, actualIr);

                // Validate delegation warnings.
                // error.ToString() will capture warning status, message, and source span. 
                var errors = check.ApplyErrors();

                var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

                Assert.Equal(expectedWarnings.Length, errorList.Length);
                for (int i = 0; i < errorList.Length; i++)
                {
                    Assert.Equal(expectedWarnings[i], errorList[i]);
                }

                var run = check.GetEvaluator();

                var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

                if ((cdsNumberIsFloat && parserNumberIsFloatOption) ||
                    (cdsNumberIsFloat && !parserNumberIsFloatOption))
                {
                    Assert.Equal(expected, result.ToObject());
                }
                else
                {
                    Assert.Equal(new decimal((double)expected), result.ToObject());
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
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels();
            var tableT1 = dv.AddTable(displayName, logicalName);

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
            for (int i = 0; i < errorList.Length; i++)
            {
                Assert.Equal(expectedWarnings[i], errorList[i]);
            }
        }

        [Theory]

        //Inner first which can still be delegated.
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price >90)}, Filter(r2, Price = First(Filter(r1, Price > 90)).Price)))",
           1,
           "With({r1:__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)}, (With({r2:__retrieveMultiple(t1, __gt(t1, new_price, 90), 999)}, (__retrieveMultiple(t1, __and(__gt(t1, new_price, 90), __eq(t1, new_price, (__retrieveSingle(t1, __and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)))).new_price)), 999)))))",
           false,
           false)]
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price >90)}, Filter(r2, Price = First(Filter(r1, Price > 90)).Price)))",
           1,
           "With({r1:__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)}, (With({r2:__retrieveMultiple(t1, __gt(t1, new_price, 90), 999)}, (__retrieveMultiple(t1, __and(__gt(t1, new_price, 90), __eq(t1, new_price, (__retrieveSingle(t1, __and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)))).new_price)), 999)))))",
           true,
           true)]
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price >90)}, Filter(r2, Price = First(Filter(r1, Price > 90)).Price)))",
           1,
           "With({r1:__retrieveMultiple(t1, __lt(t1, new_price, Float(120)), 999)}, (With({r2:__retrieveMultiple(t1, __gt(t1, new_price, Float(90)), 999)}, (__retrieveMultiple(t1, __and(__gt(t1, new_price, Float(90)), __eq(t1, new_price, (__retrieveSingle(t1, __and(__lt(t1, new_price, Float(120)), __gt(t1, new_price, Float(90))))).new_price)), 999)))))",
           true,
           false)]
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price >90)}, Filter(r2, Price = First(Filter(r1, Price > 90)).Price)))",
           1,
           "With({r1:__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)}, (With({r2:__retrieveMultiple(t1, __gt(t1, new_price, 90), 999)}, (__retrieveMultiple(t1, __and(__gt(t1, new_price, 90), __eq(t1, new_price, (__retrieveSingle(t1, __and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)))).new_price)), 999)))))",
           false,
           true)]

        [InlineData("With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90))",
            1,
            "With({r:__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)}, (__retrieveMultiple(t1, __and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)), 999)))",
            false,
            false)]
        [InlineData("With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90))",
            1,
            "With({r:__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)}, (__retrieveMultiple(t1, __and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)), 999)))",
            true,
            true)]
        [InlineData("With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90))",
            1,
            "With({r:__retrieveMultiple(t1, __lt(t1, new_price, Float(120)), 999)}, (__retrieveMultiple(t1, __and(__lt(t1, new_price, Float(120)), __gt(t1, new_price, Float(90))), 999)))",
            true,
            false)]
        [InlineData("With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90))",
            1,
            "With({r:__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)}, (__retrieveMultiple(t1, __and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)), 999)))",
            false,
            true)]

        [InlineData("With({r: t1}, With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90)))",
            1,
            "With({r:t1}, (With({r:__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)}, (__retrieveMultiple(t1, __and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)), 999)))))",
            false,
            false)]
        [InlineData("With({r: t1}, With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90)))",
            1,
            "With({r:t1}, (With({r:__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)}, (__retrieveMultiple(t1, __and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)), 999)))))",
            true,
            true)]
        [InlineData("With({r: t1}, With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90)))",
            1,
            "With({r:t1}, (With({r:__retrieveMultiple(t1, __lt(t1, new_price, Float(120)), 999)}, (__retrieveMultiple(t1, __and(__lt(t1, new_price, Float(120)), __gt(t1, new_price, Float(90))), 999)))))",
            true,
            false)]
        [InlineData("With({r: t1}, With({r : Filter(t1, Price < 120)}, Filter(r, Price > 90)))",
            1,
            "With({r:t1}, (With({r:__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)}, (__retrieveMultiple(t1, __and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)), 999)))))",
            false,
            true)]

        [InlineData("With({r : Filter(t1, Price < 120)}, With({r: t1}, Filter(r, Price > 90)))",
            1,
            "With({r:__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)}, (With({r:t1}, (__retrieveMultiple(t1, __gt(t1, new_price, 90), 999)))))",
            false,
            false)]
        [InlineData("With({r : Filter(t1, Price < 120)}, With({r: t1}, Filter(r, Price > 90)))",
            1,
            "With({r:__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)}, (With({r:t1}, (__retrieveMultiple(t1, __gt(t1, new_price, 90), 999)))))",
            true,
            true)]
        [InlineData("With({r : Filter(t1, Price < 120)}, With({r: t1}, Filter(r, Price > 90)))",
            1,
            "With({r:__retrieveMultiple(t1, __lt(t1, new_price, Float(120)), 999)}, (With({r:t1}, (__retrieveMultiple(t1, __gt(t1, new_price, Float(90)), 999)))))",
            true,
            false)]
        [InlineData("With({r : Filter(t1, Price < 120)}, With({r: t1}, Filter(r, Price > 90)))",
            1,
            "With({r:__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)}, (With({r:t1}, (__retrieveMultiple(t1, __gt(t1, new_price, 90), 999)))))",
            false,
            true)]

        // Second Scoped variable uses the first scoped variable. Still the second scoped variable is delegated.
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(r1, Price > 90)}, Filter(r2, Price = 100)))",
            1,
            "With({r1:__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)}, (With({r2:__retrieveMultiple(t1, __and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)), 999)}, (__retrieveMultiple(t1, __and(__and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)), __eq(t1, new_price, 100)), 999)))))",
            false,
            false)]
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(r1, Price > 90)}, Filter(r2, Price = 100)))",
            1,
            "With({r1:__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)}, (With({r2:__retrieveMultiple(t1, __and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)), 999)}, (__retrieveMultiple(t1, __and(__and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)), __eq(t1, new_price, 100)), 999)))))",
            true,
            true)]
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(r1, Price > 90)}, Filter(r2, Price = 100)))",
            1,
            "With({r1:__retrieveMultiple(t1, __lt(t1, new_price, Float(120)), 999)}, (With({r2:__retrieveMultiple(t1, __and(__lt(t1, new_price, Float(120)), __gt(t1, new_price, Float(90))), 999)}, (__retrieveMultiple(t1, __and(__and(__lt(t1, new_price, Float(120)), __gt(t1, new_price, Float(90))), __eq(t1, new_price, Float(100))), 999)))))",
            true,
            false)]
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(r1, Price > 90)}, Filter(r2, Price = 100)))",
            1,
            "With({r1:__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)}, (With({r2:__retrieveMultiple(t1, __and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)), 999)}, (__retrieveMultiple(t1, __and(__and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)), __eq(t1, new_price, 100)), 999)))))",
            false,
            true)]

        // inner lookup has filter and that should delegate.
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price > 90)}, Filter(t1, Price = LookUp(r1, Price = 100).Price)))",
            1,
            "With({r1:__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)}, (With({r2:__retrieveMultiple(t1, __gt(t1, new_price, 90), 999)}, (__retrieveMultiple(t1, __eq(t1, new_price, (LookUp(__retrieveMultiple(t1, __lt(t1, new_price, 120), 999), (EqDecimals(new_price,100)))).new_price), 999)))))",
            false,
            false,
            "Warning 18-20: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price > 90)}, Filter(t1, Price = LookUp(r1, Price = 100).Price)))",
            1,
            "With({r1:__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)}, (With({r2:__retrieveMultiple(t1, __gt(t1, new_price, 90), 999)}, (__retrieveMultiple(t1, __eq(t1, new_price, (LookUp(__retrieveMultiple(t1, __lt(t1, new_price, 120), 999), (EqNumbers(new_price,100)))).new_price), 999)))))",
            true,
            true,
            "Warning 18-20: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price > 90)}, Filter(t1, Price = LookUp(r1, Price = 100).Price)))",
            1,
            "With({r1:__retrieveMultiple(t1, __lt(t1, new_price, Float(120)), 999)}, (With({r2:__retrieveMultiple(t1, __gt(t1, new_price, Float(90)), 999)}, (__retrieveMultiple(t1, __eq(t1, new_price, (LookUp(__retrieveMultiple(t1, __lt(t1, new_price, Float(120)), 999), (EqNumbers(new_price,Float(100))))).new_price), 999)))))",
            true,
            false,
            "Warning 18-20: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData("With({r1 : Filter(t1, Price < 120)}, With({r2: Filter(t1, Price > 90)}, Filter(t1, Price = LookUp(r1, Price = 100).Price)))",
            1,
            "With({r1:__retrieveMultiple(t1, __lt(t1, new_price, 120), 999)}, (With({r2:__retrieveMultiple(t1, __gt(t1, new_price, 90), 999)}, (__retrieveMultiple(t1, __eq(t1, new_price, (LookUp(__retrieveMultiple(t1, __lt(t1, new_price, 120), 999), (EqNumbers(Value(new_price),100)))).new_price), 999)))))",
            false,
            true,
            "Warning 18-20: This operation on table 'local' may not work if it has more than 999 rows.")]

        public void WithDelegation(string expr, int expectedRows, string expectedIr, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: cdsNumberIsFloat);

            var tableT1 = dv.AddTable(displayName, logicalName);
            var tableT2 = dv.AddTable("t2", "remote");

            var opts = parserNumberIsFloatOption ?
                PluginExecutionTests._parserAllowSideEffects_NumberIsFloat :
                PluginExecutionTests._parserAllowSideEffects;

            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_count", FormulaValue.New(100m));

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.True(check.IsSuccess);

            // compare IR to verify the delegations are happening exactly where we expect 
            var irNode = check.ApplyIR();
            var actualIr = check.GetCompactIRString();
            Assert.Equal(expectedIr, actualIr);

            // Validate delegation warnings.
            // error.ToString() will capture warning status, message, and source span. 
            var errors = check.ApplyErrors();

            var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

            Assert.Equal(expectedWarnings.Length, errorList.Length);
            for (int i = 0; i < errorList.Length; i++)
            {
                Assert.Equal(expectedWarnings[i], errorList[i]);
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

        [Theory]

        // do not give warning on tabular function, where source is delegable.
        [InlineData("Concat(Filter(t1, Price < 120), Price & \",\")",
           "100,10,-10,",
           "Concat(__retrieveMultiple(t1, __lt(t1, new_price, 120), 999), (Concatenate(DecimalToText(new_price), ,)))",
           false,
           false)]

        [InlineData("Concat(FirstN(t1, 2), Price & \",\")",
           "100,10,",
           "Concat(__retrieveMultiple(t1, __noFilter(), Float(2)), (Concatenate(DecimalToText(new_price), ,)))",
           false,
           false)]

        [InlineData("Concat(ShowColumns(t1, 'new_price'), new_price & \",\")",
           "100,10,-10,",
           "Concat(__retrieveMultiple(t1, __noFilter(), 999, new_price), (Concatenate(DecimalToText(new_price), ,)))",
           false,
           false)]

        // Give warning when source is entire table.
        [InlineData("Concat(t1, Price & \",\")",
           "100,10,-10,",
           "Concat(t1, (Concatenate(DecimalToText(new_price), ,)))",
           false,
           false,
           "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        public void FunctionPartialDelegation(string expr, object expected, string expectedIr, bool cdsNumberIsFloat, bool parserNumberIsFloatOption, params string[] expectedWarnings)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: cdsNumberIsFloat);

            var tableT1 = dv.AddTable(displayName, logicalName);
            var tableT2 = dv.AddTable("t2", "remote");

            var opts = parserNumberIsFloatOption ?
                PluginExecutionTests._parserAllowSideEffects_NumberIsFloat :
                PluginExecutionTests._parserAllowSideEffects;

            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_count", FormulaValue.New(100m));

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.True(check.IsSuccess);

            // compare IR to verify the delegations are happening exactly where we expect 
            var irNode = check.ApplyIR();
            var actualIr = check.GetCompactIRString();
            Assert.Equal(expectedIr, actualIr);

            // Validate delegation warnings.
            // error.ToString() will capture warning status, message, and source span. 
            var errors = check.ApplyErrors();

            var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

            Assert.Equal(expectedWarnings.Length, errorList.Length);
            for (int i = 0; i < errorList.Length; i++)
            {
                Assert.Equal(expectedWarnings[i], errorList[i]);
            }

            var run = check.GetEvaluator();

            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.Equal(expected, result.ToObject());
        }

        [Theory]

        [InlineData("FirstN(ShowColumns(t1, 'new_price', 'old_price'), 1)",
            2,
            "__retrieveMultiple(t1, __noFilter(), Float(1), new_price, old_price)",
            true)]

        [InlineData("ShowColumns(FirstN(t1, 1), 'new_price', 'old_price')",
            2,
            "__retrieveMultiple(t1, __noFilter(), Float(1), new_price, old_price)",
            true)]

        [InlineData("FirstN(Filter(ShowColumns(t1, 'new_price', 'old_price'), new_price < 120), 1)",
            2,
            "__retrieveMultiple(t1, __lt(t1, new_price, 120), Float(1), new_price, old_price)",
            true)]

        [InlineData("First(ShowColumns(t1, 'new_price', 'old_price'))",
            2,
            "__retrieveSingle(t1, __noFilter(), new_price, old_price)",
            true)]

        [InlineData("First(Filter(ShowColumns(t1, 'new_price', 'old_price'), new_price < 120))",
            2,
            "__retrieveSingle(t1, __lt(t1, new_price, 120), new_price, old_price)",
            true)]

        [InlineData("LookUp(ShowColumns(t1, 'new_price', 'old_price'), new_price < 120)",
            2,
            "__retrieveSingle(t1, __lt(t1, new_price, 120), new_price, old_price)",
            true)]

        [InlineData("ShowColumns(Filter(t1, Price < 120), 'new_price')",
            1,
            "__retrieveMultiple(t1, __lt(t1, new_price, 120), 999, new_price)",
            true)]

        [InlineData("ShowColumns(Filter(t1, Price < 120), 'new_price', 'old_price')",
            2,
            "__retrieveMultiple(t1, __lt(t1, new_price, 120), 999, new_price, old_price)",
            true)]

        [InlineData("Filter(ShowColumns(t1, 'new_price', 'old_price'), new_price < 120)",
            2,
            "__retrieveMultiple(t1, __lt(t1, new_price, 120), 999, new_price, old_price)",
            true)]

        // This is not delegated, but doesn't impact perf.
        [InlineData("ShowColumns(LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\")), 'new_price')",
            1,
            "ShowColumns(__retrieveGUID(t1, GUID(00000000-0000-0000-0000-000000000001)), new_price)",
            true)]

        [InlineData("LookUp(ShowColumns(t1, 'localid'), localid=GUID(\"00000000-0000-0000-0000-000000000001\"))",
            1,
            "__retrieveGUID(t1, GUID(00000000-0000-0000-0000-000000000001), localid)",
            true)]

        [InlineData("First(ShowColumns(ShowColumns(t1, 'localid'), 'localid'))",
            1,
            "__retrieveSingle(t1, __noFilter(), localid)",
            true)]

        [InlineData("First(ShowColumns(ShowColumns(t1, 'localid', 'new_price'), 'localid'))",
            1,
            "__retrieveSingle(t1, __noFilter(), localid)",
            true)]

        [InlineData("First(ShowColumns(ShowColumns(t1, 'localid'), 'new_price'))",
            1,
            "(__retrieveGUID(t1, GUID(00000000-0000-0000-0000-000000000001), localid)).localid",
            false)]
        public void ShowColumnDelegation(string expr, int expectedCount, string expectedIr, bool isCheckSuccess, params string[] expectedWarnings)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: false);

            var tableT1 = dv.AddTable(displayName, logicalName);
            var tableT2 = dv.AddTable("t2", "remote");

            var opts = PluginExecutionTests._parserAllowSideEffects;

            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_count", FormulaValue.New(100m));

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);

            if (!isCheckSuccess)
            {
                Assert.False(check.IsSuccess);
                return;
            }

            Assert.True(check.IsSuccess);

            // compare IR to verify the delegations are happening exactly where we expect 
            var irNode = check.ApplyIR();
            var actualIr = check.GetCompactIRString();
            Assert.Equal(expectedIr, actualIr);

            // Validate delegation warnings.
            // error.ToString() will capture warning status, message, and source span. 
            var errors = check.ApplyErrors();

            var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

            Assert.Equal(expectedWarnings.Length, errorList.Length);
            for (int i = 0; i < errorList.Length; i++)
            {
                Assert.Equal(expectedWarnings[i], errorList[i]);
            }

            var run = check.GetEvaluator();

            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            int columnCount = 0;
            if (result is TableValue tv)
            {
                columnCount = tv.Type.FieldNames.Count();
            } 
            else if(result is RecordValue rv)
            {
                columnCount = rv.Type.FieldNames.Count();
            }

            Assert.Equal(expectedCount, columnCount);
        }

        [Theory]

        [InlineData("IsBlank(FirstN(t1, 1))", false, "IsBlank(__retrieveMultiple(t1, __noFilter(), Float(1)))")]
        [InlineData("IsBlank(ShowColumns(Filter(t1, Price < 120), 'new_price'))", false, "IsBlank(__retrieveMultiple(t1, __lt(t1, new_price, 120), 999, new_price))")]
        [InlineData("IsBlank(LookUp(t1, Price < -100))", true, "IsBlank(__retrieveSingle(t1, __lt(t1, new_price, NegateDecimal(100))))")]
        public void IsBlankDelegation(string expr, bool expected, string expectedIr, params string[] expectedWarnings)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: false);

            var tableT1 = dv.AddTable(displayName, logicalName);
            var tableT2 = dv.AddTable("t2", "remote");

            var opts = PluginExecutionTests._parserAllowSideEffects;

            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_count", FormulaValue.New(100m));

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);

            Assert.True(check.IsSuccess);

            // compare IR to verify the delegations are happening exactly where we expect 
            var irNode = check.ApplyIR();
            var actualIr = check.GetCompactIRString();
            Assert.Equal(expectedIr, actualIr);

            // Validate delegation warnings.
            // error.ToString() will capture warning status, message, and source span. 
            var errors = check.ApplyErrors();

            var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

            Assert.Equal(expectedWarnings.Length, errorList.Length);
            for (int i = 0; i < errorList.Length; i++)
            {
                Assert.Equal(expectedWarnings[i], errorList[i]);
            }

            var run = check.GetEvaluator();

            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.Equal(expected, ((BooleanValue)result).Value);
        }

        private static IList<(string, string)> TransformForWithFunction(string expr, string expectedIr, int warningCount)
        {
            var inputs = new List<(string, string)> { (expr, expectedIr) };

            if (warningCount > 0 || expr.StartsWith("With(") || expr.StartsWith("Collect("))
            {
                return inputs;
            }

            // transforms input expression without with, to wrap inside with.
            // e.g. LookUp(t1, Price = 255).Price -> With({r:t1}, LookUp(r, Price = 255).Price)
            var withExpr = new StringBuilder("With({r:t1},");
            withExpr.Append(expr.Replace("(t1,", "(r,"));
            withExpr.Append(")");

            // transforms expected IR without with, to wrap inside with.
            // e.g. __retrieveSingle(t1, __eq(t1, new_price, 255))).new_price -> With({r:t1}, (__retrieveSingle(t1, __eq(r, new_price, 255))).new_price)
            var withIr = new StringBuilder("With({r:t1}, (");
            withIr.Append(expectedIr);
            withIr.Append("))");

            inputs.Add((withExpr.ToString(), withIr.ToString()));

            return inputs;
        }
    }
}
