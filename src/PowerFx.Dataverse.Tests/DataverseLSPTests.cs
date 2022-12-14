//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.LanguageServerProtocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Web;


namespace Microsoft.PowerFx.Dataverse.Tests
{
    // end2end test with full LSP (Language Service Provider) to mimic Dataverse test usage.
    // LSP is network layer on top of intellisense support. 
    [TestClass]
	public class DataverseLSPTests
    {
		static string _entityLogicalName = DataverseTests.AllAttributeModel.LogicalName;

		public class PowerFxScopeFactory : IPowerFxScopeFactory
		{
			public PowerFxScopeFactory()
			{
			}

			public IPowerFxScope GetOrCreateInstance(string documentUri)
			{
				Uri uri = new Uri(documentUri);
				var query = HttpUtility.ParseQueryString(uri.Query);
				var entityLogicalName = query.Get("entityLogicalName");

				var localeName = query.Get("localeName");
				var culture = (localeName == null) ? null : CultureInfo.CreateSpecificCulture(localeName);

				// Dataverse would use the entity name to look up proper metadata. 
				Assert.AreEqual(_entityLogicalName, entityLogicalName);

				var powerFx2SqlEngine = DataverseIntellisenseTests.GetAllAttributesEngine(culture);

				return powerFx2SqlEngine;
			}
		}

		[TestMethod]
		public void CheckLspSuggestion()
		{
			var _sendToClientData = new List<string>();
			var _scopeFactory = new PowerFxScopeFactory();
			var _testServer = new TestLanguageServer(_sendToClientData.Add, _scopeFactory);

			string expression = "Ab"; // expect "Abs"

			_testServer.OnDataReceived(JsonSerializer.Serialize(new
			{
				jsonrpc = "2.0",
				id = "123456",
				method = "textDocument/completion",
				@params = new
				{
					textDocument = new
					{
						uri = "powerfx://field_designer?entityLogicalName=" + _entityLogicalName + "&expression=" + expression
					},
					position = new
					{
						line = 0,
						character = 2
					},
					context = new
					{
						triggerKind = 1,
					}
				}
			}));

			Assert.AreEqual(_sendToClientData.Count, 1);
			var resultJson = _sendToClientData[0];

			var response = JsonSerializer.Deserialize<JsonRpcCompletionResponse>(resultJson);

			Assert.AreEqual(response.jsonrpc, "2.0");
			Assert.AreEqual(response.id, "123456");

			Assert.IsNull(response.error); // exceptions converted into error

			Assert.IsFalse(response.result.isincomplete);
			Assert.AreEqual(response.result.items.Where(item => item.label == "Abs").Count(), 1);
		}

		[DataTestMethod]
		[DataRow("1 + 2", "Number", DisplayName = "Number")]
		[DataRow("\"foo\"", "String", DisplayName = "String literal")]
		[DataRow("'UserLocal DateTime'", "DateTime", DisplayName = "DateTime")]
		[DataRow("DateOnly", "Date", DisplayName = "Date")]
		[DataRow("UTCToday()", "DateTimeNoTimeZone", DisplayName = "UTCToday function")]
		[DataRow("UTCNow()", "DateTimeNoTimeZone", DisplayName = "UTCNow function")]
		[DataRow("Boolean", "Boolean")]
		[DataRow("field", "Number", DisplayName = "Decimal")]
		[DataRow("Money", "Number", DisplayName = "Money")]
		[DataRow("Int", "Number", DisplayName = "Int")]
		[DataRow("String", "String", DisplayName = "String")]
		[DataRow("true", "Boolean", DisplayName = "Boolean literal")]
		[DataRow("Mod(int, int)", "Number", DisplayName = "Mod function")]
		public void CheckLspCheck(string expression, string type)
		{
			var _sendToClientData = new List<string>();
			var _scopeFactory = new PowerFxScopeFactory();
			var _testServer = new TestLanguageServer(_sendToClientData.Add, _scopeFactory);

			_testServer.OnDataReceived(JsonSerializer.Serialize(new
			{
				jsonrpc = "2.0",
				method = "textDocument/didChange",
				@params = new
				{
					textDocument = new
					{
						uri = "powerfx://field_designer?entityLogicalName=" + _entityLogicalName + "&getExpressionType=true",
						version = 4
					},
					contentChanges = new [] {
						new
						{
							text = expression
						}
					}
				}
			}));

			Assert.AreEqual(_sendToClientData.Count, 2);
			var resultJson = _sendToClientData[1];

			var response = JsonSerializer.Deserialize<JsonRpcExpressionTypeResponse>(resultJson);

			Assert.AreEqual(response.jsonrpc, "2.0");

			Assert.IsNotNull(response.@params.type);

			Assert.AreEqual(type, response.@params.type.Type);
		}

