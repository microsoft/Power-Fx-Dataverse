//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.AccessControl;
using System.ServiceModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Dataverse.EntityMock;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class CustomApiTests
    {
        #region Signature helpers
        private static CustomApiEntity NewApi(string name)
        {
            return new CustomApiEntity
            {
                 name = name,
                 uniquename = name,
                 isfunction = true
            };
        }
        private static CustomApiRequestParam NewIn(string name, CustomApiParamType type)
        {
            return new CustomApiRequestParam
            {
                 name =name,
                 uniquename = name,
                 type = type
            };
        }

        private static CustomApiResponse NewOut(string name, CustomApiParamType type)
        {
            return new CustomApiResponse
            {
                name = name,
                uniquename = name,
                type = type
            };
        }

        private static CustomApiSignature NewSig(string name, params IParameterType[] parameters)
        {
            var sig = new CustomApiSignature
            {
                Api = NewApi(name),
                Inputs = parameters.OfType<CustomApiRequestParam>().ToArray(),
                Outputs = parameters.OfType<CustomApiResponse>().ToArray(),
            };
            return sig;
        }

        #endregion // Signature helpers

        public static readonly CustomApiSignature _api1Signature =
                NewSig("api1",
                NewIn("p1", CustomApiParamType.Integer),
                NewOut("out", CustomApiParamType.Integer));

        [Fact]
        public async Task CustomApiTestsStatic()
        {
            var (dvc, el) = PluginExecutionTests.CreateMemoryForRelationshipModels();
            
            dvc.AddPlugin(_api1Signature);

            var engine = new RecalcEngine();

            var expr = "api1({p1:19}).out";
            var check = engine.Check(expr, symbolTable: dvc.Symbols);
                        
            Assert.True(check.IsSuccess);

            // Now invoke it. 
            var eval = check.GetEvaluator();

            var mockExec = new MockExecute
            {
                Work = (req) =>
                {
                    Assert.Equal("api1", req.RequestName);
                    var obj = req.Parameters["p1"];
                    Assert.Equal(19, (int)obj);

                    var orgResp = new OrganizationResponse();
                    orgResp["out"] = 38;

                    return orgResp;
                }
            };

            var rc = new RuntimeConfig(dvc.SymbolValues);
            rc.AddDataverseExecute(mockExec);

            var result = await eval.EvalAsync(default, rc);

            Assert.Equal(38, result.ToDouble());

            // "str38" 
            // Success!!!
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
