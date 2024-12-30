// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class ApiDelegationTests
    {
        // Delegation using direct API.
        [Theory]
        [InlineData(1, "Filter(t1, Price < 120 And 90 <= Price)", true, "((Price lt 120) and (Price ge 90))", 1000, "Table({Price:100,opt:Blank()})")]
        [InlineData(2, "Filter(t1, Price < 120 Or 90 <= Price)", false, null, null, "Table({Price:100,opt:Blank()})")] // Or not delegated
        [InlineData(3, "Filter(t1, Price > 120 And 90 <= Price)", false, null, null, "Table()")] // Gt not delegated
        [InlineData(4, "First(t1).Price", true, null, 1, "100")]
        [InlineData(5, "Filter(t1, ThisRecord.opt = Opt.display2)", true, "(opt eq 'logical2')", 1000, "Table({Price:100,opt:Blank()})")]
        [InlineData(6, "Filter(t1, IsBlank(Price))", false, null, null, "Table()")] // Null not delegated
        [InlineData(7, "Filter(t1, Not(Price < 120) = true)", false, null, null, "Table()")] // Not not delegated
        [InlineData(8, "ShowColumns(t1, Price)", false, null, null, "Table({Price:100})")] // table not selectable
        public async Task TestDirectApi(int id, string expr, bool delegation, string odataFilter, int? top, string expectedStr)
        {
            var dnp = DisplayNameUtility.MakeUnique(new Dictionary<string, string>()
            {
                { "logical1", "display1" },
                { "logical2", "display2" }
            });
            var optionSet = new OptionSet("Opt", dnp);

            var recordType = RecordType.Empty()
                .Add("Price", FormulaType.Number)
                .Add("opt", optionSet.FormulaType);

            var recordValue = FormulaValue.NewRecordFromFields(recordType, new NamedValue[] { new NamedValue("Price", FormulaValue.New(100f)) });

            TestTableValue ttv = new TestTableValue(
                "t1", 
                recordType, 
                recordValue,                 
                allColumnFilters: new List<DelegationOperator>() { DelegationOperator.And, DelegationOperator.Eq, DelegationOperator.Lt, DelegationOperator.Le });

            var st = new SymbolValues("Delegable_1");
            st.Add("t1", ttv);

            Assert.Equal("Delegable_1", st.SymbolTable.DebugName);

            var config = new PowerFxConfig();
            config.AddOptionSet(optionSet);

            var engine = new RecalcEngine(config);
            engine.EnableDelegation();

            var check = new CheckResult(engine)
                .SetText(expr)
                .SetBindingInfo(st.SymbolTable);

            var errors = check.ApplyErrors().ToArray();

            var ir = check.GetCompactIRString();

            var eval = check.GetEvaluator();

            var rc = new RuntimeConfig(st);
            var result = await eval.EvalAsync(CancellationToken.None, rc);

            DataverseDelegationParameters ddp = (DataverseDelegationParameters)ttv.DelegationParameters;
            string actualODataFilter = ddp?.GetOdataFilter();

            Assert.Equal(delegation, ddp != null);
            Assert.Equal<object>(odataFilter, actualODataFilter);
            Assert.Equal(top, ddp?.Top);

            var sb = new StringBuilder();
            result.ToExpression(sb, new FormulaValueSerializerSettings { UseCompactRepresentation = true });
            var resultStr = sb.ToString();
            Assert.Equal(expectedStr, resultStr);
        }

        [Theory]
        [InlineData("Filter(t1, Price < 120)", "__retrieveMultiple(t1, __lt(t1, {fieldFunctions:Table(), fieldName:Price}, Float(120)), __noop(), __noJoin(), __noopGroupBy(), 1000, )")]
        [InlineData("Filter(t1, Price <= 120)", "Filter(t1, (LeqNumbers(Price,Float(120))))")]
        public async Task CapabilitiesTest(string expr, string expectedIr)
        {
            var recordType = RecordType.Empty()
                .Add("Price", FormulaType.Number);

            var recordValue = FormulaValue.NewRecordFromFields(recordType, new NamedValue[] { new NamedValue("Price", FormulaValue.New(100f)) });

            TestTableValue ttv = new TestTableValue("t1", recordType, recordValue, new List<DelegationOperator>() { DelegationOperator.Eq, DelegationOperator.Lt });

            var st = new SymbolValues("Delegable_1");
            st.Add("t1", ttv);

            Assert.Equal("Delegable_1", st.SymbolTable.DebugName);

            var config = new PowerFxConfig();
            var engine = new RecalcEngine(config);
            engine.EnableDelegation();

            var check = new CheckResult(engine)
                .SetText(expr)
                .SetBindingInfo(st.SymbolTable);

            var errors = check.ApplyErrors().ToArray();

            var ir = check.GetCompactIRString();

            Assert.Equal<object>(expectedIr, ir);
        }
    }
}
