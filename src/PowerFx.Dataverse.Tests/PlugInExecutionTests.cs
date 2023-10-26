//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Microsoft.PowerFx.Connectors;
using Microsoft.PowerFx.Types;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class PlugInExecutionTests
    {
        // https://docs.microsoft.com/en-us/power-apps/developer/data-platform/xrm-tooling/use-connection-strings-xrm-tooling-connect                        
        // $"Url=https://aurorabapenv9984a.crm10.dynamics.com/; Username={username}; Password={pwd}; authtype=OAuth";
        private static readonly string cx = $"Url=https://aurorabapenv9984a.crm10.dynamics.com/; Username=aurorauser09@capintegration01.onmicrosoft.com; Password=9wS29#&Wr; authtype=OAuth";

        [SkippableFact]
        public async Task PlugInExecutionTest()
        {            
            if (cx == null)
            {
                Skip.If(true, $"Skipping Live Dataverse tests. Set {cx} env var.");
                throw new NotImplementedException();
            }

            IOrganizationService svcClient = new ServiceClient(cx) { EnableAffinityCookie = true, UseWebApi = false };
            DataverseService dvService = new DataverseService(svcClient);
            DataverseConnection dvConnection = new DataverseConnection(dvService, new XrmMetadataProvider(svcClient));

            // PlugIn is defined as:
            // Input : x: string, y: number
            // Output: z: string
            // Expression: { z: x & Text(y*2) }
            CustomApiSignature plugIn = await dvService.GetPlugInAsync("lucgen1", CancellationToken.None).ConfigureAwait(false);

            dvService.AddPlugIn(plugIn);
            
            RuntimeConfig runtimeConfig = new RuntimeConfig().AddService(dvConnection);            
            FormulaValue result = await dvService.ExecutePlugInAsync(runtimeConfig, plugIn.Api.uniquename, FormulaValue.NewRecordFromFields(new NamedValue("x", FormulaValue.New("str")), new NamedValue("y", FormulaValue.New(29))), CancellationToken.None).ConfigureAwait(false);

            RecordValue record = Assert.IsAssignableFrom<RecordValue>(result);
            Assert.Single(record.Fields);

            StringValue str = Assert.IsType<StringValue>(record.GetField("z"));
            Assert.Equal("str58", str.Value);
        }

        [SkippableFact]
        public async Task PlugInExecutionTest2()
        {
            if (cx == null)
            {
                Skip.If(true, $"Skipping Live Dataverse tests. Set {cx} env var.");
                throw new NotImplementedException();
            }

            PowerFxConfig pfxConfig = new PowerFxConfig();
            IOrganizationService svcClient = new ServiceClient(cx) { EnableAffinityCookie = true, UseWebApi = false };            
            DataverseConnection dvConnection = new DataverseConnection(svcClient);

            // lucgen1 PlugIn =  Input : x: string, y: number / Output: z: string / Expression: { z: x & Text(y*2) }
            CustomApiSignature plugIn = await dvConnection.DataverseService.GetPlugInAsync("lucgen1", CancellationToken.None).ConfigureAwait(false);            
            RuntimeConfig runtimeConfig = await new RuntimeConfig().AddService(dvConnection).AddPlugInAsync(pfxConfig, "PlugIn", plugIn).ConfigureAwait(false);

            RecalcEngine engine = new RecalcEngine(pfxConfig);
            FormulaValue result = await engine.EvalAsync(@"PlugIn.lucgen1(""str"", 19).z", CancellationToken.None, new ParserOptions() { AllowsSideEffects = true }, runtimeConfig: runtimeConfig).ConfigureAwait(false);
            Assert.Equal("str38", (result as StringValue).Value);
        }

        [SkippableFact]
        public async Task PlugInExecutionTest3()
        {
            if (cx == null)
            {
                Skip.If(true, $"Skipping Live Dataverse tests. Set {cx} env var.");
                throw new NotImplementedException();
            }

            OpenApiDocument msnWeatherDoc = ReadSwagger(@"Swagger\MSNWeather.json");
            using var httpClient = new HttpClient();           
            using var client = new PowerPlatformConnectorClient("https://tip1002-002.azure-apihub.net", "b29c41cf-173b-e469-830b-4f00163d296b", "b17aa2032f6742dd971437fd4295040b", () => "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsIng1dCI6IjlHbW55RlBraGMzaE91UjIybXZTdmduTG83WSIsImtpZCI6IjlHbW55RlBraGMzaE91UjIybXZTdmduTG83WSJ9.eyJhdWQiOiJodHRwczovL2FwaWh1Yi5henVyZS5jb20iLCJpc3MiOiJodHRwczovL3N0cy53aW5kb3dzLm5ldC85MWJlZTNkOS0wYzE1LTRmMTctODYyNC1jOTJiYjhiMzZlYWQvIiwiaWF0IjoxNjk4MzMwNTYwLCJuYmYiOjE2OTgzMzA1NjAsImV4cCI6MTY5ODMzNTg2MiwiYWNyIjoiMSIsImFpbyI6IkFUUUF5LzhVQUFBQUtucHZ1YWNjbWQ3Kzl4NmZRV29QcS9xMXQ1N2lReThiS0d4WXM5R0hHcHBxUkh3SWoySWx4S3FLQ3B5WVJSS1AiLCJhbXIiOlsicHdkIl0sImFwcGlkIjoiYThmN2E2NWMtZjViYS00ODU5LWIyZDYtZGY3NzJjMjY0ZTlkIiwiYXBwaWRhY3IiOiIwIiwiZmFtaWx5X25hbWUiOiJ1c2VyMDkiLCJnaXZlbl9uYW1lIjoiYXVyb3JhIiwiaXBhZGRyIjoiOTAuMTA0LjczLjIwMyIsIm5hbWUiOiJhdXJvcmF1c2VyMDkiLCJvaWQiOiIzMmJhMTFiZC1mZjE1LTQ2ZjctYmQzMy02YjQ4MWU1ZjVjN2UiLCJwdWlkIjoiMTAwMzIwMDEzQjlCODhDNCIsInJoIjoiMC5BVzhBMmVPLWtSVU1GMC1HSk1rcnVMTnVyVjg4QmY2U05oUlBydkx1TlB3SUhLNXZBTEUuIiwic2NwIjoiUnVudGltZS5BbGwiLCJzdWIiOiJaeWlBRHY0Yk9JXzFldWNsNko2dzd6ZHp3bFZBMmktWlYtZ25PQUtGRVQ0IiwidGlkIjoiOTFiZWUzZDktMGMxNS00ZjE3LTg2MjQtYzkyYmI4YjM2ZWFkIiwidW5pcXVlX25hbWUiOiJhdXJvcmF1c2VyMDlAY2FwaW50ZWdyYXRpb24wMS5vbm1pY3Jvc29mdC5jb20iLCJ1cG4iOiJhdXJvcmF1c2VyMDlAY2FwaW50ZWdyYXRpb24wMS5vbm1pY3Jvc29mdC5jb20iLCJ1dGkiOiJoWWZwM2p2eGFFV01TbU5vYThFY0FBIiwidmVyIjoiMS4wIn0.enQPDIEf3FvKCDcPLZmyBexF5Gb9riNtZppbVjIyKYiPuuTOa5cnMoPK2LbCMR5qWucQDR2t-ELb2zl7hHtahkLvzH-OhTJaEfeWOJDVwDphkqrlJqxp2cI1LBk8RbKSAtpSCMG0evzegvqOvpe1ekwFajkhTKR49PAelQqZ1lh_yYI3kuhq4Mg9YH5_k5RGZFjdR5aeDULU94RrxrMihkD5q0s0ykb3BHVHPXkRfWf6MnsoN1snkLnGFLOBK4d1H40Ybr-hbxs1f2OjavqpqBrlhgk7ECi3DSSjMaJHligP_BuJEDK-w_rF0D7pkuELB4wuBvyQpio1HPBh6CG4yA", httpClient) { SessionId = Guid.NewGuid().ToString() };

            PowerFxConfig pfxConfig = new PowerFxConfig();
            pfxConfig.AddActionConnector("MsnWeather", msnWeatherDoc);

            IOrganizationService svcClient = new ServiceClient(cx) { EnableAffinityCookie = true, UseWebApi = false };            
            DataverseConnection dvConnection = new DataverseConnection(svcClient);
           
            RuntimeConfig runtimeConfig = new RuntimeConfig().AddService(dvConnection);                  
            PlugInRuntimeContext runtimeContext = new PlugInRuntimeContext(runtimeConfig, dvConnection.DataverseService);

            // MSN Weather connector
            runtimeContext.AddHttpInvoker("MsnWeather", client);
            runtimeConfig.AddService<BaseRuntimeConnectorContext>(runtimeContext);

            // lucgen1 PlugIn =  Input : x: string, y: decimal / Output: z: string / Expression: { z: x & Text(y*2) }
            await runtimeConfig.AddPlugInAsync(pfxConfig, "PlugIn", "lucgen1").ConfigureAwait(false);

            // lucgen2 PlugIn =  Input : x: string, y: decimal / Output: t: string / Expression: { t: Text(y*5) & x & x }
            await runtimeConfig.AddPlugInAsync(pfxConfig, "PlugIn", "lucgen2").ConfigureAwait(false);            

            RecalcEngine engine = new RecalcEngine(pfxConfig);
            CheckResult checkResult = engine.Check(@"PlugIn.lucgen1(""str"", 19).z &  ""-"" & PlugIn.lucgen2(""xyz"", 71).t & ""-"" & Text(MsnWeather.CurrentWeather(""Paris"", ""I"").responses.weather.current.temp)", new ParserOptions() { AllowsSideEffects = true });
            Assert.True(checkResult.IsSuccess);

            FormulaValue result = await checkResult.GetEvaluator().EvalAsync(CancellationToken.None, runtimeConfig).ConfigureAwait(false);
            Assert.Equal("str38-355xyzxyz-60", (result as StringValue).Value);
        }

        public static Stream GetStream(string name)
        {
            if (!Path.IsPathRooted(name))
            {
                string assemblyNamespace = "Microsoft.PowerFx.Dataverse.Tests";
                string fullName = assemblyNamespace + "." + name.Replace('\\', '.');

                var assembly = typeof(PlugInExecutionTests).Assembly;
                var stream = assembly.GetManifestResourceStream(fullName);

                Assert.NotNull(stream);
                return stream;
            }

            return File.OpenRead(name);
        }

        public static string ReadAllText(string name)
        {
            using (var stream = GetStream(name))
            using (var textReader = new StreamReader(stream))
            {
                return textReader.ReadToEnd();
            }
        }

        // Get a swagger file from the embedded resources. 
        public static OpenApiDocument ReadSwagger(string name)
        {
            using (var stream = GetStream(name))
            {
                var doc = new OpenApiStreamReader().Read(stream, out OpenApiDiagnostic diag);

                if ((doc == null || doc.Paths == null || doc.Paths.Count == 0) && diag != null && diag.Errors.Count > 0)
                {
                    throw new InvalidDataException($"Unable to parse Swagger file: {string.Join(", ", diag.Errors.Select(err => err.Message))}");
                }

                return doc;
            }
        }
    }
}
