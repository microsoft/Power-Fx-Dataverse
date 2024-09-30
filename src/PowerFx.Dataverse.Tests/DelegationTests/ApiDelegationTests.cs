// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Connectors;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class ApiDelegationTests
    {
        // Delegation using direct API.
        [Theory]
        [InlineData("Filter(t1, Price < 120 And 90 <= Price)", "$filter=((Price+lt+120)+and+(Price+ge+90))&$top=1000", "Table({Price:100,opt:Blank()})")]
        [InlineData("First(t1).Price", "$top=1", "100")]
        [InlineData("Filter(t1, ThisRecord.opt = Opt.display2)", "$filter=(opt+eq+%27logical2%27)&$top=1000", "Table({Price:100,opt:Blank()})")]
        public async Task TestDirectApi(string expr, string odataFilter, string expectedStr)
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

            TestCdpDataSource ds = TestCdpDataSource.GetCDPDataSource("t1", recordType, recordValue);
            CdpTableValue tt = ds.CdpTable.GetTableValue();            

            var st = new SymbolValues("Delegable_1");
            st.Add("t1", tt);

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

            string actualODataFilter = ds.CdpTable.ODataParameters.ToQueryString();
            Assert.Equal<object>(odataFilter, actualODataFilter);            

            var sb = new StringBuilder();
            result.ToExpression(sb, new FormulaValueSerializerSettings { UseCompactRepresentation = true });
            var resultStr = sb.ToString();
            Assert.Equal(expectedStr, resultStr);            
        }

        [Theory]
        [InlineData("Filter(t1, Price < 120)", "__retrieveMultiple(t1, __lt(t1, Price, Float(120)), __noop(), 1000, )")]
        [InlineData("Filter(t1, Price <= 120)", "Filter(t1, (LeqNumbers(Price,Float(120))))")]
        public async Task CapabilitiesTest(string expr, string expectedIr)
        {
            var recordType = RecordType.Empty()
                .Add("Price", FormulaType.Number);

            var recordValue = FormulaValue.NewRecordFromFields(recordType, new NamedValue[] { new NamedValue("Price", FormulaValue.New(100f)) });

            TestCdpDataSource ds = TestCdpDataSource.GetCDPDataSource("t1", recordType, recordValue, serviceCapabilities =>
            {
                // Hack ServiceCapabilities to only allow '<' operator on Price
                ((ColumnCapabilities2)((Dictionary<string, ColumnCapabilitiesBase2>)serviceCapabilities.ColumnsCapabilities)["Price"]).Capabilities = new ColumnCapabilitiesDefinition2(new string[] { "lt" }, null, null);
            });
            
            CdpTableValue cdpTable = ds.CdpTable.GetTableValue();

            //var t1 = new MyTable(tt.ToRecord());
            var st = new SymbolValues("Delegable_1");
            st.Add("t1", cdpTable);

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
