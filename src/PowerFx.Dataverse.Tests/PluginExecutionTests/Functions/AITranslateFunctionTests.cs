//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests
{

    public class AITranslateFunctionTests
    {
        // FAils if config.EnableAIFunctions() is not called.
        [Theory]
        [InlineData(true, "AITranslate(\"yo soy John\")")]
        [InlineData(false, "AITranslate(\"yo soy John\")")]
        [InlineData(true, "AITranslate(\"yo soy John\", \"fr\")")]
        [InlineData(false, "AITranslate(\"yo soy John\", \"fr\")")]
        public void Missing(bool enable, string input)
        {
            var config = new PowerFxConfig();
            if (enable)
            {
                config.EnableAIFunctions();
            }
            var engine = new RecalcEngine(config);

            var result = engine.Check(input);
            Assert.Equal(enable, result.IsSuccess);
        }

        [Fact]
        public async Task Success()
        {
            var config = new PowerFxConfig();
            config.EnableAIFunctions();

            var engine = new RecalcEngine(config);

            var rc = new RuntimeConfig();

            var client = new MockExecute();
            rc.AddDataverseExecute(client);


            client.Work = (req) =>
            {
                // Validate parameters
                Assert.Equal("AITranslate", req.RequestName);
                Assert.Equal("Yo soy John", req.Parameters["Text"]);

                var resp = new OrganizationResponse
                {
                    ResponseName = "AITranslate"
                };
                resp["TranslatedText"] = "I am John";
                return resp;
            };

            var result = await engine.EvalAsync("AITranslate(\"Yo soy John\")", default, runtimeConfig: rc);

            Assert.Equal("I am John", result.ToObject());
        }

        [Fact]
        public async Task SuccessWithLanguage()
        {
            var config = new PowerFxConfig();
            config.EnableAIFunctions();

            var engine = new RecalcEngine(config);

            var rc = new RuntimeConfig();

            var client = new MockExecute();
            rc.AddDataverseExecute(client);


            client.Work = (req) =>
            {
                // Validate parameters
                Assert.Equal("AITranslate", req.RequestName);
                Assert.Equal("My name is John", req.Parameters["Text"]);
                Assert.Equal("fr", req.Parameters["TargetLanguage"]);

                var resp = new OrganizationResponse
                {
                    ResponseName = "AITranslate"
                };
                resp["TranslatedText"] = "Je m'appelle John";
                return resp;
            };

            var result = await engine.EvalAsync("AITranslate(\"My name is John\", \"fr\")", default, runtimeConfig: rc);

            Assert.Equal("Je m'appelle John", result.ToObject());
        }

        [Theory]
        [InlineData("AITranslate(\"yo soy John\")")]
        [InlineData("AITranslate(\"yo soy John\", \"fr\")")]
        public async Task Failure(string input)
        {
            var config = new PowerFxConfig();
            config.EnableAIFunctions();

            var engine = new RecalcEngine(config);

            var rc = new RuntimeConfig();

            var client = new MockExecute();
            rc.AddDataverseExecute(client);

            var msg = "simulated failure";

            client.Work = (req) =>
            {
                throw new InvalidOperationException(msg);
            };

            var result = await engine.EvalAsync(input, default, runtimeConfig: rc);

            var errors = (ErrorValue)result;
            Assert.Equal(1, errors.Errors.Count);
            var error = errors.Errors[0];
            Assert.Contains(msg, error.Message);
            Assert.Equal(ErrorSeverity.Severe, error.Severity);
        }

        public class MockExecute : IDataverseExecute
        {
            public Func<OrganizationRequest, OrganizationResponse> Work;

            public async Task<DataverseResponse<OrganizationResponse>> ExecuteAsync(OrganizationRequest request, CancellationToken cancellationToken = default)
            {
                return await DataverseResponse<OrganizationResponse>.RunAsync(
                    async () => this.Work(request),
                    "method");
            }
        }
    }
}
