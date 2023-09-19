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

    public class AISummarizeRecordFunctionTests
    {
        // FAils if config.EnableAIFunctions() is not called.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Missing(bool enable)
        {
            var id = Guid.NewGuid();
            var config = new PowerFxConfig();
            if (enable)
            {
                config.EnableAIFunctions();
            }
            var engine = new RecalcEngine(config);

            var result = engine.Check($"AISummarizeRecord(\"Account\", \"{id}\")");
            Assert.Equal(enable, result.IsSuccess);
        }

        [Fact]
        public async Task Success()
        {
            var id = Guid.NewGuid();
            var config = new PowerFxConfig();
            config.EnableAIFunctions();

            var engine = new RecalcEngine(config);

            var rc = new RuntimeConfig();

            var client = new MockExecute();
            rc.AddDataverseExecute(client);

            client.Work = (req) =>
            {
                // Validate parameters
                Assert.Equal("AISummarizeRecord", req.RequestName);
                Assert.Equal("Account", req.Parameters["EntityLogicalName"]);
                Assert.Equal(id.ToString(), req.Parameters["Id"]);

                var resp = new OrganizationResponse
                {
                    ResponseName = "AISummarizeRecord"
                };
                resp["SummarizedText"] = "string";
                return resp;
            };

            var result = await engine.EvalAsync($"AISummarizeRecord(\"Account\", \"{id}\")", default, runtimeConfig: rc);
            Assert.Equal("string", result.ToObject());
        }

        [Fact]
        public async Task Failure()
        {
            var id = Guid.NewGuid();

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

            var result = await engine.EvalAsync($"AISummarizeRecord(\"Account\", \"{id}\")", default, runtimeConfig: rc);

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
