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
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    [TestClass]

    public class ExpressionEvaluationTests
    {
        private const string ConnectionStringVariable = "FxTestSQLDatabase";

        /// <summary>
        /// The connection string for the database to execute generated SQL
        /// </summary>
        /// "Data Source=tcp:SQLSERVER;Initial Catalog=test;Integrated Security=True;Persist Security Info=True;";        
        private static string ConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);

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
            return new SqlRunner(ConnectionString)
            {
                NumberIsFloat = DataverseEngine.NumberIsFloat,
                Features = PowerFx2SqlEngine.DefaultFeatures
            };
        }

        [TestMethod]
        public void RunSqlTestCases()
        {
            // "Data Source=tcp:SQLSERVER;Initial Catalog=test;Integrated Security=True;Persist Security Info=True;";
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
                    runner.AddFile(_testSettings, path);
                }
                
                Parallel.ForEach(runner.Tests.Where(t => t.SourceFile.Contains("BypassOverrideLogic")), t => t.OverrideFrom = "BypassOverrideLogic");

                TestRunFullResults result = runner.RunTests();
                Console.WriteLine(result.Output);

                // Any failures introduced by new tests or unsupported features should be overridden
                Assert.AreEqual(0, result.Fail);

                // Verify that we're actually running tests. 
                Assert.IsTrue(result.Total > 7000);
                Assert.IsTrue(result.Pass > 2000);
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
                : base()
            {                
                base.NumberIsFloat = DataverseEngine.NumberIsFloat;

                _connection = new SqlConnection(connectionString ?? throw new InvalidOperationException($"ConnectionString not set"));
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

                if (iSetup.HandlerName != null ||  iSetup.TimeZoneInfo != null)
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
                        _ = createCmd.ExecuteNonQuery();

                        var insertCmd = cx.CreateCommand();
                        insertCmd.Transaction = tx;
                        insertCmd.CommandText = "INSERT INTO placeholder (dummy) VALUES (7)";
                        _ = insertCmd.ExecuteNonQuery();

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
                            if (compileResult._unsupportedWarnings != null && compileResult._unsupportedWarnings.Any())
                            {
                                Console.WriteLine($"Warnings for {expr}: {string.Join(", ", compileResult._unsupportedWarnings)}");
                                result.UnsupportedReason = compileResult._unsupportedWarnings[0];
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
