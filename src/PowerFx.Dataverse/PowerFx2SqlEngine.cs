//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Core.Errors;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Logging;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using static Microsoft.PowerFx.Dataverse.SqlVisitor.Context;


namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Allows compiling to SQL. 
    /// </summary>
    public sealed class PowerFx2SqlEngine : DataverseEngine
    {
        // default decimal precision
        internal const int DefaultPrecision = 10;

        internal static readonly Features DefaultFeatures = Features.PowerFxV1;

        // This NumberIsFloat should be removed when the SQL compiler is running on native Decimal
        // Tracked with https://github.com/microsoft/Power-Fx-Dataverse/issues/117
        public PowerFx2SqlEngine(
            EntityMetadata currentEntityMetadata = null,
            CdsEntityMetadataProvider metadataProvider = null,
            CultureInfo culture = null)
            : base(currentEntityMetadata, metadataProvider, new PowerFxConfig(DefaultFeatures), culture, numberIsFloat: false)
        {
        }

        // Called by Engine after Bind to collect additional errors. 
        // this can walk the tree and apply SQL-engine specific restrictions. 
        protected override IEnumerable<ExpressionError> PostCheck(CheckResult result)
        {
            var errors = PostCheck2(result);
            return ExpressionError.New(errors);
        }

        private IEnumerable<IDocumentError> PostCheck2(CheckResult result)
        { 
            var expression = result.Parse.Text;
            var binding = result.ApplyBindingInternal();

            if (result.IsSuccess)
            {
                try
                {
                    var returnType = BuildReturnType(binding.ResultType);

                    // SQL visitor will throw errors for SQL-specific constraints.
                    var sqlInfo = result.ApplySqlCompiler();
                    var res = sqlInfo._retVal;

                    FormulaType nodeType = res.type == new SqlBigType() ? FormulaType.Decimal :  res.type;

                    var errors = new List<IDocumentError>();
                    if (!ValidateReturnType(new SqlCompileOptions(), nodeType, binding.Top.GetTextSpan(), out returnType, out var typeErrors, allowEmptyExpression: true, expression))
                    {
                        errors.AddRange(typeErrors);
                    }

                    if (errors.Count > 0)
                    {
                        return errors;
                    }


                    if (result.IsSuccess)
                    {
                        result.ReturnType = returnType;
                    }
                }
                catch (SqlCompileException ex)
                {
                    // append the entire span for any errors with an unknown span
                    return ex.GetErrors(binding.Top.GetTextSpan());
                }
                catch (NotImplementedException)
                {
                    return new SqlCompileException(SqlCompileException.NotSupported, binding.Top.GetTextSpan()).GetErrors(binding.Top.GetTextSpan());
                }
                catch (Exception ex)
                {
                    return new[] {
                        new TexlError(binding.Top, DocumentErrorSeverity.Critical, TexlStrings.ErrGeneralError, ex.Message)
                        };
                }
            }

            return new IDocumentError[0];
        }

        // Compile the formula to SQL that can be called from CDS. 
        // Expression: "new_CurrencyPrice * new_Quantity";
        public SqlCompileResult Compile(string expression, SqlCompileOptions options)
        {
            // don't perform SQL validations during the initial check, as they will be done as part of the compilation
            SqlCompileResult sqlResult = new SqlCompileResult(this);
            sqlResult.SetText(expression);
            sqlResult.SetBindingInfo();
            var binding = sqlResult.ApplyBindingInternal();

            // attempt to produce a sanitized formula, success or failure, for reporting
            var sanitizedFormula = StructuralPrint.Print(binding.Top, binding, new NameSanitizer(binding));

            sqlResult.ApplyErrors(); // will invoke post-check hook

            if (!sqlResult.IsSuccess)
            {
                sqlResult.SanitizedFormula = sanitizedFormula;

                // Note if we fail due to unsupported functions or unsupported function tabular overloads.
                sqlResult._unsupportedWarnings = new List<string>();
                foreach (var error in sqlResult.Errors)
                {
                    if (error.MessageKey == "ErrUnknownFunction" || 
                        error.MessageKey == "ErrUnimplementedFunction" ||
                        error.MessageKey == "ErrNumberExpected" || // remove when fixed: https://github.com/microsoft/Power-Fx/issues/1375
                        error.MessageKey == "ErrNumberTooLarge" || // Numeric value is too large.
                        (error.MessageKey == "ErrBadType_ExpectedType_ProvidedType" && error._messageArgs?.Length == 2 && error._messageArgs.Contains("Table")))
                    {
                        sqlResult._unsupportedWarnings.Add(error.Message);
                    }

                }
                return sqlResult;
            }

            try
            {
                var sqlInfo = sqlResult.ApplySqlCompiler();
                var ctx = sqlInfo._ctx;
                var result = sqlInfo._retVal;
                var irNode = sqlResult.ApplyIR().TopNode;

                // if no function content generated (for scenarios which perform no logic), set the return
                if (ctx._sbContent.Length == 0)
                {
                    result = ctx.SetIntermediateVariable(irNode, fromRetVal: result);
                }

                FormulaType nodeType = result.type == new SqlBigType() ? FormulaType.Decimal : result.type;

                if (!ValidateReturnType(options, nodeType, irNode.IRContext.SourceContext, out var retType, out var errors))
                {
                    var errorResult = new SqlCompileResult(errors);
                    errorResult.SanitizedFormula = sanitizedFormula;
                    return errorResult;
                }

                StringWriter tw = new StringWriter();

                var funcName = (string.IsNullOrWhiteSpace(options.UdfName))
                    ? "fn_udf_" + Guid.NewGuid().ToString("n")
                    : options.UdfName;

                string verb = (options.CreateMode == SqlCompileOptions.Mode.Alter) ? "ALTER" : "CREATE";
                tw.WriteLine($"{verb} FUNCTION {funcName}(");

                // Write parameter list - these are the fields we depend on
                var parameters = ctx.GetParameters().ToList();
                for (var i = 0; i < parameters.Count(); i++)
                {
                    var del = (i == parameters.Count - 1) ? "" : ",";
                    var fieldName = parameters[i].Item1.LogicalName;
                    var varName = ctx.GetVarName(fieldName, ctx.RootScope, null);
                    // Existing CDS SQL generation passes money values as big type
                    var typeName = parameters[i].Item2 is SqlMoneyType ? SqlVisitor.ToSqlType(new SqlBigType()) : SqlVisitor.ToSqlType(parameters[i].Item2);
                    tw.WriteLine($"    {varName} {typeName}{del} -- {fieldName}");
                }

                tw.WriteLine($") RETURNS {SqlVisitor.ToSqlType(retType)}");
                // schemabinding only applies if there are no reference fields and formula field doesn't use any time bound functions
                var refFieldCount = ctx.GetReferenceFields().Count();
                if (refFieldCount == 0 && !ctx.expressionHasTimeBoundFunction)
                {
                    tw.WriteLine($"  {SqlStatementFormat.WithSchemaBindingFormat}");
                }
                tw.WriteLine($"AS BEGIN");

                var indent = "    ";

                Dictionary<Tuple<string, string, string>, List<VarDetails>> initRefFieldsMap = null;
                if (refFieldCount > 0)
                {
                    initRefFieldsMap = new Dictionary<Tuple<string, string, string>, List<VarDetails>>();

                    // Declare and prepare to initialize any reference fields, by organizing them by table and relationship fields
                    foreach (var field in ctx.GetReferenceFields())
                    {
                        var sqlType = SqlVisitor.ToSqlType(field.VarType);
                        tw.WriteLine($"{indent}DECLARE {field.VarName} {sqlType}");
                        string referencing = null;
                        string referenced = null;
                        if (field.Navigation != null)
                        {
                            referencing = field.Navigation.ReferencingFieldName;
                            var referencedLogical = field.Navigation.TargetFieldNames[0];
                            referenced = _metadataCache.GetColumnSchemaName(field.Navigation.TargetTableNames[0], field.Navigation.TargetFieldNames[0]);

                        }
                        else if (field.Column.RequiresReference())
                        {
                            // for calculated or logical fields on the root scope, use the primary key for referencing and referenced
                            // NOTE: the referencing needs to be the logical name, but the referenced needs to be the schema name
                            referencing = field.Scope.Type.CdsTableDefinition().PrimaryKeyColumn;
                            referenced = field.Scope.Type.CdsColumnDefinition(referencing).SchemaName;
                        }

                        // fields in the relationship itself don't need be initialized
                        if (referencing != null)
                        {
                            // use the field path to construct a path to the referencing field
                            DPath referencingPath = field.Path.Length > 1 ? field.Path.Parent.Parent : new DPath();
                            referencingPath = referencingPath.Append(new DName(referencing));

                            var referencingVar = ctx.GetVarName(referencingPath, field.Scope, null, create: false);
                            var tableSchemaName = _metadataCache.GetTableSchemaName(field.Table);

                            // Table Schema name returns table view and we need to refer Base tables  in UDF in case of non logical fields hence Suffixing Base to the Schema Name
                            // because logical fields can only be referred from view 
                            if (!field.Column.IsLogical)
                            {
                                tableSchemaName = tableSchemaName + "Base";
                            }

                            // the key should include the schema name of the table, the var name for the referencing field, and the schema name of the referenced field
                            var key = new Tuple<string, string, string>(tableSchemaName, referencingVar, referenced);
                            if (!initRefFieldsMap.TryGetValue(key, out var fields))
                            {
                                fields = new List<VarDetails>();
                                initRefFieldsMap.Add(key, fields);
                            }
                            fields.Add(field);
                        }
                    }
                }

                // Declare temps 
                foreach (var temp in ctx.GetTemps())
                {
                    tw.WriteLine($"{indent}DECLARE {temp.Item1} {SqlVisitor.ToSqlType(temp.Item2)}");
                }

                if (ctx.DoesDateDiffOverflowCheck)
                {
                    tw.WriteLine($"{indent}{SqlStatementFormat.VariableDeclarationForDateTimeOverflowChecks}");
                }

                if (refFieldCount > 0)
                {
                    foreach (var pair in initRefFieldsMap)
                    {
                        // Initialize the reference field values from the primary field
                        var selects = String.Join(",", pair.Value.Select((VarDetails field) => { return $"{field.VarName} = [{field.Column.SchemaName}]"; }));
                        tw.WriteLine($"{indent}SELECT TOP(1) {selects} FROM [dbo].[{pair.Key.Item1}] WHERE[{pair.Key.Item3}] = {pair.Key.Item2}");
                    }
                }

                tw.WriteLine();
                tw.WriteLine($"{indent}-- expression body");

                tw.Write(ctx._sbContent);

                tw.WriteLine($"{indent}-- end expression body");
                tw.WriteLine();

                EmitReturn(tw, indent, ctx, result, retType, options);
                tw.WriteLine($"END");
                                
                sqlResult.SqlFunction = tw.ToString();

                // Write actual function definition 
                tw = new StringWriter();
                {
                    string dil = "";
                    tw.Write($"{funcName}(");

                    foreach (var details in parameters)
                    {
                        var fieldName = details.Item1.SchemaName;
                        tw.Write(dil);
                        tw.Write($"[{fieldName}]");
                        dil = ",";
                    }

                    tw.WriteLine(")");
                }
                sqlResult.SqlCreateRow = tw.ToString();

                var dependentFields = ctx.GetDependentFields();

                // The top-level identifiers are the logical names of fields on the main entity
                sqlResult.ApplyDependencyAnalysis();
                var topLevelIdentifiers = dependentFields.ContainsKey(_currentEntityName) ? dependentFields[_currentEntityName] : new HashSet<string>();
                sqlResult.TopLevelIdentifiers.Clear();
                sqlResult.TopLevelIdentifiers.UnionWith(topLevelIdentifiers);

                // related identifiers are the the logical names of fields on other entities
                sqlResult.RelatedIdentifiers = dependentFields.Where(pair => pair.Key != _currentEntityName).ToDictionary(pair => pair.Key, pair => pair.Value);

                sqlResult.DependentRelationships = ctx.GetDependentRelationships();

                sqlResult.ReturnType = retType;
                sqlResult.LogicalFormula = this.GetInvariantExpression(expression, null, _cultureInfo);
                sqlResult.SanitizedFormula = sanitizedFormula;

                sqlResult._unsupportedWarnings = ctx._unsupportedWarnings;
                return sqlResult;
            }
            catch (NotImplementedException)
            {
                var errorResult = new SqlCompileResult(
                    new SqlCompileException(SqlCompileException.NotSupported, binding.Top.GetTextSpan()).GetErrors(binding.Top.GetTextSpan())
                );
                errorResult.SanitizedFormula = sanitizedFormula;
                return errorResult;
            }
            catch (Exception ex)
            {
                var errorResult = new SqlCompileResult(
                    new[] {
                        new TexlError(binding.Top, DocumentErrorSeverity.Critical, TexlStrings.ErrGeneralError, ex.Message)
                    }
                );
                errorResult.SanitizedFormula = sanitizedFormula;
                return errorResult;
            }
        }

        private void EmitReturn(StringWriter tw, string indent, SqlVisitor.Context context, SqlVisitor.RetVal result, FormulaType returnType, SqlCompileOptions options)
        {
            // emit final range checks using the final type
            result.type = returnType;

            context._sbContent = new System.Text.StringBuilder();
            context.PerformRangeChecks(result, null, postCheck: true);
            tw.Write(context._sbContent);

            if (result.type is NumberType || result.type is DecimalType)
            {
                int precision;
                if (result.type is SqlIntType)
                {
                    precision = 0;
                }
                else
                {
                    precision = options.TypeHints?.Precision ?? (result.type is SqlMoneyType ? 2 : DefaultPrecision);
                }
                tw.WriteLine($"{indent}RETURN ROUND({result}, {precision})");
            }
            else
            {
                tw.WriteLine($"{indent}RETURN {result}");
            }
        }
    }

    internal static class CheckResultExtensions
    {
        private static SqlCompileInfo SqlCompilerWorker(this CheckResult check)
        {
            var binding = check.ApplyBindingInternal();

            var irResult = check.ApplyIR();
            var irNode = irResult.TopNode;
            var scopeSymbol = irResult.RuleScopeSymbol;

            var v = new SqlVisitor();
            var ctx = new SqlVisitor.Context(irNode, scopeSymbol, binding.ContextScope);
            
            // This visitor will throw exceptions on SQL errors. 
            var result = irNode.Accept(v, ctx);

            var info = new SqlCompileInfo
            {
                _retVal = result,
                _ctx = ctx
            };
            return info;
        }

        /// <summary>
        /// Run SQL compiler phase or throw on errors. 
        /// If the check result is a <see cref="SqlCompileResult"/>, then this is cached.
        /// </summary>
        /// <param name="check"></param>
        /// <returns></returns>
        /// <exception cref="SqlCompileException">Throw if we hit Power Fx restrictions that 
        /// aren't supported by the SQL backend.</exception>
        internal static SqlCompileInfo ApplySqlCompiler(this CheckResult check)
        {
            // If this is a SqlCompileResult, then we can cache it. 
            // Else, just recompute. 
            SqlCompileResult sqlCheck = check as SqlCompileResult;
            var info = sqlCheck?._sqlInfo;
            if (info != null)
            {
                // Get from cache
                return info;
            }

            info = check.SqlCompilerWorker();

            if (sqlCheck != null)
            {
                sqlCheck._sqlInfo = info;
            }
            return info;
        }
    }
}
