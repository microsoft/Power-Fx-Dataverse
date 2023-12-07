//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.PowerFx.Core.Tests;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests
{

    public class FunctionNameTests
    {
        private static EngineDocumentation GetActual()
        {
            var funcs = Dataverse.Functions.Library.FunctionList;
            var set = new HashSet<string>(funcs.Select(f => f.Name));

            // Some functions exist in the list just so they can bind and return failure. 
            // These are not actually supported. 
            set.Remove("IsToday");
            set.Remove("Today");

            var actualSchema = new EngineDocumentation
            {
                FunctionNames = set.ToArray()
            }.Normalize();

            return actualSchema;
        }

        private static EngineDocumentation Read(string schemaName)
        {
            var path = Path.GetFullPath(schemaName);
            var json = File.ReadAllText(path);
            var schema = JsonSerializer.Deserialize<EngineDocumentation>(json).Normalize();

            return schema;
        }

        // Verify that the .json file describing which functions SQL compiler implements.
        // When you add/remove functions from Library.cs, need to update this json. 
        // Doc team then pulls from this json to drive public docs. 
        [Fact]
        public void SqlCompilerSchemaCheck()
        {
            // Get checked in schema 
            string schemaName = @"Docs\DataverseFormulaColumns.json";

            var expectedSchema = Read(schemaName);

            var actualSchema = GetActual();

            // Write actual for easy debugger 
            var pathTemp = Path.Combine(Path.GetTempPath(), "actual-" + Path.GetFileName(schemaName));
            var jsonActual = JsonSerializer.Serialize(actualSchema, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(pathTemp, jsonActual);

            // Compare
            var expected = expectedSchema.GetCompareString();
            var actual = actualSchema.GetCompareString();

            Assert.Equal(expected, actual);
        }
    }
}
