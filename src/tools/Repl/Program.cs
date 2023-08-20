// <copyright file="Program.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Dataverse;
using Microsoft.PowerFx.Types;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.PowerFx.Dataverse.Tests;
using Microsoft.PowerFx.Core.Tests;
using SharpYaml.Model;

namespace Microsoft.PowerFx
{
    public static class ConsoleRepl
    {
        private static RecalcEngine _engine;

        private static DataverseConnection _dv;

        private static ExpressionEvaluationTests _SQLTests;

        private const string OptionFormatTable = "FormatTable";
        private static bool _formatTable = true;

        private const string OptionNumberIsFloat = "NumberIsFloat";
        private static bool _numberIsFloat = false;

        private const string OptionLargeCallDepth = "LargeCallDepth";
        private static bool _largeCallDepth = false;

        private const string OptionStackTrace = "StackTrace";
        private static bool _stackTrace = false;

        private const string OptionSQLEval = "SQLEval";
        private static bool _SQLEval = false;

        private const string OptionFormatTableColumns = "FormatTableColumns";
        private static HashSet<string> _formatTableColumns;

        private static HashSet<string> _replFunctions;

        private const string OptionFeaturesNone = "FeaturesNone";

        private const string OptionPowerFxV1 = "PowerFxV1";

        private const string BatchFileName = "ReplDV.txt";

        private static readonly BasicUserInfo _userInfo = new BasicUserInfo
        {
            FullName = "Susan Burk",
            Email = "susan@contoso.com",
            DataverseUserId = new Guid("aa1d4f65-044f-4928-a95f-30d4c8ebf118"),
            TeamsMemberId = "29:1DUjC5z4ttsBQa0fX2O7B0IDu30R",
        };

        private static readonly Features _features = Features.PowerFxV1;

        private static void ResetEngine()
        {
            _dv = null;

            var props = new Dictionary<string, object>
            {
                { "FullName", _userInfo.FullName },
                { "Email", _userInfo.Email },
                { "DataverseUserId", _userInfo.DataverseUserId },
                { "TeamsMemberId", _userInfo.TeamsMemberId }
            };

            var allKeys = props.Keys.ToArray();
            SymbolTable userSymbolTable = new SymbolTable();

            userSymbolTable.AddUserInfoObject(allKeys);

            var config = new PowerFxConfig(_features) { SymbolTable = userSymbolTable };

            if (_largeCallDepth)
            {
                config.MaxCallDepth = 200;
            }

            Dictionary<string, string> options = new Dictionary<string, string>
            {
                { OptionFormatTable, OptionFormatTable },
                { OptionNumberIsFloat, OptionNumberIsFloat },
                { OptionLargeCallDepth, OptionLargeCallDepth },
                { OptionFeaturesNone, OptionFeaturesNone },
                { OptionPowerFxV1, OptionPowerFxV1 },
                { OptionStackTrace, OptionStackTrace },
                { OptionFormatTableColumns, OptionFormatTableColumns },
                { OptionSQLEval, OptionSQLEval }
            };

            _replFunctions = new HashSet<string> { };

            foreach (var featureProperty in typeof(Features).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (featureProperty.PropertyType == typeof(bool) && featureProperty.CanWrite)
                {
                    var feature = featureProperty.Name;
                    options.Add(feature.ToString(), feature.ToString());
                }
            }

            config.SymbolTable.EnableMutationFunctions();

            config.EnableSetFunction();
            config.EnableParseJSONFunction();

            config.AddFunction(new HelpFunction());
            _replFunctions.Add("Help");

            config.AddFunction(new ResetFunction());
            _replFunctions.Add("Reset");

            config.AddFunction(new ExitFunction());
            _replFunctions.Add("Exit");

            config.AddFunction(new OptionFunctionBool());
            config.AddFunction(new OptionFunctionString());
            _replFunctions.Add("Option");

            config.AddFunction(new ResetImportFunction());
            _replFunctions.Add("ResetImport");

            config.AddFunction(new ImportFunction1Arg());
            config.AddFunction(new ImportFunction2Arg());
            _replFunctions.Add("Import");

            config.AddFunction(new DVConnectFunction1Arg());
            config.AddFunction(new DVConnectFunction2Arg());
            _replFunctions.Add("DVConnect");

            config.AddFunction(new DVAddTableFunction());
            _replFunctions.Add("DVAddTable");

            var optionsSet = new OptionSet("Options", DisplayNameUtility.MakeUnique(options));

            config.AddOptionSet(optionsSet);

#pragma warning disable CS0618 // Type or member is obsolete
            config.EnableRegExFunctions(new TimeSpan(0, 0, 5));
#pragma warning restore CS0618 // Type or member is obsolete

            _engine = new RecalcEngine(config);

            _SQLTests = new ExpressionEvaluationTests(null, null);
        }

