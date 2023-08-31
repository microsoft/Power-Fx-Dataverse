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

    public class AIClassifyFunctionTests
    {
        // FAils if config.EnableAIFunctions() is not called.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Missing(bool enable)
        {
            var config = new PowerFxConfig();
            if (enable)
            {
                config.EnableAIFunctions();
            }
            var engine = new RecalcEngine(config);

            var result = engine.Check("AIClassify(\"very long string\", [\"string\", \"int\"])");
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
                Assert.Equal("AIClassify", req.RequestName);
                Assert.Equal("very long string", req.Parameters["Text"]);
                Assert.Equal("stringint", getConcatenatedStrings((TableValue)req.Parameters["Categories"]));

                var resp = new OrganizationResponse
                {
                    ResponseName = "AIClassify"
                };
                resp["Classification"] = "string";
                return resp;
            };

            var result = await engine.EvalAsync("AIClassify(\"very long string\", [\"string\", \"int\"])", default, runtimeConfig: rc);
            Assert.Equal("string", result.ToObject());
        }

        protected string getConcatenatedStrings(TableValue table)
        {
            var returnVal = "";
            foreach (var row in table.Rows)
            {
                RecordValue val = row.Value;
                string currStr = ((StringValue)val.GetField("Value")).Value;
                returnVal += currStr;
            }
            return returnVal;
        }

        [Fact]
        public async Task Failure()
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

            var result = await engine.EvalAsync("AIClassify(\"very long string\", [\"string\", \"int\"])", default, runtimeConfig: rc);

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
