//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.Types;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using OptionSetValue = Microsoft.Xrm.Sdk.OptionSetValue;

namespace Microsoft.PowerFx.Dataverse.Tests
{

    [TestClass]
    public class DataverseResponseTests
    {
        [TestMethod]
        public void Fail()
        {
            var resp = DataverseResponse<FakeMessage>.NewError("fail");
            Assert.IsTrue(resp.HasError);
            Assert.IsNull(resp.Response);
        }

        [TestMethod]
        public void Success()
        {
            var resp = new DataverseResponse<FakeMessage>(new FakeMessage { Value = "ok" });
            Assert.IsFalse(resp.HasError);
            Assert.IsNotNull(resp.Response);

            Assert.AreEqual("ok", resp.Response.Value);
        }

        [TestMethod]
        public void RunSuccess()
        {
            var resp = DataverseResponse<FakeMessage>.RunAsync(
                async () => new FakeMessage { Value = "ok" }, "op").Result;

            Assert.IsFalse(resp.HasError);
            Assert.IsNotNull(resp.Response);

            Assert.AreEqual("ok", resp.Response.Value);
        }

        [TestMethod]
        public void RunSoftError()
        {
            var exceptionMessage = "Inject test failure";
            var opMessage = "my op";

            var resp = DataverseResponse<FakeMessage>.RunAsync(
                async () => throw new FaultException<OrganizationServiceFault>(
                    new OrganizationServiceFault(),
                    new FaultReason(exceptionMessage)), opMessage).Result;

            Assert.IsTrue(resp.HasError);
            Assert.IsNull(resp.Response);

            Assert.IsTrue(resp.Error.Contains(exceptionMessage));
            Assert.IsTrue(resp.Error.Contains(opMessage));
        }

        // Non-async callback. 
        [TestMethod]
        public async Task RunHardError1()
        {
            await Assert.ThrowsExceptionAsync<MyException>(async () => await DataverseResponse<FakeMessage>.RunAsync(() => throw new MyException("unknown ex"), "op").ConfigureAwait(false)).ConfigureAwait(false);
        }

        // Async callback
        [TestMethod]
        public async Task RunHardError2()
        {
            await Assert.ThrowsExceptionAsync<MyException>(async () =>await DataverseResponse<FakeMessage>.RunAsync(async () => throw new MyException("unknown ex"), "op").ConfigureAwait(false)).ConfigureAwait(false);
        }

        // Not a dataverse exception
        class MyException : Exception
        {
            public MyException(string message) : base(message)
            {
            }
        }

        class FakeMessage
        {
            public string Value;
        }
    }


}