		// Helper to send an $/initialFixup message.
		// This will call engine's IPowerFxScopeDisplayName.TranslateToDisplayName
		private void SendFixup(LanguageServer server, string localeName, string expression)
        {
			// This will call engine's IPowerFxScopeDisplayName.TranslateToDisplayName
			server.OnDataReceived(JsonSerializer.Serialize(new
			{
				jsonrpc = "2.0",
				id = "123456",
				method = "$/initialFixup",
				@params = new
				{
					textDocument = new
					{
						uri = "powerfx://field_designer?entityLogicalName=" + _entityLogicalName + "&getExpressionType=true&localeName=" + localeName,
						version = 4,
						Text = expression
					}
				}
			}));			
		}

		// Helper to verify the results of an $/initialFixup message. 
		void AssertFixupResult(List<string> sendToClientData, string expected)
		{
			Assert.AreEqual(sendToClientData.Count, 1);
			var resultJson = sendToClientData[0];

			var response = JsonSerializer.Deserialize<JsonRpcFixupResponse>(resultJson);

			Assert.AreEqual(response.jsonrpc, "2.0");
			Assert.AreEqual(expected, response.result.text);
		}


		// Fixup message is sent when FormulaBar first initializes to convert from 
		// invariant locale to current locale. 
		[TestMethod]
		public void CheckLspFixup()
		{
			var _sendToClientData = new List<string>();
			var _scopeFactory = new PowerFxScopeFactory();
			var _testServer = new TestLanguageServer(_sendToClientData.Add, _scopeFactory);

			// Fixup will adjust from invariant to current locale:
			//   Invariant name (new_field) --> Display Name  (field)
			//   token separator (,) --> current locale separator (;)
			//   Numbers (1.1) --> local numbers (1,1)

			string invariantExpr = "If(true, 1.1, new_field)";
			string localeExpr = "If(true; 1,1; field)"; 
			string localeName = "fr-FR"; // pass to engine. 

			SendFixup(_testServer, localeName, invariantExpr);

			AssertFixupResult(_sendToClientData, localeExpr);
		}

		// Fixup still works with error expressions. 
		[TestMethod]
		public void CheckLspFixupError()
		{
			var _sendToClientData = new List<string>();
			var _scopeFactory = new PowerFxScopeFactory();
			var _testServer = new TestLanguageServer(_sendToClientData.Add, _scopeFactory);
			
			// Intentional Parse error (missing closing quote)
			// - still fixes up what it can. 
			string invariantExpr = "If(missing, 1.1, new_field // error";
			string localeExpr = "If(missing; 1,1; field // error";
			string localeName = "fr-FR"; // pass to engine. 

			SendFixup(_testServer, localeName, invariantExpr);

			AssertFixupResult(_sendToClientData, localeExpr);
		}

		public class JsonRpcExpressionTypeResponse
        {
			public string jsonrpc { get; set; }

			public Params @params { get; set; }


			public class Params
            {
				public string uri { get; set; }

				public ExpressionType @type { get; set; }

				public class ExpressionType
				{
					public string @Type { get; set; }
				}
			}
		}

		public class JsonRpcFixupResponse
		{
			public string jsonrpc { get; set; }

			public CompletionResult result { get; set; }

			public class CompletionResult
			{
				public string uri { get; set; }
				
				public string languageId { get; set; }

				public int version { get; set; }

				public string text { get; set; }
			}
		}


		public class JsonRpcCompletionResponse
		{
			public string jsonrpc { get; set; }

			public string id { get; set; } = string.Empty;

			public string error { get; set; }

			public CompletionResult result { get; set; }

			public class CompletionResult
			{
				public bool isincomplete { get; set; }

				public CompletionItem[] items { get; set; }

			}

			public class CompletionItem
			{
				public string label { get; set; }
			}
		}

		public class TestLanguageServer : LanguageServer
		{
			public List<string> _sendToClientData = new List<string>();

			public TestLanguageServer(SendToClient sendToClient, IPowerFxScopeFactory scopeFactory)
				: base(sendToClient, scopeFactory)
			{
			}

			public int TestGetCharPosition(string expression, int position) => GetCharPosition(expression, position);

			public int TestGetPosition(string expression, int line, int character) => GetPosition(expression, line, character);
		}
	}
}
