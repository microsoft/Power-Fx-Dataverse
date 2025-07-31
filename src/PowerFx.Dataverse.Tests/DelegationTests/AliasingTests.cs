// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class AliasingTests
    {
        [Theory]
        [InlineData(
            "First(Filter(ForAll(t1,{aliasedF1 : logicalF1}), aliasedF1 < 10)).aliasedF1",
            "(__retrieveSingle(t1, __lt(t1, {fieldFunctions:Table(), fieldName:logicalF1}, Float(10)), __noop(), __noJoin(), __noopGroupBy(), {logicalF1 As aliasedF1})).aliasedF1", 
            10.0)]

        [InlineData(
            "First(Filter(ForAll(t1,{logicalF2 : logicalF1}), logicalF2 < 10)).logicalF2",
            "(__retrieveSingle(t1, __lt(t1, {fieldFunctions:Table(), fieldName:logicalF1}, Float(10)), __noop(), __noJoin(), __noopGroupBy(), {logicalF1 As logicalF2})).logicalF2",
            10.0)]
        public async Task TestAliasingWrapper(string expression, string expectedIR, double expectedValue)
        {
            // Arrange
            var allowedOperators = new List<Core.Functions.Delegation.DelegationOperator>
            {
                Core.Functions.Delegation.DelegationOperator.And,
                Core.Functions.Delegation.DelegationOperator.Eq,
                Core.Functions.Delegation.DelegationOperator.Lt,
                Core.Functions.Delegation.DelegationOperator.Le
            };

            var recordType = new TestRecordType(
                "t1",
                RecordType.Empty()
                    .Add("logicalF1", FormulaType.Number, "DisplayF1")
                    .Add("logicalF2", FormulaType.Number, "DisplayF2"),
                allowedOperators);

            var mockRecord = FormulaValue.NewRecordFromFields(
                recordType,
                new NamedValue[] { new NamedValue("logicalF1", FormulaValue.New(10.0)), new NamedValue("logicalF2", FormulaValue.New(20.0)) });

            var tableValue = new TestTableValue(
                "t1",
                recordType,
                mockRecord,
                allowedOperators);

            var symbolValues = new SymbolValues("Delegable_1");
            symbolValues.Add("t1", tableValue);
            var symbolTable = symbolValues.SymbolTable;

            var engine = new RecalcEngine(new PowerFxConfig());
            engine.EnableDelegation();

            // Act
            var checkResult = engine.Check(expression, symbolTable: symbolTable);

            // Assert
            Assert.True(checkResult.IsSuccess);

            Assert.Equal(expectedIR, checkResult.GetCompactIRString());

            var evaluationResult = await checkResult
                .GetEvaluator()
                .EvalAsync(cancellationToken: default, symbolValues);

            if (evaluationResult is NumberValue numberValue)
            {
                Assert.Equal(expectedValue, numberValue.Value);
            }
            else
            {
                Assert.True(false, $"Expected NumberValue but got {evaluationResult.GetType().Name}");
            }
        }
    }
}