        public static void Main()
        {
            var enabled = new StringBuilder();

            Console.InputEncoding = System.Text.Encoding.Unicode;
            Console.OutputEncoding = System.Text.Encoding.Unicode;

            ResetEngine();

            var version = typeof(RecalcEngine).Assembly.GetName().Version.ToString();
            Console.WriteLine($"Microsoft Power Fx Console Formula REPL for Dataverse, Version {version}");

            foreach (var propertyInfo in typeof(Features).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                if (propertyInfo.PropertyType == typeof(bool) && ((bool)propertyInfo.GetValue(_engine.Config.Features)) == true)
                {
                    enabled.Append(" " + propertyInfo.Name);
                }
            }

            if (enabled.Length == 0)
            {
                enabled.Append(" <none>");
            }

            Console.WriteLine($"Experimental features enabled:{enabled}");

#pragma warning disable CA1303 // Do not pass literals as localized parameters
            Console.WriteLine($"Enter Excel formulas.  Use \"Help()\" for details.");
#pragma warning restore CA1303 // Do not pass literals as localized parameters

            var batchPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\" + BatchFileName;
            if (File.Exists(batchPath))
            {
                Console.WriteLine($"\n>> // Processing {batchPath}");
                var batchFile = File.OpenText(batchPath);
                REPL(batchFile, echo: true);
                batchFile.Close();
            }
            else
            {
                Console.WriteLine($"\n>> // Place autoexec formulas in {batchPath}");
            }

            REPL(Console.In, echo: false);
        }

        // Pattern match for Set(x,y) so that we can define the variable
        public static bool TryMatchSet(string expr, out string arg0name, out FormulaValue varValue)
        {
            var parserOptions = _engine.GetDefaultParserOptionsCopy();
            parserOptions.AllowsSideEffects = true;

            var parse = Engine.Parse(expr, options: parserOptions);
            if (parse.IsSuccess)
            {
                if (parse.Root.Kind == Microsoft.PowerFx.Syntax.NodeKind.Call)
                {
                    if (parse.Root is Microsoft.PowerFx.Syntax.CallNode call)
                    {
                        if (call.Head.Name.Value == "Set")
                        {
                            // Infer type based on arg1. 
                            var arg0 = call.Args.ChildNodes[0];
                            if (arg0 is Microsoft.PowerFx.Syntax.FirstNameNode arg0node)
                            {
                                arg0name = arg0node.Ident.Name.Value;

                                var arg1 = call.Args.ChildNodes[1];
                                var arg1expr = arg1.GetCompleteSpan().GetFragment(expr);

                                var check = _engine.Check(arg1expr, GetParserOptions(), GetSymbolTable());
                                if (check.IsSuccess)
                                {
                                    var arg1Type = check.ReturnType;

                                    varValue = check.GetEvaluator().Eval(GetRuntimeConfig());
                                    _engine.UpdateVariable(arg0name, varValue);

                                    return true;
                                }
                            }
                        }
                    }
                }
            }

            varValue = null;
            arg0name = null;
            return false;
        }

