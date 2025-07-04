﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public static class DelegationTestUtility
    {
        private const bool RegenerateSnapshot = false;

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

        internal static async Task CompareSnapShotAsync(int id, string fileName, string inputString, int lineNumber, bool isWithTransform)
        {
            var fileName2 = $"{(isWithTransform ? "WithTransformed_" : string.Empty)}{fileName}";
            var baseDirectory = Path.Join(Directory.GetCurrentDirectory(), "DelegationTests", "IRSnapShots", fileName2);

            string path =

                 // Set this if you need to regenerate the snapshot files.
                 RegenerateSnapshot 
                 ? baseDirectory.Replace(Path.Join("bin", "Debug", "net7.0"), string.Empty).Replace(Path.Join("bin", "Release", "net7.0"), string.Empty)
                 : baseDirectory;

            if (RegenerateSnapshot) 
            {
                if (!File.Exists(path))
                {
                    using (var sw = new StreamWriter(path))
                    {
                        for (int i = 1; i < lineNumber; i++)
                        {
                            await sw.WriteLineAsync(string.Empty);
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

            if (RegenerateSnapshot)
            {
                // Update or add the specified line with the input string
                allLines[index] = inputString;
                await File.WriteAllLinesAsync(path, allLines);
            }
            else
            {
                // Compare the specified line with the input string, considering new lines as empty
                var targetLine = index < allLines.Length ? allLines[index] : string.Empty;

                Assert.True(targetLine == inputString, $"Id {id}, File {fileName2} Line {index + 1}\r\n{ShowDifference(targetLine, inputString)}");
            }
        }
        
        private static string ShowDifference(string target, string input)
        {
            target ??= string.Empty;
            input ??= string.Empty;
            string common = string.Concat(target.TakeWhile((c, i) => i < input.Length && c == input[i]));
            string spc = new string(' ', common.Length + 10);
            return $"{spc}\u2193 Pos={common.Length}\r\nExpected: {target}\r\nActual: {input}";
        }

        [Fact]
        public static void CheckRegenrateSnapshot()
        {
            Assert.False(RegenerateSnapshot, "RegenerateSnapshot is set to true. Please set it to false before running the tests.");
        }
    }
}
