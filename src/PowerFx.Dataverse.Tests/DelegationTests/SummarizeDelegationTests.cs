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
    public class SummarizeDelegationTests
    {
        [Theory]
        [InlineData(1, "Summarize(t1, name, Sum(ThisGroup, amount) As TotalAmount)", new DelegationOperator[] { })]

        // Multiple Sum() delegation.
        [InlineData(2, "Summarize(t1, name, Sum(ThisGroup, amount) As TotalAmount, Sum(ThisGroup, credit) As 'T Credit')", new DelegationOperator[] { })]

        // Multiple GroupBy columns.
        [InlineData(3, "Summarize(t1, Name, credit, Sum(ThisGroup, amount) As TotalAmount)", new DelegationOperator[] { })]

        // Summarize with filter.
        [InlineData(4, "Summarize(Filter(t1, Name = \"test\") , name, credit, Sum(ThisGroup, amount) As TotalAmount)", new DelegationOperator[] { DelegationOperator.Eq })]

        // nested Summarize not supported.
        [InlineData(5, "Summarize(Summarize(t1, name, Sum(ThisGroup, amount) As TotalAmount), name)", new DelegationOperator[] { })]

        // Nested Summarize.
        public async Task SummarizeDelegationAsync(int id, string expression, DelegationOperator[] delegationOperator)
        {
            var file = "SummarizeDelegationAsync.txt";

            var recordType = RecordType.Empty()
                .Add("name", FormulaType.String, "Name")
                .Add("credit", FormulaType.Number, "Credit")
                .Add("amount", FormulaType.Number, "Amount");

            var delegationRecordType = new TestRecordType("t1", recordType, delegationOperator.ToList());

            var symbols = new SymbolTable();
            var t1Slot = symbols.AddVariable("t1", delegationRecordType.ToTable());

            var engine = new RecalcEngine();
            engine.EnableDelegation();
            var check = engine.Check(expression, symbolTable: symbols);

            var actualIr = check.GetCompactIRString();

            var testTableValue = new TestTableValue("t1", recordType, RecordValue.Empty(), delegationOperator.ToList());

            var symbolValues = symbols.CreateValues();
            symbolValues.Set(t1Slot, testTableValue);

            var result = await check.GetEvaluator().EvalAsync(CancellationToken.None, runtimeConfig: new RuntimeConfig(symbolValues));

            var oDataStrings = string.Empty;
            if (testTableValue.DelegationParameters != null)
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