        private static ParserOptions GetParserOptions()
        {
            return new ParserOptions() { AllowsSideEffects = true, NumberIsFloat = _numberIsFloat };
        }

        private static ReadOnlySymbolTable GetSymbolTable()
        {
            return ReadOnlySymbolTable.Compose(_dv?.Symbols, _engine.EngineSymbols);
        }

        // Check may mutate the SymbolValues.  It's important to call this after Check has been run.
        private static RuntimeConfig GetRuntimeConfig()
        {
            var rc = new RuntimeConfig(_dv?.SymbolValues);
            rc.SetUserInfo(_userInfo);
            return rc;
        }

        private static FormulaValue Eval(string expressionText)
        {
            Match match;

            match = Regex.Match(expressionText, @"^\s*(?<func>[a-zA-Z]+)\(", RegexOptions.Singleline);

            if (_SQLEval && !(match.Success && _replFunctions.Contains(match.Groups["func"].Value)) )
            {
                return _SQLTests.RunExpr(expressionText);
            }
            else
            {
                CheckResult checkResult = _engine.Check(expressionText, GetParserOptions(), GetSymbolTable());
                checkResult.ThrowOnErrors();

                IExpressionEvaluator evaluator = checkResult.GetEvaluator();
                return evaluator.Eval(GetRuntimeConfig());
            }
        }

        public static void REPL(TextReader input, bool echo = false, TextWriter output = null)
        {
            string expr;

            // main loop
            while ((expr = ReadFormula(input, output, echo)) != null)
            {
                Match match;

                try
                {
                    // variable assignment: Set( <ident>, <expr> )
                    if (TryMatchSet(expr, out var varName, out var varValue))
                    {
                        Console.WriteLine(varName + ": " + PrintResult(varValue));
                        output?.WriteLine(varName + ": " + PrintResult(varValue));
                    }

                    // IR pretty printer: IR( <expr> )
                    else if ((match = Regex.Match(expr, @"^\s*IR\((?<expr>.*)\)\s*$", RegexOptions.Singleline)).Success)
                    {
                        var cr = _engine.Check(match.Groups["expr"].Value, GetParserOptions(), GetSymbolTable());
                        var ir = cr.PrintIR();
                        Console.WriteLine(ir);
                        output?.WriteLine(ir);
                        cr.ThrowOnErrors();
                    }

                    // SQL pretty printer: SQL( <expr> )
                    else if ((match = Regex.Match(expr, @"^\s*SQL\((?<expr>.*)\)\s*$", RegexOptions.Singleline)).Success)
                    {
                        var sql = _SQLTests.SQLExpr(match.Groups["expr"].Value);
                        Console.Write(sql);
                        output?.Write(sql);
                    }

                    // named formula definition: <ident> = <formula>
                    else if ((match = Regex.Match(expr, @"^\s*(?<ident>(\w+|'([^']|'')+'))\s*=(?<formula>.*)$", RegexOptions.Singleline)).Success &&
                              !Regex.IsMatch(match.Groups["ident"].Value, "^\\d") &&
                              match.Groups["ident"].Value != "true" && match.Groups["ident"].Value != "false" && match.Groups["ident"].Value != "blank")
                    {
                        var ident = match.Groups["ident"].Value;
                        if (ident.StartsWith('\''))
                        {
                            ident = ident.Substring(1, ident.Length - 2).Replace("''", "'", StringComparison.Ordinal);
                        }

                        _engine.SetFormula(ident, match.Groups["formula"].Value, OnUpdate);
                    }

                    // function definition: <ident>( <ident> : <type>, ... ) : <type> = <formula>
                    //                      <ident>( <ident> : <type>, ... ) : <type> { <formula>; <formula>; ... }
                    else if (Regex.IsMatch(expr, @"^\s*\w+\((\s*\w+\s*\:\s*\w+\s*,?)*\)\s*\:\s*\w+\s*(\=|\{).*$", RegexOptions.Singleline))
                    {
                        var res = _engine.DefineFunctions(expr, _numberIsFloat);
                        if (res.Errors.Count() > 0)
                        {
                            throw new Exception("Error: " + res.Errors.First());
                        }
                    }

                    // eval and print everything else
                    else
                    {
                        var opts = new ParserOptions() { AllowsSideEffects = true, NumberIsFloat = _numberIsFloat };
#if true
                        var result = Eval(expr);
#else
                        var result = _engine.EvalAsync(expr, CancellationToken.None, GetParserOptions(), GetSymbolTable(), GetRuntimeConfig()).Result;
#endif

                        if (output != null)
                        {
                            // same algorithm used by BaseRunner.cs
                            var sb = new StringBuilder();
                            var settings = new FormulaValueSerializerSettings()
                            {
                                UseCompactRepresentation = true,
                            };

                            // Serializer will produce a human-friedly representation of the value
                            result.ToExpression(sb, settings);

                            output.Write(sb.ToString() + "\n\n");
                        }

                        if (result is ErrorValue errorValue)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Error: {errorValue.Errors[0].Kind} - {errorValue.Errors[0].Message}");
                            Console.ResetColor();
                        }
                        else
                        {
                            Console.WriteLine(PrintResult(result));
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(e.Message);
                    if (_stackTrace)
                    {
                        Console.WriteLine(e.ToString());
                    }
                    Console.ResetColor();
                    output?.WriteLine(Regex.Replace(e.InnerException.Message, "\r\n", "|") + "\n");
                }
            }
        }

        private static void OnUpdate(string name, FormulaValue newValue)
        {
            Console.Write($"{name}: ");
            if (newValue is ErrorValue errorValue)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error: " + errorValue.Errors[0].Message);
                Console.ResetColor();
            }
            else
            {
                if (newValue is TableValue)
                {
                    Console.WriteLine();
                }

                Console.WriteLine(PrintResult(newValue));
            }
        }

