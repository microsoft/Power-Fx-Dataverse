//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------


using System.Threading.Tasks;
using Microsoft.PowerFx.Types;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class ServiceUnavailableTests
    {
        [Theory]
        [InlineData("AISummarize(\"very long string\")")]
        [InlineData("AITranslate(\"Testando tradutor\")")]
        [InlineData("AISentiment(\"Some text\")")]
        [InlineData("AISummarizeRecord(First(Locals))")]
        [InlineData("AIReply(\"What is a question?\")")]
        [InlineData("AIExtract(\"Some text\",\"text\")")]
        [InlineData("AIClassify(\"very long string\", [\"string\", \"int\", \"double\"])")]
        public async Task AIFunctionWhenDVConnIsMissingAsync(string expression)
        {
            var config = new PowerFxConfig();
            config.EnableAIFunctions();

            var (dvc, ds, el) = PluginExecutionTests.CreateMemoryForRelationshipModelsInternal();
            dvc.AddTable("Locals", "local");

            var engine = new RecalcEngine(config);
            var rc = new RuntimeConfig(dvc.SymbolValues);

            var client = new DataverseNotPresent();
            rc.AddDataverseExecute(client);

            var result = await engine.EvalAsync(expression, default, runtimeConfig: rc);
            var errors = (ErrorValue)result;

            Assert.Equal(1, errors.Errors.Count);

            var error = errors.Errors[0];
            Assert.Equal(ErrorKind.ServiceUnavailable, error.Kind);
        }
    }
}
