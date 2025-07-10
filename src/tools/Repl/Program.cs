// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Dataverse;
using Microsoft.PowerFx.Repl;
using Microsoft.PowerFx.Repl.Functions;
using Microsoft.PowerFx.Repl.Services;
using Microsoft.PowerFx.Types;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
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

        private static SqlConnection _sqlConnection;

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
                { OptionFormatTableColumns, OptionFormatTableColumns },
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
            config.AddFunction(new Option0Function());
            config.AddFunction(new Option1Function());
            config.AddFunction(new Option2FunctionBool());
            config.AddFunction(new Option2FunctionString());
            config.AddFunction(new DVConnectFunction1Arg());
            config.AddFunction(new DVConnectFunction2Arg());
            config.AddFunction(new DVAddTableFunction());
            config.AddFunction(new SQLConnect0Function());
            config.AddFunction(new SQLConnect1Function());

            var optionsSet = new OptionSet("Options", DisplayNameUtility.MakeUnique(options));

            config.AddOptionSet(optionsSet);
            config.EnableJoinFunction();

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
            Console.WriteLine("Enter Excel formulas.  Use \"Help()\" for details, \"Option()\" for options,");
            Console.WriteLine("\"DVConnect()\" to connect to Dataverse, \"SQLConnect()\" for SQL execution.");
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
            repl.AddPseudoFunction(new IRPseudoFunction());
            repl.AddPseudoFunction(new CIRPseudoFunction());
            repl.AddPseudoFunction(new SQLPseudoFunction());
            repl.AddPseudoFunction(new SQLEvalPseudoFunction());
            repl.AddPseudoFunction(new DVMetadataPseudoFunction());

            //repl.Engine.EnableDelegation();
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
                repl.HandleLineAsync($"// Processing {batchPath}").Wait();
                var lines = File.ReadAllLines(batchPath);

                foreach (var line in lines)
                {
                    repl.HandleLineAsync(line).Wait();
                }
            }
            else
            {
                repl.HandleLineAsync($"// Place autoexec formulas in {batchPath}").Wait();
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

                    if (repl.ExitRequested)
                    {
                        if (_sqlConnection != null)
                        {
                            _sqlConnection.Close();
                        }

                        return;
                    }
                }

                _reset = false;
            }
        }

        private class ResetFunction : ReflectionFunction
        {
            public ResetFunction()
                : base("Reset", FormulaType.Void)
            {
            }

            public FormulaValue Execute()
            {
                _reset = true;
                return FormulaValue.NewVoid();
            }
        }

        private class DVConnectFunction1Arg : ReflectionFunction
        {
            public DVConnectFunction1Arg()
                : base("DVConnect", FormulaType.Void, new[] { FormulaType.String })
            {
            }

            public VoidValue Execute(StringValue connectionSV)
            {
                var dvc2 = new DVConnectFunction2Arg();
                return dvc2.Execute(connectionSV, BooleanValue.New(false));
            }
        }

        private class DVConnectFunction2Arg : ReflectionFunction
        {
            public DVConnectFunction2Arg()
                : base("DVConnect", FormulaType.Void, new[] { FormulaType.String, FormulaType.Boolean })
            {
            }

            public VoidValue Execute(StringValue connectionSV, BooleanValue multiOrg)
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

                UpdateUserInfo(svcClient);

                // used by the AI functions and now we have a valid service to work with
                var clientExecute = new DataverseService(svcClient);
                var innerServices = new BasicServiceProvider();
                innerServices.AddService<IDataverseExecute>(clientExecute);
                _repl.InnerServices = innerServices;

                var symbolValue = new SymbolValues();

                symbolValue.AddEnvironmentVariables(clientExecute.GetEnvironmentVariablesAsync().Result);
                
                _repl.ExtraSymbolValues = ReadOnlySymbolValues.Compose(symbolValue, _dv.SymbolValues);

                try
                {
                    AddCustomApisAsync(clientExecute).Wait();
                }
                catch (Exception e)
                {
                    // Non-fatal error
                    Console.WriteLine($"Failed to add APIs: {e.Message}");
                }

                return BooleanValue.NewVoid();
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

                foreach (var sig in sigs)
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

        private class SQLConnect0Function : ReflectionFunction
        {
            public SQLConnect0Function()
                : base("SQLConnect", FormulaType.Void)
            {
            }

            public FormulaValue Execute()
            {
                var connectionString = Environment.GetEnvironmentVariable("FxTestSQLDatabase");
                if (connectionString == null)
                {
                    var error = new ExpressionError() { Message = $"Error: Environment variable FxTestSQLDatabase not set" };
                    return FormulaValue.NewError(error);
                }

                var sqlConnect1 = new SQLConnect1Function();
                return sqlConnect1.Execute(StringValue.New(connectionString));
            }
        }

        private class SQLConnect1Function : ReflectionFunction
        {
            public SQLConnect1Function()
                : base("SQLConnect", FormulaType.Void, new[] { FormulaType.String })
            {
            }

            public FormulaValue Execute(StringValue connectionString)
            {
                _sqlConnection = new SqlConnection(connectionString.Value);
                try
                {
                    _sqlConnection.Open();
                }
                catch (DbException ex)
                {
                    _sqlConnection = null;

                    var error = new ExpressionError() { Message = $"Error: Failed to connect to SQL Server: {ex.Message}" };
                    return FormulaValue.NewError(error);
                }

                return FormulaValue.NewVoid();
            }
        }

        // extracted from RunAsyncInternal in src\PowerFx.Dataverse.Tests\PowerFxEvaluationTests.cs
        public static class SQLHelpers
        {
            public static SqlCompileResult Compile(string expr)
            {
                var options = new SqlCompileOptions
                {
                    CreateMode = SqlCompileOptions.Mode.Create,
                    UdfName = null // will auto generate with guid.
                };

                var engine = new PowerFx2SqlEngine(PowerFx2SqlEngine.Empty(), dataverseFeatures: new DataverseFeatures() { IsFloatingPointEnabled = true });
                var compileResult = engine.Compile(expr, options);

                if (compileResult.IsSuccess)
                {
                    return compileResult;
                }
                else
                {
                    foreach (var error in compileResult.Errors)
                    {
                        _repl.Output.WriteLineAsync($"Error: {error.Message}", OutputKind.Error);
                    }

                    return null;
                }
            }
        }

        public class SQLPseudoFunction : IPseudoFunction
        {
            public string Name => "SQL";

            public async Task ExecuteAsync(CheckResult checkResult, PowerFxREPL repl, CancellationToken cancel)
            {
                var compileResult = SQLHelpers.Compile(checkResult.Parse.Text);
                if (compileResult != null)
                {
                    _repl.Output.WriteAsync(compileResult.SqlFunction, OutputKind.Repl);
                }
            }
        }

        public class SQLEvalPseudoFunction : IPseudoFunction
        {
            public string Name => "SQLEval";

            public async Task ExecuteAsync(CheckResult checkResult, PowerFxREPL repl, CancellationToken cancel)
            {
                var compileResult = SQLHelpers.Compile(checkResult.Parse.Text);
                if (compileResult != null)
                {
                    try
                    {
                        using SqlTransaction tx = _sqlConnection.BeginTransaction();
                        using SqlCommand createCmd = _sqlConnection.CreateCommand();

                        createCmd.Transaction = tx;
                        createCmd.CommandText = compileResult.SqlFunction;
                        int rows = createCmd.ExecuteNonQuery();

                        createCmd.CommandText = $@"CREATE TABLE placeholder (
    [placeholderid] UNIQUEIDENTIFIER DEFAULT NEWID() PRIMARY KEY,
    [dummy] INT NULL,
    [calc]  AS ([dbo].{compileResult.SqlCreateRow}))";
                        createCmd.ExecuteNonQuery();

                        var insertCmd = _sqlConnection.CreateCommand();
                        insertCmd.Transaction = tx;
                        insertCmd.CommandText = "INSERT INTO placeholder (dummy) VALUES (7)";
                        insertCmd.ExecuteNonQuery();

                        var selectCmd = _sqlConnection.CreateCommand();
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

                            _repl.Output.WriteLineAsync(_repl.ValueFormatter.Format(fv), OutputKind.Repl);
                        }
                    }
                    catch (Exception e)
                    {
                        _repl.Output.WriteLineAsync($"Error: Failed SQL execution for {checkResult.Parse.Text}", OutputKind.Error);
                        _repl.Output.WriteLineAsync(e.Message, OutputKind.Error);
                    }
                }
            }
        }

        private class DVMetadataPseudoFunction : IPseudoFunction
        {
            public string Name => "DVMetadata";

            public async Task ExecuteAsync(CheckResult checkResult, PowerFxREPL repl, CancellationToken cancel)
            {
                string Format(decimal? x)
                {
                    return x == null ? "null" : ((decimal)x).ToString("#,##0.##############");
                }

                if (checkResult.ReturnType is TableType table && table.TableSymbolName != null)
                {
                    if (_dv.MetadataCache.TryGetXrmEntityMetadata(table.TableSymbolName, out var entity))
                    {
                        var sb = new StringBuilder();

                        sb.AppendLine($"Entity: {entity.LogicalName}");
                        sb.AppendLine($"  SchemaName: {entity.SchemaName}");
                        sb.AppendLine($"  DisplayName: {entity.DisplayName?.UserLocalizedLabel?.Label ?? "<none>"}");
                        sb.AppendLine($"  ObjectTypeCode: {entity.ObjectTypeCode}");
                        sb.AppendLine($"  IsCustomEntity: {entity.IsCustomEntity}");
                        sb.AppendLine($"  IsLogicalEntity: {entity.IsLogicalEntity}");
                        sb.AppendLine($"  Attributes ({entity.Attributes?.Length ?? 0}):");
                        if (entity.Attributes != null)
                        {
                            var atts = entity.Attributes;
                            for (int i = 0; i < atts.Length; i++)
                            {
                                var attr = atts[i];

                                var primary = (bool)attr.IsPrimaryName ? "primaryName " : ((bool)attr.IsPrimaryId ? "primaryId " : string.Empty);
                                var ro = !(bool)attr.IsValidForUpdate ? "readOnly " : string.Empty;

                                var typeInfo = attr switch
                                {
                                    PicklistAttributeMetadata pl => (bool)pl.OptionSet.IsGlobal ? $"<{pl.OptionSet.Name}>" : "<local>",
                                    DecimalAttributeMetadata dec => $"<{Format(dec.MaxValue)}:{Format(dec.MinValue)}:{dec.Precision}>",
                                    MoneyAttributeMetadata mon => $"<{Format((decimal)mon.MaxValue)}:{Format((decimal)mon.MinValue)}:{mon.Precision}>",
                                    BigIntAttributeMetadata big => $"<{Format((decimal)big.MaxValue)}:{Format((decimal)big.MinValue)}>",
                                    DoubleAttributeMetadata dbl => $"<{Format((decimal)dbl.MaxValue)}:{Format((decimal)dbl.MinValue)}:{dbl.Precision}>",
                                    _ => string.Empty
                                };

                                sb.AppendLine($"    - {attr.LogicalName} {attr.AttributeType}{typeInfo} {primary}{ro}");

                                // skip name fields that accompany the main attribute
                                if (i + 1 < atts.Length && atts[i].LogicalName + "name" == atts[i + 1].LogicalName && atts[i + 1].AttributeType == AttributeTypeCode.Virtual)
                                {
                                    i++;
                                }
                            }
                        }

                        sb.AppendLine($"  OneToManyRelationships: {entity.OneToManyRelationships?.Length ?? 0}");
                        sb.AppendLine($"  ManyToOneRelationships: {entity.ManyToOneRelationships?.Length ?? 0}");
                        sb.AppendLine($"  ManyToManyRelationships: {entity.ManyToManyRelationships?.Length ?? 0}");

                        _repl.Output.WriteAsync(sb.ToString(), OutputKind.Repl);
                    }
                    else
                    {
                        _repl.Output.WriteLineAsync($"Error: failed to retrieve metadata for {table.TableSymbolName}", OutputKind.Error);
                    }
                }
                else
                {
                    _repl.Output.WriteLineAsync("Error: Can only be used on a single Dataverse table, for example DVMetadata(account)", OutputKind.Error);
                }
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

        private class MyHelpProvider : HelpProvider
        {
            public override async Task Execute(PowerFxREPL repl, CancellationToken cancel, string context = null)
            {
                var msg =
@"
Autoexec formulas can be put in ~/repldv.txt.

DVConnect( DataverseConnectionString [, MultiOrg ] )
    Connects to Dataverse.
    Second argument is Boolean: true = MultiOrg, false = SingleOrg (default).

DVAddTable( DataverseTable )
    If the table was not added with DVConnect (MultiOrg), add each manually here.

DVMetadata( DataverseTable )
    Displays metadata for the table.

SQLConnect( [ SQLConnectionString ] )
    Connect to SQL Server for SQLEval.
    If connection string not provided, attempts to use environment variable 
    ""FxTestSQLDatabase"" which is used by the test suite.

SQL( Formula )
    Display compiled T-SQL for Formula.

SQLEeval( Formula )
    Compile the formula to SQL and run on SQL Server.

Reset() 
    Resets the engine and reruns autoexec.
   
";

                await WriteAsync(repl, msg, cancel)
                    .ConfigureAwait(false);
            }
        }
    }
}
