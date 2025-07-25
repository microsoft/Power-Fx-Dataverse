﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    [CollectionDefinition("SQL Tests", DisableParallelization = true)]
    public class ExpressionEvaluationTests : IClassFixture<SkippedTestsReporting>
    {
        public readonly ITestOutputHelper Console;

        public readonly SkippedTestsReporting SkippedTestsReporting;

        public ExpressionEvaluationTests(SkippedTestsReporting fixture, ITestOutputHelper output)
        {
            Console = output;
            SkippedTestsReporting = fixture;
        }

        private const string ConnectionStringVariable = "FxTestSQLDatabase";

        /// <summary>
        /// The connection string for the database to execute generated SQL.
        /// </summary>
        /// <example>
        /// "Data Source=tcp:SQL_SERVER;Initial Catalog=test;Integrated Security=True;Persist Security Info=True;";.
        /// </example>
        private static readonly string ConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);

        // .txt tests will be filtered to match these seetings.
        private static readonly Dictionary<string, bool> _testSettings = new Dictionary<string, bool>()
        {
            { "PowerFxV1CompatibilityRules", true },
            { "NumberIsFloat", DataverseEngine.NumberIsFloat },
            
            // anything not explicitly called out here is not supported
            { "Default", false }
        };

        [SkippableTheory]
        [TxtFileData("ExpressionTestCases", "SqlExpressionTestCases", nameof(ExpressionEvaluationTests), "PowerFxV1CompatibilityRules, disable:NumberIsFloat")]
        public void RunSqlTestCases(ExpressionTestCase testCase)
        {
            using SqlRunner sqlRunner = new SqlRunner(ConnectionString, Console) { NumberIsFloat = DataverseEngine.NumberIsFloat, Features = PowerFx2SqlEngine.DefaultFeatures };
            (TestResult result, string message) = sqlRunner.RunTestCase(testCase);

            var prefix = $"Test {Path.GetFileName(testCase.SourceFile)}:{testCase.SourceLine}: ";
            switch (result)
            {
                case TestResult.Pass:
                    break;

                case TestResult.Fail:
                    Assert.Fail(prefix + message);
                    break;

                case TestResult.Skip:
                    if (!SkippedTestsReporting.Report.TryAdd($"{prefix} {testCase.Input}", message))
                    {
                        Assert.Fail($"CONFLICT when adding test to report: {prefix + message}");
                    }

                    Skip.If(true, prefix + message);
                    break;
            }
        }

        [Fact(Skip = "Enable to run a single test")]
        public void RunOneTest()
        {
            // You can point to the local path of interest.
            var path = @"C:\Users\jmstall\.nuget\packages\microsoft.powerfx.core.tests\0.2.7-preview.20230727-1003\content\ExpressionTestCases\Abs.txt";
            var line = 17;

            using SqlRunner sqlRunner = new SqlRunner(ConnectionString, Console) { NumberIsFloat = DataverseEngine.NumberIsFloat, Features = PowerFx2SqlEngine.DefaultFeatures };

            var testRunner = new TestRunner(sqlRunner);

            testRunner.AddFile(_testSettings, path);

            // We can filter to just cases we want
            if (line > 0)
            {
                testRunner.Tests.RemoveAll(x => x.SourceLine != line);
            }

            int totalTests = testRunner.Tests.Count;

            var result = testRunner.RunTests();
        }

        [Fact]
        public void ScanForTxtParseErrors()
        {
            MethodInfo method = GetType().GetMethod(nameof(RunSqlTestCases));
            TxtFileDataAttribute attr = (TxtFileDataAttribute)method.GetCustomAttributes(typeof(TxtFileDataAttribute), false)[0];

            // Verify this runs without throwing an exception.
            IEnumerable<object[]> list = attr.GetData(method);
            Console.WriteLine($"{list.Count()} test cases found.");

            // And doesn't report back any test failures.
            foreach (object[] batch in list)
            {
                var item = (ExpressionTestCase)batch[0];
                Assert.Null(item.FailMessage);
            }
        }

        [Fact]
        public void SqlCompileExceptionIsError()
        {
            Assert.True(SqlCompileException.IsError("FormulaColumns_ColumnTypeNotSupported"));
            Assert.False(SqlCompileException.IsError("OtherError"));
        }

        public static string GetSqlDefaultTestDir()
        {
            var curDir = Path.GetDirectoryName(typeof(ExpressionEvaluationTests).Assembly.Location);
            var testDir = Path.Combine(curDir, "SqlExpressionTestCases");
            return testDir;
        }

        private class SqlRunner : BaseRunner, IDisposable
        {
            private SqlConnection _connection;

            public readonly ITestOutputHelper Console;

            public SqlRunner(string connectionString, ITestOutputHelper console)
            {
                Console = console;

                if (!string.IsNullOrEmpty(connectionString))
                {
                    _connection = new SqlConnection(connectionString);
                    _connection.Open();
                }
                else
                {
                    _connection = null;
                }
            }

            public void Dispose()
            {
                if (_connection != null)
                {
                    _connection.Dispose();
                    _connection = null;
                }
            }

            // Round to default precision number of decimal places
            private double Round(double value)
            {
                return Math.Round(Math.Pow(10, PowerFx2SqlEngine.DefaultPrecision) * value, MidpointRounding.AwayFromZero) / Math.Pow(10, PowerFx2SqlEngine.DefaultPrecision);
            }

            public override bool NumberCompare(double a, double b)
            {
                if (base.NumberCompare(a, b))
                {
                    return true;
                }

                if (Round(a) == Round(b))
                {
                    return true;
                }

                return false;
            }

            protected override async Task<RunResult> RunAsyncInternal(string expr, string setupHandlerName = null)
            {
                var iSetup = InternalSetup.Parse(setupHandlerName, Features, NumberIsFloat);

                if (iSetup.Flags.HasFlag(Core.Parser.TexlParser.Flags.EnableExpressionChaining))
                {
                    return new RunResult() { UnsupportedReason = "Expression chaining is not supported." };
                }

                if (setupHandlerName.IndexOf("DisableReservedKeywords", StringComparison.OrdinalIgnoreCase) > -1)
                {
                    return new RunResult() { UnsupportedReason = "DisableReservedKeywords is not supported." };
                }

                if (_connection == null)
                {
                    return new RunResult() { UnsupportedReason = "No connection string provided." };
                }

                if (iSetup.HandlerNames != null ||
                    iSetup.TimeZoneInfo != null)
                {
                    throw new SetupHandlerNotFoundException();
                }

                var options = new SqlCompileOptions
                {
                    CreateMode = SqlCompileOptions.Mode.Create,
                    UdfName = null // will auto generate with guid.
                };

                var metadata = PowerFx2SqlEngine.Empty();

                // run unit tests with time zone conversions on
                // instantiate engine with floating point feature as on
                var engine = new PowerFx2SqlEngine(metadata, dataverseFeatures: new DataverseFeatures() { IsFloatingPointEnabled = true });
                var compileResult = engine.Compile(expr, options);

                if (!compileResult.IsSuccess)
                {
                    // Evaluation ran, but failed due to unsupported features.
                    var result = new RunResult(compileResult);

                    // If error is a known SQL restriction, than flag as unsupported.
                    foreach (var error in compileResult.Errors)
                    {
                        if (SqlCompileException.IsError(error.MessageKey))
                        {
                            result.UnsupportedReason = error.Message;
                            return result;
                        }
                    }

                    // If error is an exisitng error type (such as unsupported function), but flagged for SQL.
                    if (compileResult._unsupportedWarnings != null)
                    {
                        if (compileResult._unsupportedWarnings.Count > 0)
                        {
                            result.UnsupportedReason = compileResult._unsupportedWarnings[0];
                        }
                    }

                    return result;
                }

                try
                {
                    SqlConnection cx = _connection;
                    using SqlTransaction tx = cx.BeginTransaction();
                    using SqlCommand createCmd = cx.CreateCommand();

                    createCmd.Transaction = tx;
                    createCmd.CommandText = compileResult.SqlFunction;
                    int rows = createCmd.ExecuteNonQuery();

                    createCmd.CommandText = $@"CREATE TABLE placeholder (
    [placeholderid] UNIQUEIDENTIFIER DEFAULT NEWID() PRIMARY KEY,
    [dummy] INT NULL,
    [calc]  AS ([dbo].{compileResult.SqlCreateRow}))";
                    createCmd.ExecuteNonQuery();

                    var insertCmd = cx.CreateCommand();
                    insertCmd.Transaction = tx;
                    insertCmd.CommandText = "INSERT INTO placeholder (dummy) VALUES (7)";
                    insertCmd.ExecuteNonQuery();

                    var selectCmd = cx.CreateCommand();
                    selectCmd.Transaction = tx;
                    selectCmd.CommandText = $"SELECT dummy, calc from placeholder";
                    using (var reader = selectCmd.ExecuteReader())
                    {
                        reader.Read();
                        var dummyValue = reader.GetInt32(0);
                        if (dummyValue != 7)
                        {
                            throw new Exception("Dummy integer did not round-trip");
                        }

                        var calcValue = reader.GetValue(1);

                        if (calcValue is DBNull)
                        {
                            calcValue = null;
                        }

                        var fv = PrimitiveValueConversions.Marshal(calcValue, compileResult.ReturnType);
                        var result = new RunResult(fv);

                        // Evaluation ran, but failed due to unsupported features.
                        if (compileResult._unsupportedWarnings != null)
                        {
                            if (compileResult._unsupportedWarnings.Count > 0)
                            {
                                result.UnsupportedReason = compileResult._unsupportedWarnings[0];
                            }
                        }

                        return result;
                    }
                }
                catch (Exception e)
                {
                    this.Console.WriteLine($"Failed SQL for {expr}");
                    Console.WriteLine(compileResult.SqlFunction);
                    Console.WriteLine(e.Message);
                    Assert.Fail($"Failed SQL for {expr}");
                    throw;
                }
            }

            public override bool IsError(FormulaValue value)
            {
                // For CDS compatibility, SQL returns blank for runtime errors
                return value is BlankValue;
            }
        }
    }

    public class SkippedTestsReporting : IDisposable
    {
        public static ConcurrentDictionary<string, string> Report;

        private bool _disposedValue;

        public SkippedTestsReporting()
        {
            Report = new ConcurrentDictionary<string, string>();
        }

        public void GenerateReport()
        {
            // Environment.CurrentDirectory
            // On build servers: ENV: C:\__w\1\s\pfx\src\tests\Microsoft.PowerFx.Connectors.Tests\bin\Release\netcoreapp3.1
            // Locally         : ENV: C:\Data\Power-Fx\src\tests\Microsoft.PowerFx.Connectors.Tests\bin\Debug\netcoreapp3.1
            string outFolder = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\.."));

            var list = Report.OrderBy(kvp => kvp.Key).ToList();
            File.WriteAllText(Path.Combine(outFolder, "SkippedTests.json"), JsonSerializer.Serialize(list, new JsonSerializerOptions() { WriteIndented = true }));
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    GenerateReport();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
