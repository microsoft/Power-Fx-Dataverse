//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Dataverse;
using Microsoft.PowerFx.Types;

namespace Repl
{
    public class SqlRunner : BaseRunner, IDisposable
    {
        private SqlConnection _connection;

        public SqlRunner(string connectionString)
        {
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
            return Math.Round(Math.Pow(10, 10) * value, MidpointRounding.AwayFromZero) / Math.Pow(10, 10);
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

        public FormulaValue RunExpr(string expr)
        {
            var t = Task.Factory.StartNew(() =>
                {
                    var t = RunAsyncInternal(expr, "");
                    t.ConfigureAwait(false);

                    return t.Result;
                },
                new CancellationToken(),
                TaskCreationOptions.None,
                TaskScheduler.Default);

            while (true)
            {
                Task.WaitAny(t, Task.Delay(Timeout));

                if (t.IsCompletedSuccessfully)
                {
                    if (t.Result.Errors != null && t.Result.Errors.Any())
                    {
                        throw new Exception("Errors: " + string.Join("\n", t.Result.Errors.Select(x => x.ToString())));
                    }
                    else
                    {
                        return t.Result.Value;
                    }
                }
            }
        }

        public string SQLExpr(string expr)
        {
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
                throw new Exception("Errors: " + string.Join("\n", compileResult.Errors.Select(x => x.ToString())));
            }
            else
            {
                return compileResult.SqlFunction;
            }
        }

        protected override async Task<RunResult> RunAsyncInternal(string expr, string setupHandlerName = null)
        {
            var iSetup = InternalSetup.Parse(setupHandlerName, Features, NumberIsFloat);

            if (iSetup.Flags.HasFlag(TexlParserFlags.EnableExpressionChaining))
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
                    if (IsError2(error.MessageKey))
                    {
                        result.UnsupportedReason = error.Message;
                        return result;
                    }
                }

