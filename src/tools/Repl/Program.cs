// <copyright file="Program.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Dataverse;
using Microsoft.PowerFx.Repl.Services;
using Microsoft.PowerFx.Types;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Microsoft.PowerFx
{
    public static class ConsoleRepl
    {
        private static DataverseConnection _dv;

        private static PowerFxREPL _repl;

        private const string OptionFormatTable = "FormatTable";

        private const string OptionNumberIsFloat = "NumberIsFloat";
        private static bool _numberIsFloat = false;

        private const string OptionLargeCallDepth = "LargeCallDepth";
        private static bool _largeCallDepth = false;

        private const string OptionStackTrace = "StackTrace";
        private static bool _stackTrace = false;

        private const string OptionHashCodes = "HashCodes";

        private const string OptionFormatTableColumns = "FormatTableColumns";
        private static HashSet<string> _formatTableColumns;

        private const string OptionFeaturesNone = "FeaturesNone";

        private const string OptionPowerFxV1 = "PowerFxV1";

        private const string BatchFileName = "ReplDV.txt";

        private static bool _reset;

        private static readonly Features _features = Features.PowerFxV1;

        private static RecalcEngine ReplRecalcEngine()
        {
            _dv = null;

            var config = new PowerFxConfig(_features);

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

            config.SymbolTable.EnableAIFunctions();

            config.EnableSetFunction();
            config.EnableParseJSONFunction();

            config.AddFunction(new ResetFunction());
            config.AddFunction(new ExitFunction());
            config.AddFunction(new Option0Function());
            config.AddFunction(new Option1Function());
            config.AddFunction(new Option2FunctionBool());
            config.AddFunction(new Option2FunctionString());
            config.AddFunction(new DVConnectFunction1Arg());
            config.AddFunction(new DVConnectFunction2Arg());
            config.AddFunction(new DVAddTableFunction());

            var optionsSet = new OptionSet("Options", DisplayNameUtility.MakeUnique(options));

            config.AddOptionSet(optionsSet);

            config.EnableRegExFunctions(new TimeSpan(0, 0, 5));
            var eng = new RecalcEngine(config);
            eng.EnableDelegation();
            return eng;
        }

        public static void Main()
        {
            Console.InputEncoding = System.Text.Encoding.Unicode;
            Console.OutputEncoding = System.Text.Encoding.Unicode;

            var version = typeof(RecalcEngine).Assembly.GetName().Version.ToString();
            Console.WriteLine($"Microsoft Power Fx Console Formula REPL, Version {version}");

#pragma warning disable CA1303 // Do not pass literals as localized parameters
            Console.WriteLine("Enter Excel formulas.  Use \"Help()\" for details, \"Option()\" for options.");
#pragma warning restore CA1303 // Do not pass literals as localized parameters

            InteractiveRepl();
        }

        public static PowerFxREPL CreateRepl()
        {
            // used by the AI functions with a stub to return an error if used before connecting to Dataverse
            var innerServices = new BasicServiceProvider();
            innerServices.AddService<IDataverseExecute>(new DataverseNotPresent());

            var repl = new PowerFxREPL
            {
                Engine = ReplRecalcEngine(),
                ValueFormatter = new StandardFormatter(),
                AllowSetDefinitions = true,
                InnerServices = innerServices
            };
            repl.EnableSampleUserObject();
            repl.Engine.EnableDelegation();
            _repl = repl;
            return repl;
        }

        public static void ProcessAutoexec(PowerFxREPL repl)
        {
            var oldEcho = repl.Echo;
            repl.Echo = true;

            var batchPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\" + BatchFileName;
            if (File.Exists(batchPath))
            {
                Console.WriteLine($"\n>> // Processing {batchPath}");
                var lines = File.ReadAllLines(batchPath);

                foreach (var line in lines)
                {
                    repl.HandleLineAsync(line).Wait();
                }
            }
            else
            {
                Console.WriteLine($"\n>> // Place autoexec formulas in {batchPath}");
            }

            repl.Echo = oldEcho;
        }

        public static void InteractiveRepl()
        {
            while (true)
            {
                var repl = CreateRepl();

                ProcessAutoexec(repl);

                while (!_reset)
                {
                    repl.WritePromptAsync().Wait();
                    var line = Console.ReadLine();
                    repl.HandleLineAsync(line).Wait();
                }

                _reset = false;
            }
        }

        private class ResetFunction : ReflectionFunction
        {
            public BooleanValue Execute()
            {
                _reset = true;
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
                IOrganizationService svcClient;

                var connectionString = connectionSV.Value;
                svcClient = new ServiceClient(connectionString) { UseWebApi = false };

                if (multiOrg.Value)
                {
                    _dv = MultiOrgPolicy.New(svcClient, numberIsFloat: _numberIsFloat);
                }
                else
                {
                    _dv = SingleOrgPolicy.New(svcClient, numberIsFloat: _numberIsFloat);
                }
                _repl.ExtraSymbolValues = _dv.SymbolValues;

                UpdateUserInfo(svcClient);

                // used by the AI functions and now we have a valid service to work with
                var clientExecute = new DataverseService(svcClient);
                var innerServices = new BasicServiceProvider();
                innerServices.AddService<IDataverseExecute>(clientExecute);
                _repl.InnerServices = innerServices;

                try
                {
                    AddCustomApisAsync(clientExecute).Wait();
                }
                catch(Exception e)
                {
                    // Non-fatal error 
                    Console.WriteLine($"Failed to add APIs: {e.Message}");
                }

                return BooleanValue.New(true);
            }

            private async Task AddCustomApisAsync(DataverseService clientExecute)
            {
                List<CustomApiSignature> sigs = new List<CustomApiSignature>();

                // Org can have 100s of plugins. We don't want to load them all.
                // Add Low code plugins ...  Fewer, and more useful. 
                Console.WriteLine("Loading Low Code Plugins:");
                CustomApiEntity[] customApis = await clientExecute.GetLowCodeApiNamesAsync();

                foreach (var customApi in customApis)
                {
                    var sig = await clientExecute.GetApiSignatureAsync(customApi);
                    sigs.Add(sig);
                }

                foreach(var sig in sigs)
                { 
                    _dv.AddPlugin(sig);
                    Console.WriteLine($"Added: Environment.{sig.Api.uniquename}(...)");
                }
            }

            private void UpdateUserInfo(IOrganizationService svcClient)
            {
                // Get current user Id
                var req = new Microsoft.Crm.Sdk.Messages.WhoAmIRequest();
                var resp = (Microsoft.Crm.Sdk.Messages.WhoAmIResponse)svcClient.Execute(req);

                // Get properties
                const string userTable = "systemuser";
                const string fullName = "fullname";
                const string email = "internalemailaddress";
                var resp2 = svcClient.Retrieve(userTable, resp.UserId, new ColumnSet(fullName, email));

                var basicUserInfo = new BasicUserInfo
                {
                    FullName = (string)resp2.Attributes[fullName],
                    Email = (string)resp2.Attributes[email],
                    DataverseUserId = resp.UserId,
                    EntraObjectId = new Guid("00000000-0000-0000-0000-000000000000"),
                    TeamsMemberId = string.Empty
                };

                _repl.UserInfo = basicUserInfo.UserInfo;
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

        private class Option0Function : ReflectionFunction
        {
            public Option0Function()
                : base("Option", FormulaType.String)
            {
            }

            public FormulaValue Execute()
            {
                StringBuilder sb = new StringBuilder();

                sb.Append("\n");

                sb.Append($"{"FormatTable:",-42}{((StandardFormatter)_repl.ValueFormatter).FormatTable}\n");
                sb.Append($"{"HashCodes:",-42}{((StandardFormatter)_repl.ValueFormatter).HashCodes}\n");
                sb.Append($"{"NumberIsFloat:",-42}{_numberIsFloat}\n");
                sb.Append($"{"LargeCallDepth:",-42}{_largeCallDepth}\n");
                sb.Append($"{"StackTrace:",-42}{_stackTrace}\n");

                foreach (var prop in typeof(Features).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (prop.PropertyType == typeof(bool) && prop.CanWrite)
                    {
                        sb.Append($"{prop.Name + ((bool)prop.GetValue(Features.PowerFxV1) ? " (V1)" : string.Empty) + ":",-42}{prop.GetValue(_features)}\n");
                    }
                }

                return FormulaValue.New(sb.ToString());
            }
        }

        // displays a single setting
        private class Option1Function : ReflectionFunction
        {
            public Option1Function()
                : base("Option", FormulaType.Boolean, new[] { FormulaType.String })
            {
            }

            public FormulaValue Execute(StringValue option)
            {
                if (string.Equals(option.Value, OptionFormatTable, StringComparison.OrdinalIgnoreCase))
                {
                    return BooleanValue.New(((StandardFormatter)_repl.ValueFormatter).FormatTable);
                }

                if (string.Equals(option.Value, OptionNumberIsFloat, StringComparison.OrdinalIgnoreCase))
                {
                    return BooleanValue.New(_numberIsFloat);
                }

                if (string.Equals(option.Value, OptionLargeCallDepth, StringComparison.OrdinalIgnoreCase))
                {
                    return BooleanValue.New(_largeCallDepth);
                }

                if (string.Equals(option.Value, OptionHashCodes, StringComparison.OrdinalIgnoreCase))
                {
                    return BooleanValue.New(((StandardFormatter)_repl.ValueFormatter).HashCodes);
                }

                if (string.Equals(option.Value, OptionStackTrace, StringComparison.OrdinalIgnoreCase))
                {
                    return BooleanValue.New(_stackTrace);
                }

                return FormulaValue.NewError(new ExpressionError()
                {
                    Kind = ErrorKind.InvalidArgument,
                    Severity = ErrorSeverity.Critical,
                    Message = $"Invalid option name: {option.Value}.  Use \"Option()\" to see available Options enum names."
                });
            }
        }

        // change a setting
        private class Option2FunctionBool : ReflectionFunction
        {
            // explicit constructor needed so that the return type from Execute can be FormulaValue and acoomodate both booleans and errors
            public Option2FunctionBool()
                : base("Option", FormulaType.Boolean, new[] { FormulaType.String, FormulaType.Boolean })
            {
            }

            public FormulaValue Execute(StringValue option, BooleanValue value)
            {
                if (string.Equals(option.Value, OptionFormatTable, StringComparison.OrdinalIgnoreCase))
                {
                    ((StandardFormatter)_repl.ValueFormatter).FormatTable = value.Value;
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
                    _reset = true;
                    return value;
                }

                if (string.Equals(option.Value, OptionHashCodes, StringComparison.OrdinalIgnoreCase))
                {
                    ((StandardFormatter)_repl.ValueFormatter).HashCodes = value.Value;
                    return value;
                }

                if (string.Equals(option.Value, OptionStackTrace, StringComparison.OrdinalIgnoreCase))
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

                    _reset = true;
                    return value;
                }

                if (string.Equals(option.Value, OptionFeaturesNone, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var prop in typeof(Features).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (prop.PropertyType == typeof(bool) && prop.CanWrite)
                        {
                            prop.SetValue(_features, value.Value);
                        }
                    }

                    _reset = true;
                    return value;
                }

                var featureProperty = typeof(Features).GetProperty(option.Value, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (featureProperty?.CanWrite == true)
                {
                    featureProperty.SetValue(_features, value.Value);
                    _reset = true;
                    return value;
                }

                return FormulaValue.NewError(new ExpressionError()
                {
                    Kind = ErrorKind.InvalidArgument,
                    Severity = ErrorSeverity.Critical,
                    Message = $"Invalid option name: {option.Value}.  Use \"Option()\" to see available Options enum names."
                });
            }
        }

        private class Option2FunctionString : ReflectionFunction
        {
            // explicit constructor needed so that the return type from Execute can be FormulaValue and acoomodate both booleans and errors
            public Option2FunctionString()
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
    }
}