        public static string ReadFormula(TextReader input, TextWriter output = null, bool echo = false)
        {
            string exprPartial;
            int usefulCount;

            // read
            do
            {
                string exprOne;
                int parenCount;

                exprPartial = null;

                do
                {
                    bool doubleQuote, singleQuote;
                    bool lineComment, blockComment;
                    char last;

                    if (exprPartial == null && !echo)
                    {
#pragma warning disable CA1303 // Do not pass literals as localized parameters
                        Console.Write("\n> ");
#pragma warning restore CA1303 // Do not pass literals as localized parameters
                    }

                    exprOne = input.ReadLine();

                    if (exprOne == null)
                    {
                        Console.Write("\n");
                        return exprPartial;
                    }

                    exprPartial += exprOne + "\n";

                    // determines if the parens, curly braces, and square brackets are closed
                    // taking into escaping, block, and line comments
                    // and continues reading lines if they are not, with a blank link terminating
                    parenCount = 0;
                    doubleQuote = singleQuote = lineComment = blockComment = false;
                    last = '\0';
                    usefulCount = 0;
                    foreach (var c in exprPartial)
                    {
                        // don't need to worry about escaping as it looks like two 
                        if (c == '"' && !singleQuote)
                        {
                            doubleQuote = !doubleQuote; // strings that are back to back
                        }

                        if (c == '\'' && !doubleQuote)
                        {
                            singleQuote = !singleQuote;
                        }

                        if (c == '*' && last == '/' && !blockComment)
                        {
                            blockComment = true;
                            usefulCount--;                         // compensates for the last character already being added
                        }

                        if (c == '/' && last == '*' && blockComment)
                        {
                            blockComment = false;
                            usefulCount--;
                        }

                        if (!doubleQuote && !singleQuote && !blockComment && !lineComment && c == '/' && last == '/')
                        {
                            lineComment = true;
                            usefulCount--;
                        }

                        if (c == '\n')
                        {
                            lineComment = false;
                        }

                        if (!lineComment && !blockComment && !doubleQuote && !singleQuote)
                        {
                            if (c == '(' || c == '{' || c == '[')
                            {
                                parenCount++;
                            }

                            if (c == ')' || c == '}' || c == ']')
                            {
                                parenCount--;
                            }
                        }

                        if (!char.IsWhiteSpace(c) && !lineComment && !blockComment)
                        {
                            usefulCount++;
                        }

                        last = c;
                    }
                }
                while (!Regex.IsMatch(exprOne, "^\\s*$") && (parenCount != 0 || Regex.IsMatch(exprOne, "(=|=\\>)\\s*$")));

                if (echo)
                {
                    if (!Regex.IsMatch(exprPartial, "^\\s*$"))
                    {
                        Console.Write("\n>> " + exprPartial);
                    }

                    // skip >> for comments and setup on output
                    if (Regex.IsMatch(exprPartial, "^\\s*//") || Regex.IsMatch(exprPartial, "^#SETUP"))
                    {
                        output?.Write(exprPartial + "\n");
                        usefulCount = 0;
                    }
                    else if (!Regex.IsMatch(exprPartial, "^\\s*$"))
                    {
                        output?.Write(">> " + exprPartial);
                    }
                }
            }
            while (usefulCount == 0);

            return exprPartial;
        }

