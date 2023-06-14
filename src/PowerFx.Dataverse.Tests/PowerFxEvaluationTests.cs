//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    [TestClass]

    public class ExpressionEvaluationTests
    {
        const string ConnectionStringVariable = "FxTestSQLDatabase";

        /// <summary>
        /// The connection string for the database to execute generated SQL
        /// </summary>
        static string ConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);

        // .txt tests will be filtered to match these seetings. 
        static readonly Dictionary<string, bool> _testSettings = new Dictionary<string, bool>()
        {
            { "PowerFxV1CompatibilityRules", true },
            { "NumberIsFloat", DataverseEngine.NumberIsFloat },
            { "Default", false }              // anything not explicitly called out here is not supported
        };

        // These need to be consistent with _testSettings.
        private SqlRunner NewSqlRunner()
        {
            return new SqlRunner(ConnectionString)
            {
                NumberIsFloat = DataverseEngine.NumberIsFloat,
                Features = PowerFx2SqlEngine.DefaultFeatures
            };
        }

        [TestMethod]
        public void RunSqlTestCases()
        {
            ConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
            // short-circuit if connection string is not set
            if (ConnectionString == null)
            {
                Assert.Inconclusive("Skipping SQL tests - no connection string set");
                return;
            }

            // Build step copied all tests to output dir.
            using (var sql = NewSqlRunner())
            {
                var runner = new TestRunner(sql);
                runner.AddDir(_testSettings);

                foreach (var path in Directory.EnumerateFiles(GetSqlDefaultTestDir(), "*.txt"))
                {
                    // Skip these tests for now as they generate errors.
                    // https://github.com/microsoft/Power-Fx-Dataverse/issues/219

                    // FAIL: SqlRunner, Mod_Float.txt:34
                    // FAIL: Mod(1E+400, 3)
                    // Failed, but wrong error message: Errors: Error 4-10: Numeric value is too large.
                    // Error 4-10: Invalid argument type (Error). Expecting a Number value instead.
                    // Error 0-14: The function 'Mod' has some invalid arguments.

                    // FAIL: SqlRunner, Text_Format_PowerFxV1Compat.txt:3
                    // FAIL: Text(123.466, "[$-en-US]$#0.0M")
                    // Failed, but wrong error message: Errors: Warning 14-31: Incorrect format specifier for 'Text'.
                    // Error 0-32: The function 'Text' has some invalid arguments.

                    // FAIL: SqlRunner, Text_Format_PowerFxV1Compat.txt:6
                    // FAIL: Text(1, "M#")
                    // Failed, but wrong error message: Errors: Warning 8-12: Incorrect format specifier for 'Text'.
                    // Error 0-13: The function 'Text' has some invalid arguments.

                    // FAIL: SqlRunner, Text_Format_PowerFxV1Compat.txt:10
                    // FAIL: Text(1234.5678,"[$-]")
                    // Failed, but wrong error message: Errors: Warning 15-21: Incorrect format specifier for 'Text'.
                    // Error 0-22: The function 'Text' has some invalid arguments.

                    // FAIL: SqlRunner, Text_Format_PowerFxV1Compat.txt:14
                    // FAIL: Text(1234.5678,"[$-a")
                    // Failed, but wrong error message: Errors: Warning 15-21: Incorrect format specifier for 'Text'.
                    // Error 0-22: The function 'Text' has some invalid arguments.

                    // FAIL: SqlRunner, Text_Format_PowerFxV1Compat.txt:18
                    // FAIL: Text(1234.5678, "[$-en-US#,##0.00", "en-US")
                    // Failed, but wrong error message: Errors: Warning 16-34: Incorrect format specifier for 'Text'.
                    // Error 0-44: The function 'Text' has some invalid arguments.

                    // FAIL: SqlRunner, Text_Format_PowerFxV1Compat.txt:21
                    // FAIL: Text(1234.5678, "[$-]fr-FR # ##0,00", "vi-VI")
                    // Failed, but wrong error message: Errors: Warning 16-36: Incorrect format specifier for 'Text'.
                    // Error 0-46: The function 'Text' has some invalid arguments.

                    // FAIL: SqlRunner, Text_Format_PowerFxV1Compat.txt:24
                    // FAIL: Text(1234.5678, "[$-fr-FR][$-en-US] # ##0,00", "vi-VI")
                    // Failed, but wrong error message: Errors: Warning 16-45: Incorrect format specifier for 'Text'.
                    // Error 0-55: The function 'Text' has some invalid arguments.

                    if (path.Contains("Mod_Float.txt") || path.Contains("Text_Format_PowerFxV1Compat.txt"))
                    {
                        continue;
                    }

                    runner.AddFile(_testSettings, path);
                }

                var result = runner.RunTests();

                // Any failures introduced by new tests or unsupported features should be overridden
                Assert.AreEqual(0, result.Fail, result.Output);

                // Verify that we're actually running tests. 
                Assert.IsTrue(result.Total > 4000);
                Assert.IsTrue(result.Pass > 1000);
            }
        }

        // Use this for local testing of a single testcase (uncomment "TestMethod")
        //[TestMethod]
        public void RunSingleTestCase()
        {
            using (var sql = NewSqlRunner())
            {
                var runner = new TestRunner(sql);
                runner.AddFile(_testSettings, @"c:\temp\t.txt");
                /*
                foreach (var path in Directory.EnumerateFiles(GetSqlDefaultTestDir(), "Sql.txt"))
                {
                    runner.AddFile(DataverseEngine.NumberIsFloat, path);
                }*/

                var result = runner.RunTests();

                Assert.AreEqual(0, result.Fail, result.Output);
            }
        }

        [TestMethod]
        public void SqlCompileExceptionIsError()
        {
            Assert.IsTrue(SqlCompileException.IsError("FormulaColumns_ColumnTypeNotSupported"));
            Assert.IsFalse(SqlCompileException.IsError("OtherError"));
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

            public SqlRunner(string connectionString)
            {
                if (connectionString == null)
                {
                    throw new InvalidOperationException($"ConnectionString not set");
                }
                _connection = new SqlConnection(connectionString);
                _connection.Open();
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

                try
                {
                    var cx = _connection;
                    using (var tx = cx.BeginTransaction())
                    {
                        var createCmd = cx.CreateCommand();
                        createCmd.Transaction = tx;
                        createCmd.CommandText = compileResult.SqlFunction;
                        var rows = createCmd.ExecuteNonQuery();

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
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed SQL for {expr}");
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
}
