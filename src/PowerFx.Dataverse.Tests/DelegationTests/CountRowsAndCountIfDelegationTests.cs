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
    public class CountRowsAndCountIfDelegationTests
    {
        private static List<DelegationOperator> _supportedDelegationOperator = new List<DelegationOperator>() { DelegationOperator.Eq, DelegationOperator.And, DelegationOperator.Or, DelegationOperator.Not, DelegationOperator.Null };

        [Theory]
        [InlineData(1, "CountRows(t1)")]
        [InlineData(2, "CountRows(Filter(t1, Name = \"test\" Or Credit = 0))")]
        [InlineData(3, "CountRows(Filter(t1, Name = \"test\" And Credit = 0))")]
        [InlineData(4, "CountRows(Filter(t1, Name = \"test\" And Credit = Amount))")]

        [InlineData(5, "CountIf(t1, Name = \"test\")")]
        [InlineData(6, "CountIf(t1, IsBlank(Name))")]
        [InlineData(7, "CountIf(t1, !IsBlank(Name))")]
        [InlineData(8, "CountIf(t1, Name = \"test\" And Credit = 0)")]
        [InlineData(9, "CountIf(t1, Name = \"test\" Or Credit = 0)")]
        [InlineData(10, "CountIf(Filter(t1, Name = \"test\" Or Credit = 0), Amount = 0)")]

        [InlineData(11, "CountRows(Summarize(Filter(t1, Name = \"test\") , name, credit, Sum(ThisGroup, amount) As TotalAmount))")]

        [InlineData(12, "CountRows(Join(t1, t1 As t2, LeftRecord.Name = t2.Name, JoinType.Inner, t2.name As newName))")]

        // Can't delegate CountIf() with Summarize().
        [InlineData(13, "CountIf(Summarize(Filter(t1, Name = \"test\") , name, credit, Sum(ThisGroup, amount) As TotalAmount), name = \"test\")")]

        // Can't delegate CountIf() with Join()
        [InlineData(14, "CountIf(Join(t1, t1 As t2, LeftRecord.Name = t2.Name, JoinType.Inner, t2.name As newName), newName = \"test\")")]
        public async Task CountRowsAndCountIfDelegationAsync(int id, string expression)
        {
            var file = "CountRowsAndCountIfDelegationAsync.txt";

            var recordType = RecordType.Empty()
                .Add("name", FormulaType.String, "Name")
                .Add("credit", FormulaType.Number, "Credit")
                .Add("amount", FormulaType.Number, "Amount");

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
            if (delegationParameter != null && !delegationParameter.GroupBy?.FxAggregateExpressions.Any(e => e.AggregateMethod == Core.Entities.SummarizeMethod.Count) == true)
            {
                oDataStrings = DelegationTests.GetODataString((DataverseDelegationParameters)testTableValue.DelegationParameters);
            }
            else if (oDataStrings.Contains("__retrieve"))
            {
                throw new InvalidOperationException("Delegated IR should also have Delegation Parameters");
            }

            await DelegationTestUtility.CompareSnapShotAsync(id, file, string.IsNullOrEmpty(oDataStrings) ? actualIr : $"{actualIr} | {oDataStrings}", id, false);
        }
    }
}
