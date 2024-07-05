using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class DelegationTestUtility
    {
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
            var fileName2 = $"{(isWithTransform ? "WithTransformed_" : string.Empty)}{fileName}";
            var baseDirectory = Path.Join(Directory.GetCurrentDirectory(), "DelegationTests", "IRSnapShots", fileName2);

            string path =

                 // Set this if you need to regenerate the snapshot files.
#if REGENERATE
                 baseDirectory.Replace(Path.Join("bin", "Debug", "net7.0", "win-x64"), string.Empty)
                              .Replace(Path.Join("bin", "Release", "net7.0", "win-x64"), string.Empty) 
#else
                 baseDirectory;
#endif

#if REGENERATE
            if (!File.Exists(path))
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
#endif

            string[] allLines = await File.ReadAllLinesAsync(path);

            // Adjust for zero-based index
            int index = lineNumber - 1;

            // Ensure the array is large enough to include the new line number
            if (index >= allLines.Length)
            {
                Array.Resize(ref allLines, index + 1);
            }

#if REGENERATE
            
            // Update or add the specified line with the input string
            allLines[index] = inputString;
            await File.WriteAllLinesAsync(path, allLines);
#else
            // Compare the specified line with the input string, considering new lines as empty
            var targetLine = index < allLines.Length ? allLines[index] : "";
            Assert.True(targetLine == inputString, $"File {fileName2} Line {index + 1}\r\n{ShowDifference(targetLine, inputString)}");
#endif            
        }

        private static string ShowDifference(string target, string input)
        {
            string common = string.Concat(target.TakeWhile((c, i) => c == input[i]));
            string spc = new string(' ', common.Length + 10);
            return $"{spc}\u2193 Pos={common.Length}\r\nExpected: {target}\r\nActual: {input}";
        }
    }
}
