// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

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
                uniquename = name,
                type = type
            };
        }

        private static CustomApiResponse NewOut(string name, CustomApiParamType type)
        {
            return new CustomApiResponse
            {
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

        #endregion Signature helpers

        public static readonly CustomApiSignature _api1Signature = NewSig("api1", NewIn("p1", CustomApiParamType.Integer), NewOut("out", CustomApiParamType.Integer));

        // No inputs.
        public static readonly CustomApiSignature _apiNonputsSignature = NewSig("apiNoInputs", NewOut("out", CustomApiParamType.Integer));

        // Normal Check and Eval.
        [Fact]
        public async Task CustomApiTestsStatic()
        {
            var (dvc, el) = PluginExecutionTests.CreateMemoryForRelationshipModels();

            // Plugins are imported into "Environment" namespace by default.
            dvc.AddPlugin(_api1Signature);

            var engine = new RecalcEngine();

            var expr = "Environment.api1({p1:19}).out";
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
        }

        // An error parameter gets short circuited and won't call.
        [Fact]
        public async Task Error()
        {
            var (dvc, el) = PluginExecutionTests.CreateMemoryForRelationshipModels();

            dvc.AddPlugin(_api1Signature);

            var engine = new RecalcEngine();

            var expr = "Environment.api1({p1:1/0}).out";
            var check = engine.Check(expr, symbolTable: dvc.Symbols);

            Assert.True(check.IsSuccess);

            // Now invoke it.
            var eval = check.GetEvaluator();

            bool called = false;
            var mockExec = new MockExecute
            {
                Work = (req) =>
                {
                    // Should never call.
                    // set flag instead of throwing - in case things are catching the exception.
                    called = true;
                    return new OrganizationResponse();
                }
            };

            var rc = new RuntimeConfig(dvc.SymbolValues);
            rc.AddDataverseExecute(mockExec);

            var result = await eval.EvalAsync(default, rc);
            Assert.False(called);

            Assert.IsType<ErrorValue>(result);
        }

        // Server-side errors are caught.
        [Fact]
        public async Task ServerError()
        {
            var (dvc, el) = PluginExecutionTests.CreateMemoryForRelationshipModels();

            dvc.AddPlugin(_api1Signature);

            var engine = new RecalcEngine();

            var expr = "Environment.api1({p1:19}).out";
            var check = engine.Check(expr, symbolTable: dvc.Symbols);

            Assert.True(check.IsSuccess);

            // Now invoke it.
            var eval = check.GetEvaluator();

            var msg = $"xyz"; // some message from server.

            var mockExec = new MockExecute
            {
                Work = (req) =>
                {
                    throw new InvalidOperationException(msg);
                }
            };

            var rc = new RuntimeConfig(dvc.SymbolValues);
            rc.AddDataverseExecute(mockExec);

            var result = await eval.EvalAsync(default, rc);

            Assert.IsType<ErrorValue>(result);

            // Error should have message from server.
            var actualMsg = ((ErrorValue)result).Errors[0].Message;
            Assert.Contains(msg, actualMsg);
        }

        [Fact]
        public async Task NoInputsSignature()
        {
            var (dvc, el) = PluginExecutionTests.CreateMemoryForRelationshipModels();

            // Plugins are imported into "Environment" namespace by default.
            dvc.AddPlugin(_apiNonputsSignature);

            var engine = new RecalcEngine();

            var expr = "Environment.apiNoInputs({p1:19}).out";
            var check = engine.Check(expr, symbolTable: dvc.Symbols);
            Assert.False(check.IsSuccess);

            expr = "Environment.apiNoInputs().out";
            check = engine.Check(expr, symbolTable: dvc.Symbols);
            Assert.True(check.IsSuccess);

            // Now invoke it.
            var eval = check.GetEvaluator();

            var mockExec = new MockExecute
            {
                Work = (req) =>
                {
                    Assert.Equal("apiNoInputs", req.RequestName);
                    Assert.Empty(req.Parameters);

                    var orgResp = new OrganizationResponse();
                    orgResp["out"] = 38;

                    return orgResp;
                }
            };

            var rc = new RuntimeConfig(dvc.SymbolValues);
            rc.AddDataverseExecute(mockExec);

            var result = await eval.EvalAsync(default, rc);

            Assert.Equal(38, result.ToDouble());
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

        [Fact]
        public void CustomApiTableMarshallerTest()
        {
            var entity = new Entity("local");
            entity["name"] = "Test Account";

            var entityCollection = new EntityCollection(new List<Entity> { entity }) { EntityName = "local" };

            var xrmMetadata = new MockXrmMetadataProvider(MockModels.LocalModel);
            var cdsEntityMetadataProvider = new CdsEntityMetadataProvider(xrmMetadata);

            var dvc = new DataverseConnection(new TestOrganizationService(), cdsEntityMetadataProvider);
            dvc.AddTable("local", "local");

            var table = dvc.Marshal(entityCollection);

            Assert.IsAssignableFrom<IDelegatableTableValue>(table);
        }
    }
}
