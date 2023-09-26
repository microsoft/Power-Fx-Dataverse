// <copyright file="Program.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Texl.Builtins;
using Microsoft.PowerFx.Dataverse;
using Microsoft.PowerFx.Types;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

namespace Microsoft.PowerFx
{
    public static class ConsoleRepl2
    {
        private static RecalcEngine _engine;

        private static DataverseConnection _dv;

        private const string OptionFormatTable = "FormatTable";
        private static bool _formatTable = true;

        private const string OptionNumberIsFloat = "NumberIsFloat";
        private static bool _numberIsFloat = false;

        private const string OptionLargeCallDepth = "LargeCallDepth";
        private static bool _largeCallDepth = false;

        private const string OptionStackTrace = "StackTrace";
        private static bool _stackTrace = false;

        private const string OptionFormatTableColumns = "FormatTableColumns";
        private static HashSet<string> _formatTableColumns;

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
                { OptionFormatTableColumns, OptionFormatTableColumns }
            };

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

            config.AddFunction(new ResetFunction());
            config.AddFunction(new ExitFunction());
            config.AddFunction(new OptionFunctionBool());
            config.AddFunction(new OptionFunctionString());

#if false
            config.AddFunction(new ResetImportFunction());
            config.AddFunction(new ImportFunction1Arg());
            config.AddFunction(new ImportFunction2Arg());
#endif

            config.AddFunction(new DVConnectFunction1Arg());
            config.AddFunction(new DVConnectFunction2Arg());
            config.AddFunction(new DVAddTableFunction());

            var optionsSet = new OptionSet("Options", DisplayNameUtility.MakeUnique(options));

            config.AddOptionSet(optionsSet);

#pragma warning disable CS0618 // Type or member is obsolete
            config.EnableRegExFunctions(new TimeSpan(0, 0, 5));
#pragma warning restore CS0618 // Type or member is obsolete

            _engine = new RecalcEngine(config);
        }

        public static void Main()
        {
            var enabled = new StringBuilder();

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
                var lines = File.ReadAllLines(batchPath);

                var repl = CreateRepl(echo: true);

                foreach (var line in lines)
                {
                    repl.HandleLineAsync(line).Wait();
                }                
            }
            else
            {
                Console.WriteLine($"\n>> // Place autoexec formulas in {batchPath}");
            }

            InteractiveRepl();
        }

        // Hook repl engine with customizations.
        private class MyRepl : PowerFxReplBase
        {
            public MyRepl()
            {
            }

            public override async Task OnEvalExceptionAsync(Exception e, CancellationToken cancel)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(e.Message);

                if (ConsoleRepl2._stackTrace)
                {
                    Console.WriteLine(e.ToString());
                }

                Console.ResetColor();
            }

            public override async Task<ReplResult> HandleCommandAsync(string expr, CancellationToken cancel = default)
            {
                this.Engine = _engine; // apply latest engine. 
                this.ExtraSymbolValues = _dv?.SymbolValues;

                // Intercept to enable  some experimentla commands 

                Match match;

                // named formula definition: <ident> = <formula>
                if ((match = Regex.Match(expr, @"^\s*(?<ident>(\w+|'([^']|'')+'))\s*=(?<formula>.*)$", RegexOptions.Singleline)).Success &&
                              !Regex.IsMatch(match.Groups["ident"].Value, "^\\d") &&
                              match.Groups["ident"].Value != "true" && match.Groups["ident"].Value != "false" && match.Groups["ident"].Value != "blank")
                {
                    var ident = match.Groups["ident"].Value;
                    if (ident.StartsWith('\''))
                    {
                        ident = ident.Substring(1, ident.Length - 2).Replace("''", "'", StringComparison.Ordinal);
                    }

                    _engine.SetFormula(ident, match.Groups["formula"].Value, OnUpdate);

                    return new ReplResult();
                }
                else
                {
                    // Default to standard behavior. 
                    return await base.HandleCommandAsync(expr, cancel).ConfigureAwait(false);
                }
            }
        }

        public static PowerFxReplBase CreateRepl(bool echo)
        {
            var repl = new MyRepl
            {
                Engine = _engine,
                UserInfo = _userInfo.UserInfo,
                Echo = echo,
                AllowSetDefinitions = true,
                // ValueFormatter = new MyValueFormatter()
            };
            repl.EnableUserObject(UserInfo.AllKeys);

            return repl;
        }

        public static void InteractiveRepl()
        {
            var repl = CreateRepl(false);

            while (true)
            {
                repl.WritePromptAsync().Wait();
                var line = Console.ReadLine();
                repl.HandleLineAsync(line).Wait();
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

        /*
        private class MyValueFormatter : ValueFormatter
        {
            public override string Format(FormulaValue result)
            {                
                if (result is TableValue table)
                {
                    var firstRow = table.Rows.FirstOrDefault();
                    if (firstRow != null && firstRow.IsValue)
                    {
                        var record = firstRow.Value;
                        if (record.TryGetSpecialFieldValue(SpecialFieldKind.PrimaryKey, out _) &&
                            record.TryGetSpecialFieldName(SpecialFieldKind.PrimaryName, out _))
                        {
                            // Has special fields, print those. 

                            int maxN = 10;
                            var drows = table.Rows.Take(maxN);

                            StringBuilder sb = new StringBuilder();
                            foreach(var drow in drows)
                            {
                                if (drow.IsValue)
                                {
                                    var row = drow.Value;

                                    if (row.TryGetSpecialFieldValue(SpecialFieldKind.PrimaryKey, out var keyValue) &&
                                        row.TryGetSpecialFieldValue(SpecialFieldKind.PrimaryName, out var nameValue))
                                    {
                                        var keyStr = PrintResult(keyValue);
                                        var nameStr = PrintResult(nameValue);

                                        sb.AppendLine($"{keyStr}: {nameStr}");
                                    }   
                                }
                            }

                            return sb.ToString();
                        }
                    }
                }

                return PrintResult(result);
            }
        }
        */

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
                else if (record.Fields.Count() == 1 && record.Fields.First().Name == "Value")
                {
                    resultString = new StringBuilder("{");
                    resultString.Append("Value:");
                    resultString.Append(record.Fields.First().GetPrintField());
                    resultString.Append("}");
                }
                else
                {
                    var separator = string.Empty;
                    var fieldNames = _formatTableColumns != null ? _formatTableColumns : record.Type.FieldNames;

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

                    var fieldNames = _formatTableColumns != null ? _formatTableColumns : table.Type.FieldNames;
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
    }
}