        internal static string PrintResult(FormulaValue value, bool minimal = false)
        {
            StringBuilder resultString;

            if (value is BlankValue)
            {
                resultString = new StringBuilder(minimal ? string.Empty : "Blank()");
            }
            else if (value is ErrorValue errorValue)
            {
                resultString = new StringBuilder(minimal ? "<error>" : $"<Error: {errorValue.Errors[0].Message}>");
            }
            else if (value is UntypedObjectValue)
            {
                resultString = new StringBuilder(minimal ? "<untyped>" : "<Untyped: Use Value, Text, Boolean, or other functions to establish the type>");
            }
            else if (value is StringValue str)
            {
                resultString = new StringBuilder(minimal ? str.Value : str.ToExpression());
            }
            else if (value is RecordValue record)
            {
                if (minimal)
                {
                    resultString = new StringBuilder("<record>");
                }
                else  if (record.Fields.Count() == 1 && record.Fields.First().Name == "Value")
                {
                    resultString = new StringBuilder("{");
                    resultString.Append("Value:");
                    resultString.Append(record.Fields.First().GetPrintField());
                    resultString.Append("}");
                }
                else
                {
                    var separator = string.Empty;
                    var fieldNames = _formatTableColumns ?? record.Type.FieldNames;

                    resultString = new StringBuilder("{");

                    foreach (NamedValue field in record.Fields)
                    {
                        if (fieldNames.Contains(field.Name))
                        {
                            resultString.Append(separator);
                            resultString.Append(field.Name);
                            resultString.Append(':');
                            resultString.Append(field.GetPrintField());
                            separator = ", ";
                        }
                    }

                    resultString.Append('}');
                }
            }
            else if (value is TableValue table)
            {
                if (minimal)
                {
                    resultString = new StringBuilder("<table>");
                }

                // special treatment for single column table named Value
                else if (table.Rows.First().Value.Fields.Count() == 1 && table.Rows.First().Value != null && table.Rows.First().Value.Fields.First().Name == "Value")
                {
                    var separator = string.Empty;
                    resultString = new StringBuilder("[");
                    foreach (var row in table.Rows)
                    {
                        resultString.Append(separator);
                        resultString.Append(row.Value.Fields.First().GetPrintField());
                        separator = ", ";
                    }

                    resultString.Append("]");
                }

                else
                {
                    // otherwise a full table treatment is needed

                    var fieldNames = _formatTableColumns ?? table.Type.FieldNames;
                    var columnCount = fieldNames.Count();

                    if (columnCount == 0)
                    {
                        return minimal ? string.Empty : "Table()";
                    }

                    var columnWidth = new int[columnCount];

                    foreach (var row in table.Rows)
                    {
                        if (row.Value != null)
                        {
                            var column = 0;

                            foreach (NamedValue field in row.Value.Fields)
                            {
                                if (fieldNames.Contains(field.Name))
                                {
                                    columnWidth[column] = Math.Max(columnWidth[column], field.GetPrintField(true).Length);
                                    column++;
                                }
                            }
                        }
                    }

                    if (_formatTable)
                    {
                        resultString = new StringBuilder("\n ");
                        var column = 0;

                        foreach (var row in table.Rows)
                        {
                            if (row.Value != null)
                            {
                                column = 0;

                                foreach (NamedValue field in row.Value.Fields)
                                {
                                    if (fieldNames.Contains(field.Name))
                                    {
                                        columnWidth[column] = Math.Max(columnWidth[column], field.Name.Length);
                                        resultString.Append(' ');
                                        resultString.Append(field.Name.PadLeft(columnWidth[column]));
                                        resultString.Append("  ");
                                        column++;
                                    }
                                }

                                break;
                            }
                        }

                        resultString.Append("\n ");

                        foreach (var row in table.Rows)
                        {
                            if (row.Value != null)
                            {
                                column = 0;

                                foreach (NamedValue field in row.Value.Fields)
                                {
                                    if (fieldNames.Contains(field.Name))
                                    {
                                        resultString.Append(new string('=', columnWidth[column] + 2));
                                        resultString.Append(' ');
                                        column++;
                                    }
                                }

                                break;
                            }
                        }

                        foreach (var row in table.Rows)
                        {
                            column = 0;
                            resultString.Append("\n ");
                            if (row.Value != null)
                            {
                                foreach (NamedValue field in row.Value.Fields)
                                {
                                    if (fieldNames.Contains(field.Name))
                                    {
                                        resultString.Append(' ');
                                        resultString.Append(field.GetPrintField(true).PadLeft(columnWidth[column]));
                                        resultString.Append("  ");
                                        column++;
                                    }
                                }                               
                            }
                            else
                            {
                                resultString.Append(row.IsError ? row.Error?.Errors?[0].Message : "Blank()");
                            }
                        }
                    }
                    else
                    {
                        // table without formatting 

                        resultString = new StringBuilder("[");
                        var separator = string.Empty;
                        foreach (var row in table.Rows)
                        {
                            resultString.Append(separator);
                            resultString.Append(PrintResult(row.Value));
                            separator = ", ";
                        }

                        resultString.Append(']');
                    }
                }
            }
            else
            {
                resultString = new StringBuilder();
                var settings = new FormulaValueSerializerSettings() { UseCompactRepresentation = true };
                value.ToExpression(resultString, settings);                
            }

            return resultString.ToString();
        }

