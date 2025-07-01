// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class TopLevelAggregationDelegationTests
    {
        private static List<DelegationOperator> _supportedDelegationOperator = new List<DelegationOperator>() { DelegationOperator.Eq, DelegationOperator.And, DelegationOperator.Gt, DelegationOperator.Or, DelegationOperator.Not, DelegationOperator.Null, DelegationOperator.JoinInner, DelegationOperator.Distinct };

        [Theory]

        [InlineData(1, "CountRows(t1)")]
        [InlineData(2, "CountRows(Filter(t1, Name = \"test\" Or Credit = 0))")]
        [InlineData(3, "CountRows(Filter(t1, Name = \"test\" And Credit = 0))")]

        // Can't delegate CountRows() because inner filter is non delegable due to column comparison.
        [InlineData(4, "CountRows(Filter(t1, Name = \"test\" And Credit = Amount))")]

        [InlineData(5, "CountIf(t1, Name = \"test\")")]
        [InlineData(6, "CountIf(t1, IsBlank(Name))")]
        [InlineData(7, "CountIf(t1, !IsBlank(Name))")]
        [InlineData(8, "CountIf(t1, Name = \"test\" And Credit = 0)")]
        [InlineData(9, "CountIf(t1, Name = \"test\" Or Credit = 0)")]
        [InlineData(10, "CountIf(Filter(t1, Name = \"test\" Or Credit = 0), Amount = 0)")]

        [InlineData(11, "CountRows(Summarize(Filter(t1, Name = \"test\") , name, credit, Sum(ThisGroup, amount) As TotalAmount))")]

        // $$$ Can't setup Join() capabilities for delegation in non-dv case.
        [InlineData(12, "CountRows(Join(t1, t1 As t2, LeftRecord.id = t2.id, JoinType.Inner, t2.name As newName))")]

        // Can't delegate CountIf() with Summarize().
        [InlineData(13, "CountIf(Summarize(Filter(t1, Name = \"test\") , name, credit, Sum(ThisGroup, amount) As TotalAmount), name = \"test\")")]

        // Can't delegate CountIf() with Join()
        [InlineData(14, "CountIf(Join(t1, t1 As t2, LeftRecord.id = t2.id, JoinType.Inner, t2.name As newName), newName = \"test\")")]

        // Basic top level Sum() aggregation.
        [InlineData(15, "Sum(t1, amount)")]

        // top level Sum() with ForAll().
        [InlineData(16, "Sum(ForAll(t1, {amount: ThisRecord.amount}), amount)")]

        // top level Sum() with ForAll() and column manipulation, can't be delegated.
        [InlineData(17, "Sum(ForAll(t1, {amount: ThisRecord.amount * 2}), amount)")]

        // top level Sum() with Filter() and ForAll().
        [InlineData(18, "Sum(ForAll(Filter(t1, ThisRecord.Name = \"test\"), {amount: ThisRecord.amount}), amount)")]

        // Can't delegate Sum() with Summarize(). But still partially delegates Summarize.
        [InlineData(19, "Sum(Summarize(t1, name, Sum(ThisGroup, amount) As TotalAmount), TotalAmount)")]

        [InlineData(20, "CountRows(Distinct(Filter(t1, Credit > 5), Credit))")]
        [InlineData(21, "CountRows(Distinct(Filter(Sort(t1, Credit), Credit > 5), Credit))")]
        [InlineData(22, "CountRows(Distinct(Filter(ForAll(t1, { Xyz: Credit }), Xyz > 5), Xyz))")]
        public async Task TopLevelAggregationDelegationAsync(int id, string expression)
        {
            var file = "TopLevelAggregationDelegationAsync.txt";

            var recordType = RecordType.Empty()
                .Add("name", FormulaType.String, "Name")
                .Add("credit", FormulaType.Number, "Credit")
                .Add("amount", FormulaType.Number, "Amount")
                .Add("id", FormulaType.Number, "Id");

            var delegationRecordType = new TestRecordType("t1", recordType, _supportedDelegationOperator);

            var symbols = new SymbolTable();
            var t1Slot = symbols.AddVariable("t1", delegationRecordType.ToTable());

            var engine = new RecalcEngine();
            engine.EnableDelegation();
            engine.Config.EnableJoinFunction();
            var check = engine.Check(expression, symbolTable: symbols);

            var actualIr = check.GetCompactIRString();
            var testTableValue = new TestTableValue("t1", recordType, RecordValue.Empty(), _supportedDelegationOperator);

            var symbolValues = symbols.CreateValues();
            symbolValues.Set(t1Slot, testTableValue);

            var result = await check.GetEvaluator().EvalAsync(CancellationToken.None, runtimeConfig: new RuntimeConfig(symbolValues));

            var oDataStrings = string.Empty;
            var delegationParameter = (DataverseDelegationParameters)testTableValue.DelegationParameters;

            oDataStrings = delegationParameter != null &&

                // OData can't delegate CountRows() with Summarize() operations.
                (delegationParameter.GroupBy == null || !delegationParameter.ReturnTotalRowCount)
                ? DelegationTests.GetODataString(delegationParameter) : string.Empty;

            await DelegationTestUtility.CompareSnapShotAsync(id, file, string.IsNullOrEmpty(oDataStrings) ? actualIr : $"{actualIr} | {oDataStrings}", id, false);
        }
    }
}
