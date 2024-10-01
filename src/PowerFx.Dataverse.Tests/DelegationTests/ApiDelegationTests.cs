// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        [InlineData("Filter(t1, Price < 120 And 90 <= Price)", "((Price lt 120) and (Price ge 90))", 1000, "Table({Price:100,opt:Blank()})")]
        [InlineData("First(t1).Price", null, 1, "100")]
        [InlineData("Filter(t1, ThisRecord.opt = Opt.display2)", "(opt eq 'logical2')", 1000, "Table({Price:100,opt:Blank()})")]
        public async Task TestDirectApi(string expr, string odataFilter, int top, string expectedStr)
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

            TestTableValue ttv = new TestTableValue("t1", recordType, recordValue);

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
            string actualODataFilter = ddp.GetOdataFilter();

            Assert.Equal<object>(odataFilter, actualODataFilter);
            Assert.Equal(top, ddp.Top);

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

            TestTableValue ttv = new TestTableValue("t1", recordType, recordValue, (tp) =>
            {
                return new TableParameters()
                {
                    TableName = tp.TableName,
                    IsReadOnly = tp.IsReadOnly,
                    RecordType = tp.RecordType,
                    DatasetName = tp.DatasetName,
                    SortRestriction = tp.SortRestriction,
                    FilterRestriction = tp.FilterRestriction,
                    SelectionRestriction = tp.SelectionRestriction,
                    GroupRestriction = tp.GroupRestriction,
                    FilterFunctions = tp.FilterFunctions,
                    FilterSupportedFunctions = tp.FilterSupportedFunctions,
                    PagingCapabilities = tp.PagingCapabilities,
                    SupportsRecordPermission = tp.SupportsRecordPermission,
                    SupportsDataverseOffline = tp.SupportsDataverseOffline,
                    ColumnsCapabilities = tp.ColumnsCapabilities.Select(kvp => kvp.Key == "Price" 
                                                                            ? new KeyValuePair<string, ColumnCapabilitiesBase>(kvp.Key, new ColumnCapabilities(new ColumnCapabilitiesDefinition() { FilterFunctions = new string[] { "lt" } }))
                                                                            : kvp).ToList(),
                    ColumnsWithRelationships = new Dictionary<string, string>()
                };
            });

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
