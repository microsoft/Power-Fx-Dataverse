// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.PowerFx.Dataverse;
using Xunit;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

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
                "Microsoft.PowerFx.Dataverse.EngineExtensions",
                "Microsoft.PowerFx.Dataverse.AttributeUtilityExtensions",
                "Microsoft.PowerFx.Dataverse.ConfigExtensions",

                "Microsoft.PowerFx.Dataverse.DataverseConnection",                
                "Microsoft.PowerFx.Dataverse.XrmMetadataProvider",
                "Microsoft.PowerFx.Dataverse.MetadataExtensions",
                "Microsoft.PowerFx.Dataverse.DataverseEntityCache",
                "Microsoft.PowerFx.Dataverse.IDataverseEntityCache",
                "Microsoft.PowerFx.Dataverse.IDataverseEntityCacheCleaner",

                // Policies 
                "Microsoft.PowerFx.Dataverse.Policy",
                "Microsoft.PowerFx.Dataverse.SingleOrgPolicy",
                "Microsoft.PowerFx.Dataverse.MultiOrgPolicy",

                // Optional services / Mocks.
                "Microsoft.PowerFx.Dataverse.DataverseService",
                "Microsoft.PowerFx.Dataverse.DataverseResponse`1",
                "Microsoft.PowerFx.Dataverse.DataverseResponse",
                "Microsoft.PowerFx.Dataverse.IDataverseServices",
                "Microsoft.PowerFx.Dataverse.IDataverseCreator",
                "Microsoft.PowerFx.Dataverse.IDataverseReader",
                "Microsoft.PowerFx.Dataverse.IDataverseUpdater",
                "Microsoft.PowerFx.Dataverse.IDataverseDeleter",
                "Microsoft.PowerFx.Dataverse.IDataverseRefresh",
                "Microsoft.PowerFx.Dataverse.IDataverseExecute",

                // Functions
                "Microsoft.PowerFx.Dataverse.AISummarizeFunction",
                "Microsoft.PowerFx.Dataverse.AIReplyFunction",
                "Microsoft.PowerFx.Dataverse.AISentimentFunction"

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
                
                // SQL compilation 
                "Microsoft.PowerFx.Dataverse.DataverseEngine",
                "Microsoft.PowerFx.Dataverse.PowerFx2SqlEngine",
                "Microsoft.PowerFx.Dataverse.SqlCompileOptions",
                "Microsoft.PowerFx.Dataverse.SqlCompileResult",
                "Microsoft.PowerFx.Dataverse.SqlCompileResult",
                "Microsoft.PowerFx.Dataverse.SqlCompileResult",

                // Other
                "Microsoft.AppMagic.Common.Telemetry.Log",
                "Microsoft.PowerFx.Dataverse.DataverseHelpers"
            };

            Verify(allowed, asm);
        }

        static void Verify(HashSet<string> allowed, Assembly asm)
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
