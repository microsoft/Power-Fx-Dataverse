//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class ExpressionEvaluationTests
    {
        public readonly ITestOutputHelper Console;
        private readonly SqlRunner sqlRunner;

        public ExpressionEvaluationTests(ITestOutputHelper output)
        {
            Console = output;
            sqlRunner = NewSqlRunner();
        }

        private const string ConnectionStringVariable = "FxTestSQLDatabase";

        /// <summary>
        /// The connection string for the database to execute generated SQL
        /// </summary>
        /// <example> 
        /// "Data Source=tcp:SQL_SERVER;Initial Catalog=test;Integrated Security=True;Persist Security Info=True;";
        /// </example>
        private static readonly string ConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);

        // .txt tests will be filtered to match these seetings. 
        private static readonly Dictionary<string, bool> _testSettings = new Dictionary<string, bool>()
        {
            { "PowerFxV1CompatibilityRules", true },
            { "NumberIsFloat", DataverseEngine.NumberIsFloat },
            { "Default", false }              // anything not explicitly called out here is not supported
        };

        // These need to be consistent with _testSettings.
        private SqlRunner NewSqlRunner()
        {
            return new SqlRunner(ConnectionString, Console)
            {
                NumberIsFloat = DataverseEngine.NumberIsFloat,
                Features = PowerFx2SqlEngine.DefaultFeatures
            };
        }

        [SkippableTheory]
        [TxtFileData("ExpressionTestCases", "SqlExpressionTestCases", nameof(ExpressionEvaluationTests), "PowerFxV1CompatibilityRules")]
        public void RunSqlTestCases(ExpressionTestCase testCase)
        {
            (TestResult result, string message) = sqlRunner.RunTestCase(testCase);

            var prefix = $"Test {Path.GetFileName(testCase.SourceFile)}:{testCase.SourceLine}: ";
            switch (result)
            {
                case TestResult.Pass:
                    break;

                case TestResult.Fail:
                    Assert.True(false, prefix + message);
                    break;

                case TestResult.Skip:
                    Skip.If(true, prefix + message);
                    break;
            }
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
            private readonly SemaphoreSlim _concurrencySemaphore = new SemaphoreSlim(10);
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

                /* if (setupHandlerName.IndexOf("disable:NumberIsFloat", StringComparison.OrdinalIgnoreCase) > -1)
                {
                    return new RunResult() { UnsupportedReason = "NumberIsFloat=false is not supported." };
                } */

                if (setupHandlerName.IndexOf("DisableReservedKeywords", StringComparison.OrdinalIgnoreCase) > -1)
                {
                    return new RunResult() { UnsupportedReason = "DisableReservedKeywords is not supported." };
                }

                if (_connection == null)
                {
                    return new RunResult() { UnsupportedReason = "No connection string provided." };
                }

                if (iSetup.HandlerName != null ||
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
                var engine = new PowerFx2SqlEngine(metadata);
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

                _concurrencySemaphore.Wait();

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
                    Assert.True(false, $"Failed SQL for {expr}");
                    throw;
                }
                finally
                {
                    _concurrencySemaphore.Release();
                }
            }

            public override bool IsError(FormulaValue value)
            {
                // For CDS compatibility, SQL returns blank for runtime errors
                return value is BlankValue;
            }
        }
    }
}
