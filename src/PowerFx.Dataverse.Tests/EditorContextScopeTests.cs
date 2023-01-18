//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.LanguageServerProtocol;
using Microsoft.PowerFx.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Web;


namespace Microsoft.PowerFx.Dataverse.Tests
{
	// Unit tests on EditorContextScope.
	// This is used for intellisense. 
	[TestClass]
	public class EditorContextScopeTests
	{ 
		private PowerFx2SqlEngine GetSqlEngine()
        {
			var provider = new MockXrmMetadataProvider(DataverseTests.RelationshipModels);
			var sqlEngine = new PowerFx2SqlEngine(DataverseTests.RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider));
			return sqlEngine;
		}

		// Check() calls through to engine. 
		[TestMethod]
		public void Test()
		{
			var sqlEngine = GetSqlEngine();
			Engine engine = sqlEngine;

			// 'field' is defined on sql
			var expr = "ThisRecord.new_price + new_quantity"; // Succeeds! 

			var result = engine.Check(expr);
			Assert.IsTrue(result.IsSuccess);

			var display = sqlEngine.ConvertToDisplay(expr);
			Assert.AreEqual("ThisRecord.Price + Quantity", display);

			// symbols as null, since we're pulling everything from RuleScope
			EditorContextScope scope = engine.CreateEditorScope(symbols: null);
			result = scope.Check(expr);
			Assert.IsTrue(result.IsSuccess);
			Assert.AreEqual(result.ReturnType, FormulaType.Number);
			
			IPowerFxScope scope2 = scope;
			display = scope2.ConvertToDisplay(expr);
			Assert.AreEqual("ThisRecord.Price + Quantity", display);
		}

		// Verify EditorContextScope still picks up the SQL restrictions. 
		[TestMethod]
		public void SqlRestriction()
        {		
			// An expression that's not valid in SQL compiler. 
			string expr = "Text(123, \",###.0\")";

			// Normally valid 
			var engineFull = new Engine(new PowerFxConfig());
			var result = engineFull.Check(expr);
			Assert.IsTrue(result.IsSuccess);

			// But not valid with SQL engine
			Engine engineSql = GetSqlEngine();
			result = engineSql.Check(expr);
			Assert.IsFalse(result.IsSuccess);


			EditorContextScope scope = engineSql.CreateEditorScope();
			result = scope.Check(expr);
			Assert.IsFalse(result.IsSuccess);
		}
	}
}