        private class ResetFunction : ReflectionFunction
        {
            public BooleanValue Execute()
            {
                ResetEngine();
                return FormulaValue.New(true);
            }
        }

        private class ExitFunction : ReflectionFunction
        {
            public BooleanValue Execute()
            {
                System.Environment.Exit(0);
                return FormulaValue.New(true);
            }
        }

        private class OptionFunctionString : ReflectionFunction
        {
            // explicit constructor needed so that the return type from Execute can be FormulaValue and acoomodate both booleans and errors
            public OptionFunctionString()
                : base("Option", FormulaType.String, new[] { FormulaType.String, FormulaType.String })
            {
            }

            public FormulaValue Execute(StringValue option, StringValue value)
            {
                if (string.Equals(option.Value, OptionFormatTableColumns, StringComparison.OrdinalIgnoreCase))
                {
                    _formatTableColumns = new HashSet<string>();
                    foreach (Match match in Regex.Matches(value.Value, "\\w+", RegexOptions.None, TimeSpan.FromSeconds(5)))
                    {
                        _formatTableColumns.Add(match.Value);
                    }
                    if (_formatTableColumns.Count() == 0)
                    {
                        _formatTableColumns = null;
                        return StringValue.New("ALL");
                    }
                    else
                    {
                        return StringValue.New(string.Join(",", _formatTableColumns));
                    }
                }

                return FormulaValue.NewError(new ExpressionError()
                {
                    Kind = ErrorKind.InvalidArgument,
                    Severity = ErrorSeverity.Critical,
                    Message = $"Invalid option name: {option.Value}."
                });
            }
        }

