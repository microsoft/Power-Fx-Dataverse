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
        public void RunSuccess()
        {
            var resp = DataverseResponse<FakeMessage>.RunAsync(
                async () => new FakeMessage { Value = "ok" }, "op").Result;

            Assert.False(resp.HasError);
            Assert.NotNull(resp.Response);

            Assert.Equal("ok", resp.Response.Value);
        }

        [Fact]
        public void RunSoftError()
        {
            var exceptionMessage = "Inject test failure";
            var opMessage = "my op";

            var resp = DataverseResponse<FakeMessage>.RunAsync(
                async () => throw new FaultException<OrganizationServiceFault>(
                    new OrganizationServiceFault(),
                    new FaultReason(exceptionMessage)), opMessage).Result;

            Assert.True(resp.HasError);
            Assert.Null(resp.Response);

            Assert.Contains(exceptionMessage, resp.Error);
            Assert.Contains(opMessage, resp.Error);
        }

        // Non-async callback. 
        [Fact]
        public async Task RunHardError1()
        {
            await Assert.ThrowsAsync<MyException>(async () => await DataverseResponse<FakeMessage>.RunAsync(() => throw new MyException("unknown ex"), "op").ConfigureAwait(false)).ConfigureAwait(false);
        }

        // Async callback
        [Fact]
        public async Task RunHardError2()
        {
            await Assert.ThrowsAsync<MyException>(async () =>await DataverseResponse<FakeMessage>.RunAsync(async () => throw new MyException("unknown ex"), "op").ConfigureAwait(false)).ConfigureAwait(false);
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
