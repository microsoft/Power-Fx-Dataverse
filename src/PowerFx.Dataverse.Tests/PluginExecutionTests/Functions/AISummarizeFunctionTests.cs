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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    [TestClass]
    public class AISummarizeFunctionTests
    {
        // FAils if config.EnableAIFunctions() is not called.
        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void Missing(bool enable)
        {
            var config = new PowerFxConfig();        
            if (enable)
            {
                config.EnableAIFunctions();
            }
            var engine = new RecalcEngine(config);
            
            var result = engine.Check("AISummarize(\"very long string\")");
            Assert.AreEqual(enable, result.IsSuccess);
        }

        [TestMethod]
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
                Assert.AreEqual("SummarizeText", req.RequestName);
                Assert.AreEqual("very long string", req.Parameters["InputText"]);
                Assert.IsNotNull(req.Parameters["source"]);


                var resp = new OrganizationResponse
                {
                    ResponseName = "SummarizeText"
                };
                resp["SummarizedText"] = "short string";
                return resp;
            };
            
            var result = await engine.EvalAsync("AISummarize(\"very long string\")", default, runtimeConfig: rc);

            Assert.AreEqual("short string", result.ToObject());
        }

        [TestMethod]
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

            var result = await engine.EvalAsync("AISummarize(\"very long string\")", default, runtimeConfig: rc);
            
            var errors = (ErrorValue)result;
            Assert.AreEqual(1, errors.Errors.Count);
            var error = errors.Errors[0];
            Assert.IsTrue(error.Message.Contains(msg));
            Assert.AreEqual(ErrorSeverity.Severe, error.Severity);
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