        private class OptionFunctionBool : ReflectionFunction
        {
            // explicit constructor needed so that the return type from Execute can be FormulaValue and acoomodate both booleans and errors
            public OptionFunctionBool()
                : base("Option", FormulaType.Boolean, new[] { FormulaType.String, FormulaType.Boolean })
            {
            }

            public FormulaValue Execute(StringValue option, BooleanValue value)
            {
                if (string.Equals(option.Value, OptionFormatTable, StringComparison.OrdinalIgnoreCase))
                {
                    _formatTable = value.Value;
                    return value;
                }

                if (string.Equals(option.Value, OptionNumberIsFloat, StringComparison.OrdinalIgnoreCase))
                {
                    _numberIsFloat = value.Value;
                    return value;
                }

                if (string.Equals(option.Value, OptionLargeCallDepth, StringComparison.OrdinalIgnoreCase))
                {
                    _largeCallDepth = value.Value;
                    ResetEngine();
                    return value;
                }

                if (string.Equals(option.Value, OptionSQLEval, StringComparison.OrdinalIgnoreCase))
                {
                    _SQLEval = value.Value;
                    return value;
                }

                if (option.Value.ToLower(CultureInfo.InvariantCulture) == OptionStackTrace.ToLower(CultureInfo.InvariantCulture))
                {
                    _stackTrace = value.Value;
                    return value;
                }

                if (string.Equals(option.Value, OptionPowerFxV1, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var prop in typeof(Features).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (prop.PropertyType == typeof(bool) && prop.CanWrite && (bool)prop.GetValue(Features.PowerFxV1))
                        {
                            prop.SetValue(_features, value.Value);
                        }
                    }

                    ResetEngine();
                    return value;
                }

                if (option.Value.ToLower(CultureInfo.InvariantCulture) == OptionFeaturesNone.ToLower(CultureInfo.InvariantCulture))
                {
                    foreach (var prop in typeof(Features).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (prop.PropertyType == typeof(bool) && prop.CanWrite)
                        {
                            prop.SetValue(_features, value.Value);
                        }
                    }

                    ResetEngine();
                    return value;
                }

                var featureProperty = typeof(Features).GetProperty(option.Value, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (featureProperty?.CanWrite == true)
                {
                    featureProperty.SetValue(_features, value.Value);
                    ResetEngine();
                    return value;
                }

                return FormulaValue.NewError(new ExpressionError()
                {
                        Kind = ErrorKind.InvalidArgument,
                        Severity = ErrorSeverity.Critical,
                        Message = $"Invalid option name: {option.Value}."
                });
            }
        }

        private class ImportFunction1Arg : ReflectionFunction
        {
            public BooleanValue Execute(StringValue fileNameSV)
            {
                var if2 = new ImportFunction2Arg();
                return if2.Execute(fileNameSV, null);
            }
        }

        private class ImportFunction2Arg : ReflectionFunction
        {
            public BooleanValue Execute(StringValue fileNameSV, StringValue outputSV)
            {
                var fileName = fileNameSV.Value;
                if (File.Exists(fileName))
                {
                    TextReader fileReader = new StreamReader(fileName, true);
                    TextWriter outputWriter = null;

                    if (outputSV != null)
                    {
                        outputWriter = new StreamWriter(outputSV.Value, false, System.Text.Encoding.UTF8);
                    }

                    ConsoleRepl.REPL(fileReader, true, outputWriter);
                    fileReader.Close();
                    outputWriter?.Close();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"File not found: {fileName}");
                    Console.ResetColor();
                    return BooleanValue.New(false);
                }

                return BooleanValue.New(true);
            }
        }

