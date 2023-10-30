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
using Microsoft.OpenApi.Writers;
using Microsoft.PowerFx.Connectors;
using Microsoft.PowerFx.Types;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class PlugInExecutionTests
    {
        private readonly ITestOutputHelper _output;

        public PlugInExecutionTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // https://docs.microsoft.com/en-us/power-apps/developer/data-platform/xrm-tooling/use-connection-strings-xrm-tooling-connect                        
        // $"Url=https://aurorabapenv9984a.crm10.dynamics.com/; Username={username}; Password={pwd}; authtype=OAuth";
        private static readonly string cx = null;

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
            OpenApiDocument swagger = DataverseService.GetSwagger(plugIn);

            using var sw = new StringWriter();
            swagger.SerializeAsV2(new OpenApiJsonWriter(sw));
            sw.Flush();
            string swaggerString = sw.ToString();

            _output.WriteLine(swaggerString);
            _output.WriteLine("");

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

#if RESPECT_REQUIRED_OPTIONAL
            FormulaValue result = await engine.EvalAsync(@"PlugIn.lucgen1(""str"", 19).z", CancellationToken.None, new ParserOptions() { AllowsSideEffects = true }, runtimeConfig: runtimeConfig).ConfigureAwait(false);
#else
            FormulaValue result = await engine.EvalAsync(@"PlugIn.lucgen1({x: ""str"", y: 19}).z", CancellationToken.None, new ParserOptions() { AllowsSideEffects = true }, runtimeConfig: runtimeConfig).ConfigureAwait(false);
#endif
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
            using var client = new PowerPlatformConnectorClient("https://tip1002-002.azure-apihub.net", "b29c41cf-173b-e469-830b-4f00163d296b", "b17aa2032f6742dd971437fd4295040b", () => "eyJ0eXAiO...", httpClient) { SessionId = Guid.NewGuid().ToString() };

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

#if RESPECT_REQUIRED_OPTIONAL
            CheckResult checkResult = engine.Check(@"PlugIn.lucgen1(""str"", 19).z &  ""-"" & PlugIn.lucgen2(""xyz"", 71).t & ""-"" & Text(MsnWeather.CurrentWeather(""Paris"", ""I"").responses.weather.current.temp)", new ParserOptions() { AllowsSideEffects = true });
#else
            CheckResult checkResult = engine.Check(@"PlugIn.lucgen1({x: ""str"", y: 19}).z &  ""-"" & PlugIn.lucgen2({x: ""xyz"", y: 71}).t & ""-"" & Text(MsnWeather.CurrentWeather(""Paris"", ""I"").responses.weather.current.temp)", new ParserOptions() { AllowsSideEffects = true });
#endif
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
