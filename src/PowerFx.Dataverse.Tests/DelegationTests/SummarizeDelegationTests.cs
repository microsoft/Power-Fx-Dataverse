// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Differencing;
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

        // Summarize CountIf()
        [InlineData(5, "Summarize(t1, Name, CountIf(ThisGroup, Not(IsBlank(amount))) As TCount)", new DelegationOperator[] { })]

        [InlineData(6, "FirstN(Summarize(t1, name, Sum(ThisGroup, amount) As TotalAmount), 10)", new DelegationOperator[] { })]

        [InlineData(7, "First(Summarize(t1, name, Sum(ThisGroup, amount) As TotalAmount))", new DelegationOperator[] { })]

        // ************************** Not supported scenarios *****************************
        // Summarize on ShowColumns() and vice versa not supported.
        [InlineData(8, "ShowColumns(Summarize(t1, name, Sum(ThisGroup, amount) As TotalAmount), TotalAmount)", new DelegationOperator[] { })]

        [InlineData(9, "Summarize(ShowColumns(t1, Name, amount), Name, CountIf(ThisGroup, Not(IsBlank(amount))) As TCount)", new DelegationOperator[] { })]

        // Summarize on ForAll() not supported.
        [InlineData(10, "Summarize(ForAll(t1, {Name: Name, amount: amount}), Name, CountIf(ThisGroup, Not(IsBlank(amount))) As TCount)", new DelegationOperator[] { })]

        // nested Summarize not supported.
        [InlineData(11, "Summarize(Summarize(t1, name, Sum(ThisGroup, amount) As TotalAmount), name)", new DelegationOperator[] { })]

        // Summarize with column manipulation not supported.
        [InlineData(12, "Summarize(t1, name, Sum(ThisGroup, amount * 2) As TotalAmount)", new DelegationOperator[] { })]

        // Sqrt can not be delegated.
        [InlineData(13, "Summarize(t1, Name, credit, Sum(ThisGroup, Sqrt(amount)) As TotalAmount)", new DelegationOperator[] { })]

        // Sorting after Summarize() not supported.
        [InlineData(14, "FirstN(SortByColumns(Summarize(ForAll(Filter(t1, ThisRecord.Name = \"test\"), { Name:ThisRecord.Name, amount:ThisRecord.amount }), Name, CountIf(ThisGroup, Not(IsBlank(amount))) As COUNT_Id), COUNT_Id, SortOrder.Descending), 5)  ", new DelegationOperator[] { DelegationOperator.Eq, DelegationOperator.Count, DelegationOperator.Top })]
        [InlineData(15, "Sort(Summarize(t1, name, Sum(ThisGroup, amount) As TotalAmount), TotalAmount)", new DelegationOperator[] { })]
        [InlineData(16, "SortByColumns(Summarize(t1, name, Sum(ThisGroup, amount) As TotalAmount), name)", new DelegationOperator[] { })]
        [InlineData(17, "FirstN(SortByColumns(Summarize(ForAll(t1, { Name:ThisRecord.Name, amount:ThisRecord.amount }), Name, CountIf(ThisGroup, Not(IsBlank(amount))) As COUNT_Id), COUNT_Id, SortOrder.Descending), 5)  ", new DelegationOperator[] { DelegationOperator.Eq, DelegationOperator.Count, DelegationOperator.Top })]

        // Filter on Summarize() not supported.
        [InlineData(18, "Filter(Summarize(t1, name, Sum(ThisGroup, amount) As TotalAmount), TotalAmount > 10)", new DelegationOperator[] { })]

        // LookUp on Summarize() not supported.
        [InlineData(19, "LookUp(Summarize(t1, name, Sum(ThisGroup, amount) As TotalAmount), TotalAmount > 10)", new DelegationOperator[] { })]

        // Distinct on Summarize() not supported.
        [InlineData(20, "Distinct(Summarize(t1, name, Sum(ThisGroup, amount) As TotalAmount), TotalAmount)", new DelegationOperator[] { })]
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