                // If error is an exisitng error type (such as unsupported function), but flagged for SQL.                
                if (compileResult.GetType().GetField("_unsupportedWarnings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(compileResult) is List<string> _unsupportedWarnings)
                {
                    if (_unsupportedWarnings.Count > 0)
                    {
                        result.UnsupportedReason = _unsupportedWarnings[0];
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
                    if (compileResult.GetType().GetField("_unsupportedWarnings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(compileResult) is List<string> _unsupportedWarnings)
                    {
                        if (_unsupportedWarnings.Count > 0)
                        {
                            result.UnsupportedReason = _unsupportedWarnings[0];
                        }
                    }

                    return result;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed SQL for {expr}");
                Console.WriteLine(compileResult.SqlFunction);
                Console.WriteLine(e.Message);
                throw;
            }
        }

        internal static bool IsError2(string errorKey)
        {
            var fields = typeof(SqlCompileOptions).Assembly.GetTypes().FirstOrDefault(t => t.Name == "SqlCompileException").GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            foreach (var field in fields)
            {
                if (field.FieldType == typeof(ErrorResourceKey))
                {
                    var key2 = (ErrorResourceKey)field.GetValue(null);
                    if (key2.Key == errorKey)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public override bool IsError(FormulaValue value)
        {
            // For CDS compatibility, SQL returns blank for runtime errors
            return value is BlankValue;
        }
    }

    public abstract class BaseRunner
    {
        public static TimeSpan Timeout = TimeSpan.FromSeconds(20.0);

        public Features Features = typeof(Features).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).First().Invoke(null) as Features;

        public bool NumberIsFloat { get; set; }

        protected abstract Task<RunResult> RunAsyncInternal(string expr, string setupHandlerName = null);

        public (TestResult result, string message) RunTestCase(TestCase testCase)
        {
            Task<(TestResult, string)> task = Task.Factory.StartNew(delegate
            {
                Task<(TestResult, string)> task2 = RunAsync2(testCase);
                task2.ConfigureAwait(continueOnCapturedContext: false);
                return task2.Result;
            }, default, TaskCreationOptions.None, TaskScheduler.Default);
            while (true)
            {
                Task.WaitAny(task, Task.Delay(Timeout));
                if (task.IsCompletedSuccessfully)
                {
                    return task.Result;
                }

                if (!Debugger.IsAttached || task.IsCompleted)
                {
                    break;
                }

                Debugger.Log(0, null, $"Test case {testCase} running...\r\n");
            }

            return (TestResult.Fail, $"Timeout after {Timeout}");
        }

        private async Task<(TestResult, string)> RunErrorCaseAsync(TestCase testCase)
        {
            TestCase testCase2 = new TestCase
            {
                SetupHandlerName = testCase.SetupHandlerName,
                SourceLine = testCase.SourceLine,
                SourceFile = testCase.SourceFile,
                Input = "IsError(" + testCase.Input + ")",
                Expected = "true"
            };
            var (testResult, text) = await RunAsync2(testCase2).ConfigureAwait(continueOnCapturedContext: false);
            if (testResult == TestResult.Fail)
            {
                text += " (IsError() followup call)";
            }

            return (testResult, text);
        }

        private async Task<(TestResult, string)> RunAsync2(TestCase testCase)
        {
            string expected = testCase.Expected;
            if (Regex.Match(expected, "^\\s*\\#skip", RegexOptions.IgnoreCase).Success)
            {
                return (TestResult.Skip, "was skipped by request");
            }

            RunResult runResult;
            FormulaValue value;
            FormulaValue originalValue;
            try
            {
                runResult = await RunAsyncInternal(testCase.Input, testCase.SetupHandlerName).ConfigureAwait(continueOnCapturedContext: false);
                value = runResult.Value;
                originalValue = runResult.OriginalValue;
                if (!testCase.IsOverride && runResult.UnsupportedReason != null)
                {
                    return (TestResult.Skip, "Unsupported in this engine: " + runResult.UnsupportedReason);
                }

                if (runResult.Errors != null && runResult.Errors.Length != 0 && (expected.StartsWith("Errors: Error") || expected.StartsWith("Errors: Warning")))
                {
                    string[] array = expected.Replace("Errors: ", string.Empty).Split("|");
                    string[] array2 = runResult.Errors.Select((ExpressionError err) => err.ToString()).ToArray();
                    bool flag = true;
                    string[] array3 = array;
                    foreach (string text in array3)
                    {
                        if (!array2.Contains(text) && (!NumberIsFloat || !array2.Contains(Regex.Replace(text, "(?<!Number,)(\\s|'|\\()Decimal(\\s|'|,|\\.|\\))", "$1Number$2"))))
                        {
                            flag = false;
                            break;
                        }
                    }

                    if (flag)
                    {
                        return (TestResult.Pass, null);
                    }

                    return (TestResult.Fail, "Failed, but wrong error message: Errors: " + string.Join("\r\n", array2));
                }
            }
            catch (SetupHandlerNotFoundException)
            {
                return (TestResult.Skip, "was skipped due to missing setup handler " + testCase.SetupHandlerName);
            }
            catch (Exception ex2)
            {
                return (TestResult.Fail, "Threw exception: " + ex2.Message + ", " + ex2.StackTrace);
            }

            if (!(value is ErrorValue) && expected.StartsWith("Error") && IsError(value) && testCase.Input != null)
            {
                return await RunErrorCaseAsync(testCase).ConfigureAwait(continueOnCapturedContext: false);
            }

            if (value == null)
            {
                string text2 = "Did not return a value";
                if (runResult.Errors != null && runResult.Errors.Any())
                {
                    text2 += string.Join(string.Empty, runResult.Errors.Select((ExpressionError err) => "\r\n" + err));
                }

                return (TestResult.Fail, text2);
            }

            StringBuilder stringBuilder = new StringBuilder();
            FormulaValueSerializerSettings settings = new FormulaValueSerializerSettings
            {
                UseCompactRepresentation = true
            };
            value.ToExpression(stringBuilder, settings);
            string text3 = stringBuilder.ToString();
            if (string.Equals(expected, text3, StringComparison.Ordinal))
            {
                return (TestResult.Pass, null);
            }

            if (double.TryParse(expected, out var result))
            {
                if (originalValue == null || originalValue is NumberValue)
                {
                    if (decimal.TryParse(expected, out var result2) && decimal.Round(result2, 17) != result2)
                    {
                        return (TestResult.Fail, $"\r\n  Float result {result} can't match high precision Decimal expected {expected}");
                    }

                    if (value is NumberValue numberValue)
                    {
                        if (NumberCompare(numberValue.Value, result))
                        {
                            return (TestResult.Pass, null);
                        }
                    }
                    else
                    {
                        if (value is DecimalValue decimalValue && NumberCompare((double)decimalValue.Value, result))
                        {
                            return (TestResult.Pass, null);
                        }
                    }
                }
                else
                {
                    if (originalValue is DecimalValue decimalValue2 && decimal.Parse(expected, NumberStyles.Float) == decimalValue2.Value)
                    {
                        return (TestResult.Pass, null);
                    }
                }
            }

            decimal.TryParse(expected, out var _);
            if (originalValue is DecimalValue decimalValue3)
            {
                if (value is NumberValue numberValue2 && double.TryParse(expected, out var result4))
                {
                    StringBuilder stringBuilder2 = new StringBuilder();
                    originalValue.ToExpression(stringBuilder2, settings);
                    string b = stringBuilder2.ToString();
                    if (string.Equals(expected, b, StringComparison.Ordinal) && NumberCompare(numberValue2.Value, result4))
                    {
                        return (TestResult.Pass, null);
                    }
                }
            }

            return (TestResult.Fail, "\r\n  Expected: " + expected + "\r\n  Actual  : " + text3);
        }

        public virtual string GetName()
        {
            return GetType().Name;
        }

        public virtual bool IsError(FormulaValue value)
        {
            return value is ErrorValue;
        }

        public virtual bool NumberCompare(double a, double b)
        {
            double num = Math.Abs(a - b);
            if (num < 1E-05)
            {
                return true;
            }

            if (b != 0.0 && Math.Abs(num / b) < 1E-14)
            {
                return true;
            }

            return false;
        }
    }

    public class RunResult
    {
        public ExpressionError[] Errors;

        public FormulaValue Value;

        public FormulaValue OriginalValue;

        public string UnsupportedReason;

        public RunResult()
        {
        }

        public RunResult(FormulaValue value, FormulaValue originalValue = null)
        {
            Value = value;
            OriginalValue = originalValue;
        }

        public RunResult(CheckResult result)
        {
            if (!result.IsSuccess)
            {
                Errors = result.Errors.ToArray();
            }
        }

        public static RunResult FromError(string message)
        {
            RunResult runResult = new RunResult
            {
                Errors = new ExpressionError[1]
                {
                    new ExpressionError
                    {
                        Message = message,
                        Severity = ErrorSeverity.Severe
                    }
            }
            };
            return runResult;
        }
    }

    public enum TestResult
    {
        Pass,
        Skip,
        Fail
    }

    public class TestCase
    {
        public string Input;

        public string Expected;

        public string SourceFile;

        public int SourceLine;

        public string SetupHandlerName;

        public string OverrideFrom;

        public bool IsOverride => OverrideFrom != null;

        public void MarkOverride(TestCase newTest)
        {
            OverrideFrom = $"{newTest.SourceFile}:{newTest.SourceLine}";
            Expected = newTest.Expected;
            SourceFile = newTest.SourceFile;
            SourceLine = newTest.SourceLine;
        }

        public string GetUniqueId(string file)
        {
            string text = file ?? Path.GetFileName(SourceFile);
            return text.ToLower() + ":" + Input;
        }

        public override string ToString()
        {
            return $"{Path.GetFileName(SourceFile)}:{SourceLine}: {Input}";
        }
    }

    public class SetupHandlerNotFoundException : Exception
    {
    }

    [Flags]
    public enum TexlParserFlags
    {
        None = 0x0,
        EnableExpressionChaining = 0x1,
        NamedFormulas = 0x2,
        NumberIsFloat = 0x4,
        DisableReservedKeywords = 0x8
    }

    internal class InternalSetup
    {
        internal string HandlerName { get; set; }

        internal TexlParserFlags Flags { get; set; }

        internal Features Features { get; set; }

        internal TimeZoneInfo TimeZoneInfo { get; set; }

        internal bool DisableMemoryChecks { get; set; }

        private static bool TryGetFeaturesProperty(string featureName, out PropertyInfo propertyInfo)
        {
            propertyInfo = typeof(Features).GetProperty(featureName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return propertyInfo?.CanWrite ?? false;
        }

        internal static InternalSetup Parse(string setupHandlerName, bool numberIsFloat = false)
        {
            return Parse(setupHandlerName, typeof(Features).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).First().Invoke(null) as Features, numberIsFloat);
        }

        internal static InternalSetup Parse(string setupHandlerName, Features features, bool numberIsFloat = false)
        {
            InternalSetup internalSetup = new InternalSetup
            {
                Features = features
            };
            if (numberIsFloat)
            {
                internalSetup.Flags |= TexlParserFlags.NumberIsFloat;
            }

            if (string.IsNullOrWhiteSpace(setupHandlerName))
            {
                return internalSetup;
            }

            List<string> list = (from x in setupHandlerName.Split(",")
                                 select x.Trim() into x
                                 where !string.IsNullOrEmpty(x)
                                 select x).ToList();
            string[] array = list.ToArray();
            foreach (string text in array)
            {
                bool flag = false;
                string text2 = text;
                if (text.StartsWith("disable:", StringComparison.OrdinalIgnoreCase))
                {
                    flag = true;
                    text2 = text.Substring("disable:".Length);
                }

                if (string.Equals(text, "DisableMemChecks", StringComparison.OrdinalIgnoreCase))
                {
                    internalSetup.DisableMemoryChecks = true;
                    list.Remove(text);
                }
                else if (Enum.TryParse<TexlParserFlags>(text2, out TexlParserFlags result))
                {
                    if (flag)
                    {
                        internalSetup.Flags &= ~result;
                    }
                    else
                    {
                        internalSetup.Flags |= result;
                    }

                    list.Remove(text);
                }
                else if (TryGetFeaturesProperty(text2, out PropertyInfo propertyInfo))
                {
                    if (flag)
                    {
                        propertyInfo.SetValue(internalSetup.Features, false);
                    }
                    else
                    {
                        propertyInfo.SetValue(internalSetup.Features, true);
                    }

                    list.Remove(text);
                }
                else if (text.StartsWith("TimeZoneInfo", StringComparison.OrdinalIgnoreCase))
                {
                    Match match = new Regex("TimeZoneInfo\\(\"(?<tz>[^)]+)\"\\)", RegexOptions.IgnoreCase).Match(text);
                    if (!match.Success)
                    {
                        throw new ArgumentException("Invalid TimeZoneInfo setup!");
                    }

                    string value = match.Groups["tz"].Value;
                    internalSetup.TimeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(value);
                    list.Remove(text);
                }
            }

            if (list.Count > 1)
            {
                throw new ArgumentException("Too many setup handler names!");
            }

            internalSetup.HandlerName = list.FirstOrDefault();
            return internalSetup;
        }
    }
}
