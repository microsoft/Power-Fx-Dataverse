//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.ServiceModel;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class DataverseResponseTests
    {
        [Fact]
        public void Fail()
        {
            var resp = DataverseResponse<FakeMessage>.NewError("fail");
            Assert.True(resp.HasError);
            Assert.Null(resp.Response);
        }

        [Fact]
        public void Success()
        {
            var resp = new DataverseResponse<FakeMessage>(new FakeMessage { Value = "ok" });
            Assert.False(resp.HasError);
            Assert.NotNull(resp.Response);

            Assert.Equal("ok", resp.Response.Value);
        }

        [Fact]
        public async Task RunSuccess()
        {
            var resp = await  DataverseResponse<FakeMessage>.RunAsync(static async () => new FakeMessage { Value = "ok" }, "op");

            Assert.False(resp.HasError);
            Assert.NotNull(resp.Response);

            Assert.Equal("ok", resp.Response.Value);
        }

        [Fact]
        public async Task RunSoftError()
        {
            var exceptionMessage = "Inject test failure";
            var opMessage = "my op";

            var resp = await DataverseResponse<FakeMessage>.RunAsync(async () => throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault(), new FaultReason(exceptionMessage)), opMessage);

            Assert.True(resp.HasError);
            Assert.Null(resp.Response);

            Assert.Contains(exceptionMessage, resp.Error);
            Assert.Contains(opMessage, resp.Error);
        }

        // Non-async callback. 
        [Fact]
        public async Task RunHardError1()
        {
            await Assert.ThrowsAsync<MyException>(static async () => await DataverseResponse<FakeMessage>.RunAsync(static () => throw new MyException("unknown ex"), "op"));
        }

        // Async callback
        [Fact]
        public async Task RunHardError2()
        {
            await Assert.ThrowsAsync<MyException>(static async () => await DataverseResponse<FakeMessage>.RunAsync(static async () => throw new MyException("unknown ex"), "op"));
        }

        // Not a dataverse exception
        private class MyException : Exception
        {
            public MyException(string message) : base(message)
            {
            }
        }

        private class FakeMessage
        {
            public string Value;
        }
    }
}
