// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Dataverse.EntityMock;
using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.Types;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    // Unit tests on EditorContextScope.
    // This is used for intellisense.

    public class EditorContextScopeTests
    {
        private PowerFx2SqlEngine GetSqlEngine()
        {
            var provider = new MockXrmMetadataProvider(MockModels.RelationshipModels);
            var sqlEngine = new PowerFx2SqlEngine(MockModels.RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider) { NumberIsFloat = DataverseEngine.NumberIsFloat });
            return sqlEngine;
        }

        // Check() calls through to engine.
        [Fact]
        public void Test()
        {
            var sqlEngine = GetSqlEngine();
            Engine engine = sqlEngine;

            // 'field' is defined on sql
            var expr = "ThisRecord.new_price + new_quantity"; // Succeeds!

            var result = engine.Check(expr);
            Assert.True(result.IsSuccess);

            var display = sqlEngine.ConvertToDisplay(expr);
            Assert.Equal("ThisRecord.Price + Quantity", display);

            // symbols as null, since we're pulling everything from RuleScope
            EditorContextScope scope = engine.CreateEditorScope(symbols: null);
            result = scope.Check(expr);
            Assert.True(result.IsSuccess);
            Assert.Equal(result.ReturnType, FormulaType.Decimal);

            IPowerFxScope scope2 = scope;
            display = scope2.ConvertToDisplay(expr);
            Assert.Equal("ThisRecord.Price + Quantity", display);
        }

        // Verify EditorContextScope still picks up the SQL restrictions.
        [Fact]
        public void SqlRestriction()
        {
            // An expression that's not valid in SQL compiler.
            string expr = "Text(123, \",###.0\")";

            // Normally valid
            var engineFull = new Engine(new PowerFxConfig());
            var result = engineFull.Check(expr);
            Assert.True(result.IsSuccess);

            // But not valid with SQL engine
            Engine engineSql = GetSqlEngine();
            result = engineSql.Check(expr);
            Assert.False(result.IsSuccess);

            EditorContextScope scope = engineSql.CreateEditorScope();
            result = scope.Check(expr);
            Assert.False(result.IsSuccess);
        }
    }
}
