// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public partial class DelegationTests
    {
        private const string InnerJoin = @"Table(" +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000003""),other:Float(49)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000004""),other:Float(44)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000005""),other:Float(49)})";

        private const string LeftJoin = @"Table(" +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000001""),other:If(false,Float(""0""))}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000003""),other:Float(49)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000004""),other:Float(44)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000005""),other:Float(49)})";

        private const string RightJoin = @"Table(" +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000003""),other:Float(49)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000004""),other:Float(44)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000005""),other:Float(49)}," +
                                @"{localid:If(false,GUID(""00000000000000000000000000000000"")),other:If(false,Float(""0""))})";

        private const string FullJoin = @"Table(" +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000001""),other:If(false,Float(""0""))}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000003""),other:Float(49)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000004""),other:Float(44)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000005""),other:Float(49)}," +                               
                                @"{localid:If(false,GUID(""00000000000000000000000000000000"")),other:If(false,Float(""0""))})";

        private const string InnerJoin2 = @"Table(" + 
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000003""),new_name:""p1"",other2:Float(49)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000004""),new_name:""row4"",other2:Float(44)}," + 
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000005""),new_name:If(false,""""),other2:Float(49)})";

        private const string LeftJoin2 = @"Table(" +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000003""),new_name:""p1"",other2:Float(49)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000004""),new_name:""row4"",other2:Float(44)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000005""),new_name:If(false,""""),other2:Float(49)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000001""),new_name:""row1"",other2:If(false,Float(""0""))})";

        private const string RightJoin2 = @"Table(" +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000003""),new_name:""p1"",other2:Float(49)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000004""),new_name:""row4"",other2:Float(44)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000005""),new_name:If(false,""""),other2:Float(49)}," +
                                @"{localid:If(false,GUID(""00000000000000000000000000000000"")),new_name:If(false,""""),other2:If(false,Float(""0""))})";

        private const string FullJoin2 = @"Table(" +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000003""),new_name:""p1"",other2:Float(49)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000004""),new_name:""row4"",other2:Float(44)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000005""),new_name:If(false,""""),other2:Float(49)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000001""),new_name:""row1"",other2:If(false,Float(""0""))}," +
                                @"{localid:If(false,GUID(""00000000000000000000000000000000"")),new_name:If(false,""""),other2:If(false,Float(""0""))})";

        private const string InnerJoin3 = @"Table(" +
                                @"{data:Decimal(10),other:Float(49),remoteid:GUID(""00000000-0000-0000-0000-00000000000a"")}," +
                                @"{data:Decimal(10),other:Float(49),remoteid:GUID(""00000000-0000-0000-0000-00000000000a"")}," +
                                @"{data:Decimal(-10),other:Float(44),remoteid:GUID(""00000000-0000-0000-0000-00000000000b"")})";

        private const string LeftJoin3 = @"Table(" +
                                @"{data:Decimal(10),other:Float(49),remoteid:GUID(""00000000-0000-0000-0000-00000000000a"")}," +
                                @"{data:Decimal(10),other:Float(49),remoteid:GUID(""00000000-0000-0000-0000-00000000000a"")}," +
                                @"{data:Decimal(-10),other:Float(44),remoteid:GUID(""00000000-0000-0000-0000-00000000000b"")}," +
                                @"{data:Decimal(200),other:If(false,Float(""0"")),remoteid:GUID(""00000000-0000-0000-0000-000000000002"")})";

        private const string RightJoin3 = @"Table(" +
                                @"{data:Decimal(10),other:Float(49),remoteid:GUID(""00000000-0000-0000-0000-00000000000a"")}," +
                                @"{data:Decimal(10),other:Float(49),remoteid:GUID(""00000000-0000-0000-0000-00000000000a"")}," +
                                @"{data:Decimal(-10),other:Float(44),remoteid:GUID(""00000000-0000-0000-0000-00000000000b"")}," +
                                @"{data:If(false,Decimal(""0"")),other:If(false,Float(""0"")),remoteid:If(false,GUID(""00000000000000000000000000000000""))})";

        private const string FullJoin3 = @"Table(" +
                                @"{data:Decimal(10),other:Float(49),remoteid:GUID(""00000000-0000-0000-0000-00000000000a"")}," +
                                @"{data:Decimal(10),other:Float(49),remoteid:GUID(""00000000-0000-0000-0000-00000000000a"")}," +
                                @"{data:Decimal(-10),other:Float(44),remoteid:GUID(""00000000-0000-0000-0000-00000000000b"")}," +
                                @"{data:Decimal(200),other:If(false,Float(""0"")),remoteid:GUID(""00000000-0000-0000-0000-000000000002"")}," +
                                @"{data:If(false,Decimal(""0"")),other:If(false,Float(""0"")),remoteid:If(false,GUID(""00000000000000000000000000000000""))})";

        private const string InnerJoin4 = @"Table(" +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000003""),n4:""p1"",other2:Float(49)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000004""),n4:""row4"",other2:Float(44)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000005""),n4:If(false,""""),other2:Float(49)})";

        private const string InnerJoin5 = @"Table(" +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000003""),new_name:""p1"",other2:Float(49)}," +
                                @"{localid:GUID(""00000000-0000-0000-0000-000000000004""),new_name:""row4"",other2:Float(44)})";

        [Theory]
        [TestPriority(1)]

        // Can't delegate as equality comparison is not done on primary key
        [InlineData(1, "ShowColumns(Join(local, remote, LeftRecord.new_price = RightRecord.data, JoinType.Inner, RightRecord.other As other), localid, other)", 3, InnerJoin, "Warning 17-22: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(2, "ShowColumns(Join(local, remote, LeftRecord.new_price = RightRecord.data, JoinType.Left, RightRecord.other As other), localid, other)", 4, LeftJoin, "Warning 17-22: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(3, "ShowColumns(Join(local, remote, LeftRecord.new_price = RightRecord.data, JoinType.Right, RightRecord.other As other), localid, other)", 4, RightJoin, "Warning 17-22: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(4, "ShowColumns(Join(local, remote, LeftRecord.new_price = RightRecord.data, JoinType.Full, RightRecord.other As other), localid, other)", 5, FullJoin, "Warning 17-22: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Next tests (5 to 20) are based on this JOIN operation
        // Expr  => Join(Table({g:1,rtid:12},{g:3,rtid:10},{g:4,rtid:11},{g:5,rtid:10}),Table({g:2,remoteid:2},{g:10,remoteid:10,other:49},{g:11,remoteid:11,other:44}), LeftRecord.rtid = RightRecord.remoteid, JoinType.Inner, RightRecord.other As other2)         
        // Inner => Table({g:3,other2:49,rtid:10},{g:4,other2:44,rtid:11},{g:5,other2:49,rtid:10})
        // Left  => Table({g:3,other2:49,rtid:10},{g:4,other2:44,rtid:11},{g:5,other2:49,rtid:10},{g:1,other2:Blank(),rtid:12})
        // Right => Table({g:3,other2:49,rtid:10},{g:4,other2:44,rtid:11},{g:5,other2:49,rtid:10},{g:2,other2:Blank(),rtid:Blank()})
        // Full  => Table({g:3,other2:49,rtid:10},{g:4,other2:44,rtid:11},{g:5,other2:49,rtid:10},{g:1,other2:Blank(),rtid:12},{g:2,other2:Blank(),rtid:Blank()})

        [InlineData(5, "ShowColumns(Join(local, remote, LeftRecord.rtid = RightRecord.remoteid, JoinType.Inner, RightRecord.other As other2), localid, new_name, other2)", 3, InnerJoin2)]               
        [InlineData(6, "ShowColumns(Join(local, remote, LeftRecord.rtid = RightRecord.remoteid, JoinType.Left,  RightRecord.other As other2), localid, new_name, other2)", 4, LeftJoin2)]               
        [InlineData(7, "ShowColumns(Join(local, remote, LeftRecord.rtid = RightRecord.remoteid, JoinType.Right, RightRecord.other As other2), localid, new_name, other2)", 4, RightJoin2, "Warning 17-22: This operation on table 'local' may not work if it has more than 999 rows.")]        
        [InlineData(8, "ShowColumns(Join(local, remote, LeftRecord.rtid = RightRecord.remoteid, JoinType.Full,  RightRecord.other As other2), localid, new_name, other2)", 5, FullJoin2)]

        [InlineData(9,  "ShowColumns(Join(local As l, remote As r, l.rtid = r.remoteid, JoinType.Inner, r.other As other2), localid, new_name, other2)", 3, InnerJoin2)]
        [InlineData(10, "ShowColumns(Join(local As l, remote As r, l.rtid = r.remoteid, JoinType.Left,  r.other As other2), localid, new_name, other2)", 4, LeftJoin2)]
        [InlineData(11, "ShowColumns(Join(local As l, remote As r, l.rtid = r.remoteid, JoinType.Right, r.other As other2), localid, new_name, other2)", 4, RightJoin2, "Warning 17-22: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(12, "ShowColumns(Join(local As l, remote As r, l.rtid = r.remoteid, JoinType.Full,  r.other As other2), localid, new_name, other2)", 5, FullJoin2)]

        [InlineData(13, "ShowColumns(Join(local, remote, RightRecord.remoteid = LeftRecord.rtid, JoinType.Inner, RightRecord.other As other2), localid, new_name, other2)", 3, InnerJoin2)]
        [InlineData(14, "ShowColumns(Join(local, remote, RightRecord.remoteid = LeftRecord.rtid, JoinType.Left, RightRecord.other As other2), localid, new_name, other2)", 4, LeftJoin2)]
        [InlineData(15, "ShowColumns(Join(local, remote, RightRecord.remoteid = LeftRecord.rtid, JoinType.Right, RightRecord.other As other2), localid, new_name, other2)", 4, RightJoin2, "Warning 17-22: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(16, "ShowColumns(Join(local, remote, RightRecord.remoteid = LeftRecord.rtid, JoinType.Full, RightRecord.other As other2), localid, new_name, other2)", 5, FullJoin2)]
        
        [InlineData(17, "ShowColumns(Join(local As l, remote As r, r.remoteid = l.rtid, JoinType.Inner, r.other As other2), localid, new_name, other2)", 3, InnerJoin2)]
        [InlineData(18, "ShowColumns(Join(local As l, remote As r, r.remoteid = l.rtid, JoinType.Left,  r.other As other2), localid, new_name, other2)", 4, LeftJoin2)]
        [InlineData(19, "ShowColumns(Join(local As l, remote As r, r.remoteid = l.rtid, JoinType.Right, r.other As other2), localid, new_name, other2)", 4, RightJoin2, "Warning 17-22: This operation on table 'local' may not work if it has more than 999 rows.")]
        [InlineData(20, "ShowColumns(Join(local As l, remote As r, r.remoteid = l.rtid, JoinType.Full,  r.other As other2), localid, new_name, other2)", 5, FullJoin2)]

        // Now trying when remote is left and local is right
        [InlineData(21, "ShowColumns(Join(remote, local, LeftRecord.remoteid = RightRecord.rtid, JoinType.Inner, RightRecord.new_name As other2), remoteid, data, other)", 3, InnerJoin3)]
        [InlineData(22, "ShowColumns(Join(remote, local, LeftRecord.remoteid = RightRecord.rtid, JoinType.Left,  RightRecord.new_name As other2), remoteid, data, other)", 4, LeftJoin3)]
        [InlineData(23, "ShowColumns(Join(remote, local, LeftRecord.remoteid = RightRecord.rtid, JoinType.Right, RightRecord.new_name As other2), remoteid, data, other)", 4, RightJoin3, "Warning 17-23: This operation on table 'remote' may not work if it has more than 999 rows.")]
        [InlineData(24, "ShowColumns(Join(remote, local, LeftRecord.remoteid = RightRecord.rtid, JoinType.Full,  RightRecord.new_name As other2), remoteid, data, other)", 5, FullJoin3)]

        [InlineData(25, "ShowColumns(Join(remote As l, local As r, l.remoteid = r.rtid, JoinType.Inner, r.new_name As other2), remoteid, data, other)", 3, InnerJoin3)]
        [InlineData(26, "ShowColumns(Join(remote As l, local As r, l.remoteid = r.rtid, JoinType.Left,  r.new_name As other2), remoteid, data, other)", 4, LeftJoin3)]
        [InlineData(27, "ShowColumns(Join(remote As l, local As r, l.remoteid = r.rtid, JoinType.Right, r.new_name As other2), remoteid, data, other)", 4, RightJoin3, "Warning 17-23: This operation on table 'remote' may not work if it has more than 999 rows.")]
        [InlineData(28, "ShowColumns(Join(remote As l, local As r, l.remoteid = r.rtid, JoinType.Full,  r.new_name As other2), remoteid, data, other)", 5, FullJoin3)]

        [InlineData(29, "ShowColumns(Join(remote, local, RightRecord.rtid = LeftRecord.remoteid, JoinType.Inner, RightRecord.new_name As other2), remoteid, data, other)", 3, InnerJoin3)]
        [InlineData(30, "ShowColumns(Join(remote, local, RightRecord.rtid = LeftRecord.remoteid, JoinType.Left,  RightRecord.new_name As other2), remoteid, data, other)", 4, LeftJoin3)]
        [InlineData(31, "ShowColumns(Join(remote, local, RightRecord.rtid = LeftRecord.remoteid, JoinType.Right, RightRecord.new_name As other2), remoteid, data, other)", 4, RightJoin3, "Warning 17-23: This operation on table 'remote' may not work if it has more than 999 rows.")]
        [InlineData(32, "ShowColumns(Join(remote, local, RightRecord.rtid = LeftRecord.remoteid, JoinType.Full,  RightRecord.new_name As other2), remoteid, data, other)", 5, FullJoin3)]

        // join with left column renames
        [InlineData(33, "ShowColumns(Join(local, remote, LeftRecord.rtid = RightRecord.remoteid, JoinType.Inner, RightRecord.other As other2, LeftRecord.new_name As n4), localid, n4, other2)", 3, InnerJoin4)]

        // join with First()
        [InlineData(34, "First(ShowColumns(Join(local, remote, LeftRecord.rtid = RightRecord.remoteid, JoinType.Inner, RightRecord.other As other2, LeftRecord.new_name As n4), localid, n4, other2))", 1, @"{localid:GUID(""00000000-0000-0000-0000-000000000003""),n4:""p1"",other2:Float(49)}")]

        // Validate no Join delegation as either left or right is not a direct table
        [InlineData(35, "ShowColumns(Join(FirstN(local, 10), remote, LeftRecord.rtid = RightRecord.remoteid, JoinType.Inner, RightRecord.other As other2), localid, new_name, other2)", 3, InnerJoin2)]
        [InlineData(36, "ShowColumns(Join(local, FirstN(remote, 10), LeftRecord.rtid = RightRecord.remoteid, JoinType.Inner, RightRecord.other As other2), localid, new_name, other2)", 3, InnerJoin2)]
        [InlineData(37, @"ShowColumns(Join(Filter(local, true), remote, LeftRecord.rtid = RightRecord.remoteid, JoinType.Inner, RightRecord.other As other2), localid, new_name, other2)", 3, InnerJoin2, "Warning 17-36: This operation on table 'table' may not work if it has more than 999 rows.", "Warning 24-29: This operation on table 'local' may not work if it has more than 999 rows.", "Warning 31-35: Warning: This predicate is a literal value and does not reference the input table.")]
        [InlineData(38, @"ShowColumns(Join(Filter(local, IsBlank(new_name) Or new_name <> ""pz""), remote, LeftRecord.rtid = RightRecord.remoteid, JoinType.Inner, RightRecord.other As other2), localid, new_name, other2)", 2, InnerJoin5)]

        // Join + ShowColumns + LookUp        
        [InlineData(39, "LookUp(ShowColumns(Join(local, remote, LeftRecord.rtid = RightRecord.remoteid, JoinType.Inner, RightRecord.other As other2, LeftRecord.new_name As n4), localid, n4, other2), other2 = 44)", 1, @"{localid:GUID(""00000000-0000-0000-0000-000000000004""),n4:""row4"",other2:Float(44)}")]                
        [InlineData(40, @"LookUp(ShowColumns(Join(local, remote, LeftRecord.rtid = RightRecord.remoteid, JoinType.Inner, RightRecord.other As other2, LeftRecord.new_name As n4), localid, n4, other2), n4 = ""p1"")", 1, @"{localid:GUID(""00000000-0000-0000-0000-000000000003""),n4:""p1"",other2:Float(49)}")]

        // Join + ShowColumns + Filter
        [InlineData(41, "Filter(ShowColumns(Join(local, remote, LeftRecord.rtid = RightRecord.remoteid, JoinType.Inner, RightRecord.other As other2, LeftRecord.new_name As n4), localid, n4, other2), other2 = 44)", 1, @"Table({localid:GUID(""00000000-0000-0000-0000-000000000004""),n4:""row4"",other2:Float(44)})")]
        [InlineData(42, @"Filter(ShowColumns(Join(local, remote, LeftRecord.rtid = RightRecord.remoteid, JoinType.Inner, RightRecord.other As other2, LeftRecord.new_name As n4), localid, n4, other2), n4 = ""p1"")", 1, @"Table({localid:GUID(""00000000-0000-0000-0000-000000000003""),n4:""p1"",other2:Float(49)})")]

        // Join + ShowColumns + FirstN
        [InlineData(43, "FirstN(ShowColumns(Join(local, remote, LeftRecord.rtid = RightRecord.remoteid, JoinType.Inner, RightRecord.other As other2, LeftRecord.new_name As n4), localid, n4, other2), 2)", 2, @"Table({localid:GUID(""00000000-0000-0000-0000-000000000003""),n4:""p1"",other2:Float(49)},{localid:GUID(""00000000-0000-0000-0000-000000000004""),n4:""row4"",other2:Float(44)})")]

        // Join + ForAll
        [InlineData(44, "ForAll(Join(local, remote, LeftRecord.rtid = RightRecord.remoteid, JoinType.Inner, RightRecord.other As other2, LeftRecord.new_name As n4), n4)", 3, @"Table({Value:""p1""},{Value:""row4""},{Value:If(false,"""")})")]
        [InlineData(45, "ForAll(Join(local, remote, LeftRecord.rtid = RightRecord.remoteid, JoinType.Inner, RightRecord.other As other2, LeftRecord.new_name As n4), {n5: n4})", 3, @"Table({n5:""p1""},{n5:""row4""},{n5:If(false,"""")})")]

        // no delegation of SortByColumns as a FxColumnMap is used in Join 
        [InlineData(46, "ShowColumns(SortByColumns(Join(local, remote, LeftRecord.rtid = RightRecord.remoteid, JoinType.Inner, RightRecord.other As other2, LeftRecord.new_name As n4), n4), n4)", 3, @"Table({n4:""p1""},{n4:""row4""},{n4:If(false,"""")})")]
        [InlineData(47, "SortByColumns(ShowColumns(Join(local, remote, LeftRecord.rtid = RightRecord.remoteid, JoinType.Inner, RightRecord.other As other2, LeftRecord.new_name As n4), n4), n4)", 3, @"Table({n4:""p1""},{n4:""row4""},{n4:If(false,"""")})")]

        // no delegation of Summarize as a FxColumnMap is used in Join
        [InlineData(48, "Summarize(ShowColumns(Join(local, remote, LeftRecord.rtid = RightRecord.remoteid, JoinType.Inner, RightRecord.other As other2), localid, new_name, other2), localid, Average(ThisGroup, other2) As avg)", 3, @"Table({avg:Float(49),localid:GUID(""00000000-0000-0000-0000-000000000003"")},{avg:Float(44),localid:GUID(""00000000-0000-0000-0000-000000000004"")},{avg:Float(49),localid:GUID(""00000000-0000-0000-0000-000000000005"")})")]

        public async Task JoinDelegationAsync(int id, string expr, int n, string expected, params string[] expectedWarnings)
        {
            await DelegationTestAsync(id, "JoinDelegation.txt", expr, n, expected, result => GetResult(result), false, false, null, true, true, false, expectedWarnings);
        }

        private static string GetResult(FormulaValue fv)
        {
            if (fv is RecordValue rv)
            {                
                return RecordValue.NewRecordFromFields(rv.Fields.ToArray()).ToExpression().ToString();
            }

            IEnumerable<DValue<RecordValue>> rows = (fv as TableValue).Rows;

            if (!rows.Any())
            {
                return "<empty>";
            }

            return FormulaValue.NewTable(rows.First().Value.IRContext.ResultType as RecordType, rows.Select(r => RecordValue.NewRecordFromFields(r.Value.Fields.ToArray())).ToArray()).ToExpression().ToString();
        }        
    }
}
