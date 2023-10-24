// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.PowerFx.Dataverse;
using Xunit;

namespace Microsoft.PowerFx.Tests
{

    public class PublicSurfaceTests
    {
        // The goal for public namespaces is to make the SDK easy for the consumer. 
        // Namespace principles for public classes:            // 
        // - prefer fewer namespaces. See C# for example: https://docs.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis
        // - For easy discovery, but Engine in "Microsoft.PowerFx".
        // - For sub areas with many related classes, cluster into a single subnamespace.
        // - Avoid nesting more than 1 level deep


        [Fact]
        public void TestPowerFxDataverseEval()
        {
            var asm = typeof(DataverseConnection).Assembly;

            var allowed = new HashSet<string>()
            {
                "Microsoft.PowerFx.Dataverse.AttributeUtilityExtensions",
                "Microsoft.PowerFx.Dataverse.ConfigExtensions",
                "Microsoft.PowerFx.Dataverse.DataverseConnection",                
                "Microsoft.PowerFx.Dataverse.DataverseEntityCache",
                "Microsoft.PowerFx.Dataverse.EngineExtensions",
                "Microsoft.PowerFx.Dataverse.IDataverseEntityCache",
                "Microsoft.PowerFx.Dataverse.IDataverseEntityCacheCleaner",
                "Microsoft.PowerFx.Dataverse.MetadataExtensions",
                "Microsoft.PowerFx.Dataverse.XrmMetadataProvider",

                // Policies 
                "Microsoft.PowerFx.Dataverse.MultiOrgPolicy",
                "Microsoft.PowerFx.Dataverse.Policy",
                "Microsoft.PowerFx.Dataverse.SingleOrgPolicy",

                // Optional services / Mocks.
                "Microsoft.PowerFx.Dataverse.DataverseResponse",
                "Microsoft.PowerFx.Dataverse.DataverseResponse`1",
                "Microsoft.PowerFx.Dataverse.DataverseService",
                "Microsoft.PowerFx.Dataverse.IDataverseCreator",
                "Microsoft.PowerFx.Dataverse.IDataverseDeleter",
                "Microsoft.PowerFx.Dataverse.IDataverseExecute",
                "Microsoft.PowerFx.Dataverse.IDataverseReader",
                "Microsoft.PowerFx.Dataverse.IDataverseRefresh",
                "Microsoft.PowerFx.Dataverse.IDataverseServices",
                "Microsoft.PowerFx.Dataverse.IDataverseUpdater",

                // Functions
                "Microsoft.PowerFx.Dataverse.DVEnumeratePlugInsFunction",
                "Microsoft.PowerFx.Dataverse.DVAddPlugInFunction",
                "Microsoft.PowerFx.Dataverse.AITranslateFunction",
                "Microsoft.PowerFx.Dataverse.AISummarizeRecordFunction",
                "Microsoft.PowerFx.Dataverse.AISummarizeFunction",
                "Microsoft.PowerFx.Dataverse.AISentimentFunction",
                "Microsoft.PowerFx.Dataverse.AIReplyFunction",
                "Microsoft.PowerFx.Dataverse.AIExtractFunction",
                "Microsoft.PowerFx.Dataverse.AIClassifyFunction",

                // Plugins
                "Microsoft.PowerFx.Dataverse.CustomApiEntity",
                "Microsoft.PowerFx.Dataverse.CustomApiParamType",
                "Microsoft.PowerFx.Dataverse.CustomApiRequestParam",
                "Microsoft.PowerFx.Dataverse.CustomApiResponse",
                "Microsoft.PowerFx.Dataverse.CustomApiSignature",
                "Microsoft.PowerFx.Dataverse.DataverseEntityAttribute",
                "Microsoft.PowerFx.Dataverse.IDataversePlugInContext",
                "Microsoft.PowerFx.Dataverse.IParameterType",
                "Microsoft.PowerFx.Dataverse.PlugInInvoker",
                "Microsoft.PowerFx.Dataverse.PlugInRuntimeContext",
            };

            Verify(allowed, asm);
        }

        [Fact]
        public void TestPowerFxDataverse()
        {
            var asm = typeof(IXrmMetadataProvider).Assembly;

            var allowed = new HashSet<string>()
            {
                "Microsoft.PowerFx.Dataverse.AttributeUtility",
                "Microsoft.PowerFx.Dataverse.DependencyInfo",

                // Common Entity metadata providers
                "Microsoft.PowerFx.Dataverse.IXrmMetadataProvider",
                "Microsoft.PowerFx.Dataverse.CdsEntityMetadataProvider",

                // used for Eval, but here because they need Fx Core Internals. 
                "Microsoft.PowerFx.Dataverse.DVSymbolTable",
                "Microsoft.PowerFx.Dataverse.DelegationEngineExtensions",
                
                // Other
                "Microsoft.AppMagic.Common.Telemetry.Log",
                "Microsoft.PowerFx.Dataverse.DataverseHelpers",
                "Microsoft.PowerFx.Dataverse.XrmUtility"
            };

            Verify(allowed, asm);
        }

        private static void Verify(HashSet<string> allowed, Assembly asm)
        {
            var sb = new StringBuilder();
            var count = 0;
            foreach (var type in asm.GetTypes().Where(t => t.IsPublic))
            {
                var name = type.FullName;
                if (!allowed.Contains(name))
                {
                    sb.AppendLine(name);
                    count++;
                }

                allowed.Remove(name);
            }

            Assert.True(count == 0, $"Unexpected public types: {sb}");

            // Types we expect to be in the assembly are all there. 
            Assert.Equal("", string.Join(",", allowed));
        }
    }
}