        private class DVConnectFunction1Arg : ReflectionFunction
        {
            public BooleanValue Execute(StringValue connectionSV)
            {
                var dvc2 = new DVConnectFunction2Arg();
                return dvc2.Execute(connectionSV, BooleanValue.New(false));
            }
        }

        private class DVConnectFunction2Arg : ReflectionFunction
        {
            public BooleanValue Execute(StringValue connectionSV, BooleanValue multiOrg)
            {
                IOrganizationService _svcClient;

                var connectionString = connectionSV.Value;
                _svcClient = new ServiceClient(connectionString) { UseWebApi = false };

                if (multiOrg.Value)
                {
                    _dv = MultiOrgPolicy.New(_svcClient, numberIsFloat: _numberIsFloat);
                }
                else
                {
                    _dv = SingleOrgPolicy.New(_svcClient, numberIsFloat: _numberIsFloat);
                }

                return BooleanValue.New(true);
            }
        }

        private class DVAddTableFunction : ReflectionFunction
        {
            public BooleanValue Execute(StringValue localNameSV, StringValue logNameSV)
            {
                _dv.AddTable(localNameSV.Value, logNameSV.Value);

                return BooleanValue.New(true);
            }
        }

        private class ResetImportFunction : ReflectionFunction
        {
            public BooleanValue Execute(StringValue fileNameSV)
            {
                var import = new ImportFunction1Arg();
                if (File.Exists(fileNameSV.Value))
                {
                    ResetEngine();
                }

                return import.Execute(fileNameSV);
            }
        }

        private class HelpFunction : ReflectionFunction
        {
            public BooleanValue Execute()
            {
                var column = 0;
                var funcList = string.Empty;
                List<string> funcNames = _engine.SupportedFunctions.FunctionNames.ToList();
                
                funcNames.Sort();
                foreach (var func in funcNames)
                {
                    funcList += $"  {func,-14}";
                    if (++column % 5 == 0)
                    {
                        funcList += "\n";
                    }
                }

                funcList += "  Set";

                // If we return a string, it gets escaped. 
                // Just write to console 
                Console.WriteLine(
                @"
<formula> alone is evaluated and the result displayed.
    Example: 1+1 or ""Hello, World""
Set( <identifier>, <formula> ) creates or changes a variable's value.
    Example: Set( x, x+1 )

<identifier> = <formula> defines a named formula with automatic recalc.
    Example: F = m * a
<identifier>( <param> : <type>, ... ) : <type> = <formula> 
        extends a named formula with parameters, creating a function.
    Example: F( m: Number, a: Number ): Number = m * a
<identifier>( <param> : <type>, ... ) : <type> { 
       <expression>; <expression>; ...
       }  defines a block function with chained formulas.
    Example: Log( message: String ): None 
             { 
                    Collect( LogTable, message );
                    Notify( message );
             }
Supported types: Number, String, Boolean, DateTime, Date, Time

Available functions (all are case sensitive):
" + funcList + @"

Available operators: = <> <= >= + - * / % && And || Or ! Not in exactin 

Record syntax is { < field >: < value >, ... } without quoted field names.
    Example: { Name: ""Joe"", Age: 29 }
Use the Table function for a list of records.  
    Example: Table( { Name: ""Joe"" }, { Name: ""Sally"" } )
Use [ <value>, ... ] for a single column table, field name is ""Value"".
    Example: [ 1, 2, 3 ] 
Records and Tables can be arbitrarily nested.

Use Option( Options.FormatTable, false ) to disable table formatting.

Once a formula is defined or a variable's type is defined, it cannot be changed.
Use the Reset() function to clear all formulas and variables.
");

                return FormulaValue.New(true);
            }
        }
    }
}

internal static class Exts
{
    public static string GetPrintField(this NamedValue field, bool minimal = false)
    {
        return field.IsExpandEntity ? "<EE>" : Microsoft.PowerFx.ConsoleRepl.PrintResult(field.Value, minimal);
    }
}
