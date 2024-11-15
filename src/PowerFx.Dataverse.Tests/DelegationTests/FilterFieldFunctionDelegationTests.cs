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
    public class FilterFieldFunctionDelegationTests
    {
        [Theory]
        [InlineData(1, "Filter(t1, Year(DateTimeColumn) = 2)", new[] { DelegationOperator.Year, DelegationOperator.Eq })]
        [InlineData(2, "Filter(t1, Year(DateTimeColumn) = 2)", new DelegationOperator[] { })]
        [InlineData(3, "Filter(t1, Month(DateTimeColumn) = 2)", new[] { DelegationOperator.Month, DelegationOperator.Eq })]
        [InlineData(4, "Filter(t1, Month(DateTimeColumn) = 2)", new DelegationOperator[] { })]
        [InlineData(5, "Filter(t1, Hour(DateTimeColumn) = 2)", new[] { DelegationOperator.Hour, DelegationOperator.Eq })]
        [InlineData(6, "Filter(t1, Hour(DateTimeColumn) = 2)", new DelegationOperator[] { })]
        public async Task FilterFieldFuntionDelegationAsync(int id, string expression, DelegationOperator[] delegationOperator)
        {
            var file = "FilterFieldFuntionDelegation.txt";

            var recordType = RecordType.Empty().Add("DateTimeColumn", FormulaType.DateTime);
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
