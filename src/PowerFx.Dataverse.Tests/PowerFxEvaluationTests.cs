//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
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
        static string ConnectionString = null;

        [ClassInitialize()]
        public static void ClassInit(TestContext context)
        {
            ConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
            
            if (string.IsNullOrEmpty(ConnectionString) && context.Properties.Contains("SqlConnectionString"))
                ConnectionString = context.Properties["SqlConnectionString"].ToString();

            if (string.IsNullOrEmpty(ConnectionString) && ConnectionString.Length > 75)
                Console.WriteLine($"Using connection string: {ConnectionString.Substring(0, 75)}...");
        }

        [TestMethod]
        public void RunSqlTestCases()
        {             
            // short-circuit if connection string is not set
            if (string.IsNullOrEmpty(ConnectionString))
            {
                Assert.Inconclusive("Skipping SQL tests - no connection string set");
                return;
            }

            // Build step copied all tests to output dir.
            using (var sql = new SqlRunner(ConnectionString))
            {
                var runner = new TestRunner(sql);
                runner.AddDir();

                foreach (var path in Directory.EnumerateFiles(GetSqlDefaultTestDir(), "*.txt"))
                {
                    runner.AddFile(path);
                }

                var result = runner.RunTests();

                // Ideally, this should only go down as the rest of the functions/capabilities are added
                // TODO: replace error count with locally based overlays of specific differences
                Assert.AreEqual(42, result.Fail);
            }
        }

        [TestMethod]
        public void RunCleanSqlTestCases()
        {            
            // short-circuit if connection string is not set
            if (string.IsNullOrEmpty(ConnectionString))
            {
                Assert.Inconclusive("Skipping SQL tests - no connection string set");
                return;
            }

            // Build step copied all tests to output dir.
            using (var sql = new SqlRunner(ConnectionString))
            {
                var runner = new TestRunner(sql);
                                
                // Run only those test files fully supported by SQL
                runner.AddFile(
                    "AndOrCases.txt",
                    "arithmetic.txt",
                    "Blank.txt",
                    "Date.txt",
                    "If.txt",
                    "inScalar.txt",
                    "mathfuncs.txt",
                    "string.txt",
                    "Text.txt",
                    "Value.txt"
                    );

                foreach (var path in Directory.EnumerateFiles(GetSqlDefaultTestDir(), "*.txt"))
                {
                    runner.AddFile(path);
                }
                
                var result = runner.RunTests();

                Assert.AreEqual(0, result.Fail);

                // Verify that we're actually running tests. 
                Assert.IsTrue(result.Total > 400);
            }
        }

        // Use this for local testing of a single testcase (uncomment "TestMethod")
        //[TestMethod]
        public void RunSingleTestCase()
        { 
            using (var sql = new SqlRunner(ConnectionString))
            {
                var runner = new TestRunner(sql);
                //runner.AddFile("Testingtxt");
                foreach (var path in Directory.EnumerateFiles(GetSqlDefaultTestDir(), "Sql.txt"))
                {
                    runner.AddFile(path);
                }

                var result = runner.RunTests();

                Assert.AreEqual(0, result.Fail);
            }
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

            // Round to 4 decimal places
            private double Round(double value)
            {
                return Math.Round(10000 * value, MidpointRounding.AwayFromZero) / 10000;
            }

            public override bool NumberCompare(double a, double b)
            {
                // SQL only has 4 digits of precision after the decimal. 
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
                if (setupHandlerName != null)
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
