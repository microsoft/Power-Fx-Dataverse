// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Web;
using Microsoft.Dataverse.EntityMock;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.LanguageServerProtocol;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    // end2end test with full LSP (Language Service Provider) to mimic Dataverse test usage.
    // LSP is network layer on top of intellisense support.
    public class DataverseLSPTests
    {
        private static readonly string _entityLogicalName = MockModels.AllAttributeModel.LogicalName;

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
                Assert.Equal(_entityLogicalName, entityLogicalName);

                Engine powerFx2SqlEngine = DataverseIntellisenseTests.GetAllAttributesEngine(culture);
                EditorContextScope scope = powerFx2SqlEngine.CreateEditorScope();
                return scope;
            }
        }

        [Fact]
        public void CheckLspSuggestion()
        {
            var ssendToClientData = new List<string>();
            var scopeFactory = new PowerFxScopeFactory();
            var testServer = new TestLanguageServer(ssendToClientData.Add, scopeFactory);

            string expression = "Ab"; // expect "Abs"

#pragma warning disable CS0618
            testServer.OnDataReceived(JsonSerializer.Serialize(new
#pragma warning restore CS0618
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

            Assert.Single(ssendToClientData);
            var resultJson = ssendToClientData[0];

            var response = JsonSerializer.Deserialize<JsonRpcCompletionResponse>(resultJson);

            Assert.Equal("2.0", response.jsonrpc);
            Assert.Equal("123456", response.id);

            Assert.Null(response.error); // exceptions converted into error

            Assert.False(response.result.isincomplete);
            Assert.Single(response.result.items.Where(item => item.label == "Abs"));
        }

        [Theory]
        [InlineData("1 + 2", "Decimal")] // "Decimal"
        [InlineData("\"foo\"", "String")] // "String literal"
        [InlineData("'UserLocal DateTime'", "DateTime")] // "DateTime"
        [InlineData("DateOnly", "Date")] // "Date"
        [InlineData("UTCToday()", "DateTimeNoTimeZone")] // "UTCToday function"
        [InlineData("UTCNow()", "DateTimeNoTimeZone")] // "UTCNow function"
        [InlineData("Now()", "DateTime")] // "Now function"
        [InlineData("Boolean", "Boolean")]
        [InlineData("field", "Decimal")] // "Decimal"
        [InlineData("Money", "Decimal")] // "Money"
        [InlineData("Int", "Decimal")] // "Int"
        [InlineData("String", "String")] // "String"
        [InlineData("true", "Boolean")] // "Boolean literal"
        [InlineData("Mod(int, int)", "Decimal")] // "Mod function"
        public void CheckLspCheck(string expression, string type)
        {
            var sendToClientData = new List<string>();
            var scopeFactory = new PowerFxScopeFactory();
            var testServer = new TestLanguageServer(sendToClientData.Add, scopeFactory);

#pragma warning disable CS0618
            testServer.OnDataReceived(JsonSerializer.Serialize(new
#pragma warning restore CS0618
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
                    contentChanges = new[] 
                    {
                        new
                        {
                            text = expression
                        }
                    }
                }
            }));

            Assert.Equal(2, sendToClientData.Count);

            var resultJson = sendToClientData[1];
            var response = JsonSerializer.Deserialize<JsonRpcExpressionTypeResponse>(resultJson);

            Assert.Equal("2.0", response.jsonrpc);
            Assert.NotNull(response.@params.type);
            Assert.Equal(type, response.@params.type.Type);
        }

        // Helper to send an $/initialFixup message.
        // This will call engine's IPowerFxScopeDisplayName.TranslateToDisplayName
        private void SendFixup(LanguageServer server, string localeName, string expression)
        {
            // This will call engine's IPowerFxScopeDisplayName.TranslateToDisplayName
#pragma warning disable CS0618
            server.OnDataReceived(JsonSerializer.Serialize(new
#pragma warning restore CS0618
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
        private void AssertFixupResult(List<string> sendToClientData, string expected)
        {
            Assert.Single(sendToClientData);
            var resultJson = sendToClientData[0];

            var response = JsonSerializer.Deserialize<JsonRpcFixupResponse>(resultJson);

            Assert.Equal("2.0", response.jsonrpc);
            Assert.Equal(expected, response.result.text);
        }

        // Fixup message is sent when FormulaBar first initializes to convert from
        // invariant locale to current locale.
        [Fact]
        public void CheckLspFixup()
        {
            var sendToClientData = new List<string>();
            var scopeFactory = new PowerFxScopeFactory();
            var testServer = new TestLanguageServer(sendToClientData.Add, scopeFactory);

            // Fixup will adjust from invariant to current locale:
            //   Invariant name (new_field) --> Display Name  (field)
            //   token separator (,) --> current locale separator (;)
            //   Numbers (1.1) --> local numbers (1,1)

            string invariantExpr = "If(true, 1.1, new_field)";
            string localeExpr = "If(true; 1,1; field)";
            string localeName = "fr-FR"; // pass to engine.

            SendFixup(testServer, localeName, invariantExpr);

            AssertFixupResult(sendToClientData, localeExpr);
        }

        // Fixup still works with error expressions.
        [Fact]
        public void CheckLspFixupError()
        {
            var sendToClientData = new List<string>();
            var scopeFactory = new PowerFxScopeFactory();
            var testServer = new TestLanguageServer(sendToClientData.Add, scopeFactory);

            // Intentional Parse error (missing closing quote)
            // - still fixes up what it can.
            string invariantExpr = "If(missing, 1.1, new_field // error";
            string localeExpr = "If(missing; 1,1; field // error";
            string localeName = "fr-FR"; // pass to engine.

            SendFixup(testServer, localeName, invariantExpr);

            AssertFixupResult(sendToClientData, localeExpr);
        }

        // Get errors  in other locales.
        [Theory]
        [InlineData("1.2", "en-US")] // success case
        [InlineData("1,2", "fr-FR")] // success case
        [InlineData("1.2", "fr-FR", "Caractères inattendus. La formule contient « 2 » a...;Un opérateur était attendu. Nous attendons un opér...;Opérande attendu. La formule ou l’expression atten...;L’opérateur « . » ne peut pas être utilisé sur les...")] // error - no dups, all French
        [InlineData("1 + foo", "fr-FR", "Le nom n’est pas valide. « foo » n’est pas reconnu...")] // binding
        [InlineData("1 + foo", "en-US", "Name isn't valid. 'foo' isn't recognized.")] // binding
        [InlineData("1 + ", "en-US", "Expected an operand. The formula or expression exp...;Invalid argument type. Expecting one of the follow...")] // Parse error
        [InlineData("1 + ", "fr-FR", "Opérande attendu. La formule ou l’expression atten...;Type d’argument non valide. L’une des valeurs suiv...")] // Parse error
        public void ErrorIsLocalized(string expression, string localeName, string expectedError = null)
        {
            var expectedErrors = (expectedError == null) ?
                new string[0] :
                expectedError.Split(';');

            ErrorIsLocalized_DidOpen(expression, localeName, expectedErrors);
            ErrorIsLocalized_DidChange(expression, localeName, expectedErrors);
        }

        private void AssertErrors(string[] expectedErrors, List<string> sendToClientData)
        {
            Assert.Single(sendToClientData);
            var sentToClientData = sendToClientData[0];
            var json = JsonSerializer.Deserialize<JsonRpcPublishDiagnosticsNotification>(sentToClientData);

            AssertErrors(expectedErrors, json);
        }

        private static string Truncate(string s, int len)
        {
            if (s.Length <= len)
            {
                return s;
            }

            return s.Substring(0, len) + "...";
        }

        private void AssertErrors(string[] expectedErrors, JsonRpcPublishDiagnosticsNotification json)
        {
            var msgs = json.@params.diagnostics.ToArray();
            msgs.OrderBy(m => m.ToString()).ToArray(); // make deterministic for tests.
            Assert.Equal(expectedErrors.Length, msgs.Length);
            for (int i = 0; i < expectedErrors.Length; i++)
            {
                // Some error message sare are too long, just truncate first part for comparison.
                var truncatedMessage = Truncate(msgs[i].message, 50);
                Assert.Equal(expectedErrors[i], truncatedMessage);
            }
        }

        // Verify errors with didOpen message.
        private void ErrorIsLocalized_DidOpen(string expression, string localeName, string[] expectedErrors)
        {
            var sendToClientData = new List<string>();
            var scopeFactory = new PowerFxScopeFactory();
            var testServer = new TestLanguageServer(sendToClientData.Add, scopeFactory);

#pragma warning disable CS0618
            testServer.OnDataReceived(JsonSerializer.Serialize(new
#pragma warning restore CS0618
            {
                jsonrpc = "2.0",
                id = "123456",
                method = "textDocument/didOpen",
                @params = new
                {
                    textDocument = new
                    {
                        uri = "powerfx://field_designer?entityLogicalName=" + _entityLogicalName + "&localeName=" + localeName,
                        version = 4,
                        Text = expression
                    }
                }
            }));

            AssertErrors(expectedErrors, sendToClientData);
        }

        // Verify errors with didChange
        internal void ErrorIsLocalized_DidChange(string expression, string localeName, string[] expectedErrors)
        {
            var sendToClientData = new List<string>();
            var scopeFactory = new PowerFxScopeFactory();
            var testServer = new TestLanguageServer(sendToClientData.Add, scopeFactory);

#pragma warning disable CS0618
            testServer.OnDataReceived(JsonSerializer.Serialize(new
#pragma warning restore CS0618
            {
                jsonrpc = "2.0",
                id = "123456",
                method = "textDocument/didChange",
                @params = new
                {
                    textDocument = new
                    {
                        uri = "powerfx://field_designer?entityLogicalName=" + _entityLogicalName + "&localeName=" + localeName,
                        version = 4,
                    },
                    contentchanges = new[]
                    {
                        new 
                        {
                            text = expression
                        }
                    }
                }
            }));

            AssertErrors(expectedErrors, sendToClientData);
        }

#pragma warning disable SA1300 // Elements should begin with uppercase letter

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

        public class JsonRpcPublishDiagnosticsNotification
        {
            public string jsonrpc { get; set; } = string.Empty;

            public string method { get; set; } = string.Empty;

            public PublishDiagnosticsParams @params { get; set; } = new PublishDiagnosticsParams();
        }

        public class PublishDiagnosticsParams
        {
            /// <summary>
            /// The URI for which diagnostic information is reported.
            /// </summary>
            public string uri { get; set; }

            /// <summary>
            /// An array of diagnostic information items.
            /// </summary>
            public Diagnostic[] diagnostics { get; set; }
        }

        [DebuggerDisplay("{message}")]
        public class Diagnostic
        {
            /// <summary>
            /// The diagnostic's message.
            /// </summary>
            public string message { get; set; }

            /// <summary>
            /// A diagnostic instance may represent an error, warning, hint, etc., and each may impose different
            /// behavior on an editor.  This member indicates the diagnostic's kind.
            /// </summary>
            public int severity { get; set; }
        }

#pragma warning restore SA1300 // Elements should begin with uppercase letter

        public class TestLanguageServer : LanguageServer
        {
            public List<string> _sendToClientData = new List<string>();

#pragma warning disable CS0618

            public TestLanguageServer(SendToClient sendToClient, IPowerFxScopeFactory scopeFactory)
                : base(sendToClient, scopeFactory)
#pragma warning restore CS0618
            {
            }

            public int TestGetCharPosition(string expression, int position) => GetCharPosition(expression, position);

            public int TestGetPosition(string expression, int line, int character) => GetPosition(expression, line, character);
        }
    }
}
