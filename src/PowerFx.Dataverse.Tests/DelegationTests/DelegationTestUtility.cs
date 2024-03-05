using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using System.Threading;
using Xunit;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.PowerFx.Dataverse;
using System.Threading.Tasks;
using System.IO;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class DelegationTestUtility
    {
        /// <summary>
        /// Set this to true, if you need to regenerate the snapshot files.
        /// </summary>
        private const bool _regenerate = true;

        internal static IList<string> TransformForWithFunction(string expr, int warningCount)
        {
            var inputs = new List<string> { expr };

            if (warningCount > 0 || expr.StartsWith("With(") || expr.StartsWith("Collect("))
            {
                return inputs;
            }

            // transforms input expression without with, to wrap inside with.
            // e.g. LookUp(t1, Price = 255).Price -> With({r:local}, LookUp(r, Price = 255).Price)
            var withExpr = new StringBuilder("With({r:local},");
            withExpr.Append(expr.Replace("(t1,", "(r,"));
            withExpr.Append(")");

            inputs.Add(withExpr.ToString());

            return inputs;
        }

        internal static async Task CompareSnapShotAsync(string fileName, string inputString, int lineNumber, bool isWithTransform)
        {
            var baseDirectory = Path.Join(Directory.GetCurrentDirectory(), "DelegationTests", "IRSnapShots", $"{(isWithTransform ? "WithTransformed_" : string.Empty)}{fileName}");

            string path = _regenerate ?
            baseDirectory
            .Replace(Path.Join("bin", "Debug", "netcoreapp3.1", "win-x64"), string.Empty)
            .Replace(Path.Join("bin", "Release", "netcoreapp3.1", "win-x64"), string.Empty) :
            baseDirectory;

            if (!File.Exists(path))
            {
                // If file doesn't exist and operation is regenerate, create the file with necessary padding
                if (_regenerate)
                {
                    using (var sw = new StreamWriter(path))
                    {
                        for (int i = 1; i < lineNumber; i++)
                        {
                            await sw.WriteLineAsync("");
                        }

                        await sw.WriteLineAsync(inputString);
                    }
                }
            }

            string[] allLines = await File.ReadAllLinesAsync(path);

            // Adjust for zero-based index
            int index = lineNumber - 1;

            // Ensure the array is large enough to include the new line number
            if (index >= allLines.Length)
            {
                Array.Resize(ref allLines, index + 1);
            }

            if (_regenerate)
            {
                // Update or add the specified line with the input string
                allLines[index] = inputString;
                await File.WriteAllLinesAsync(path, allLines);
            }
            else
            {
                // Compare the specified line with the input string, considering new lines as empty
                var targetLine = index < allLines.Length ? allLines[index] : "";
                Assert.Equal(targetLine, inputString);
            }
        }

        [Fact]
        public void RegenerateShouldBeFalse()
        {
            Assert.False(_regenerate);
        }
    }
}
