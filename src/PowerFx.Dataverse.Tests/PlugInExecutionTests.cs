//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Types;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class PlugInExecutionTests
    {
        [SkippableFact]
        public async Task PlugInExecutionTest()
        {
            // https://docs.microsoft.com/en-us/power-apps/developer/data-platform/xrm-tooling/use-connection-strings-xrm-tooling-connect                        
            string cx = null; // $"Url=https://aurorabapenv67c10.crm10.dynamics.com/; Username={username}; Password={password}; authtype=OAuth";

            if (cx == null)
            {
                Skip.If(true, $"Skipping Live Dataverse tests. Set {cx} env var.");
                throw new NotImplementedException();
            }

            IOrganizationService svcClient = new ServiceClient(cx) { EnableAffinityCookie = true, UseWebApi = false };
            DataverseService dvService = new DataverseService(svcClient);
            DataverseConnection dvConnection = new DataverseConnection(svcClient);
            
            CustomApiSignature plugIn = await dvService.GetPlugInAsync("lucgen1", CancellationToken.None).ConfigureAwait(false);

            dvService.AddPlugIn(plugIn);
            
            RuntimeConfig config = new RuntimeConfig().AddService(dvConnection);            
            FormulaValue result = await dvService.ExecutePlugInAsync(config, plugIn.Api.uniquename, FormulaValue.NewRecordFromFields(new NamedValue("x", FormulaValue.New("str")), new NamedValue("y", FormulaValue.New(29))), CancellationToken.None).ConfigureAwait(false);

            RecordValue record = Assert.IsAssignableFrom<RecordValue>(result);
            Assert.Single(record.Fields);

            StringValue str = Assert.IsType<StringValue>(record.GetField("z"));
            Assert.Equal("str58", str.Value);
        }
    }
}
