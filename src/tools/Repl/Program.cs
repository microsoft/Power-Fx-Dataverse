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
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx
{
    public static class ConsoleRepl
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
            FullName = "First Last",
            Email = "unknown@contoso.com",
            DataverseUserId = new Guid("00000000-0000-1111-1111-111111111111"),
            TeamsMemberId = "29:1111111111111111111111111111",
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

#pragma warning disable CS0618 // Type or member is obsolete
        private static PowerFxREPL _repl;

        public static PowerFxREPL CreateRepl(bool echo)
        {
            var repl = new PowerFxREPL
            {
                Engine = _engine,
                UserInfo = _userInfo.UserInfo,
                Echo = echo,
                AllowSetDefinitions = true,                
            };
            repl.EnableUserObject(UserInfo.AllKeys);

            _repl = repl;
            return repl;
        }
#pragma warning restore CS0618 // Type or member is obsolete

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


                return BooleanValue.New(true);
            }

            private void UpdateUserInfo(IOrganizationService svcClient)
            {
                // Get current user Id
                var req = new Microsoft.Crm.Sdk.Messages.WhoAmIRequest();
                var resp = (Microsoft.Crm.Sdk.Messages.WhoAmIResponse)svcClient.Execute(req);

                _userInfo.DataverseUserId = resp.UserId;


                // Get properties
                const string userTable = "systemuser";
                const string fullName = "fullname";
                const string email = "internalemailaddress";

                var resp2 = svcClient.Retrieve(userTable, resp.UserId, new ColumnSet(fullName, email));
                _userInfo.FullName = (string)resp2.Attributes[fullName];
                _userInfo.Email = (string)resp2.Attributes[email];

                _repl.UserInfo = _userInfo.UserInfo;
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
