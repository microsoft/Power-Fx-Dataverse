﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.PowerFx.Core.Errors;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Logging;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using static Microsoft.PowerFx.Dataverse.SqlCompileOptions;
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

        public PowerFx2SqlEngine(
            EntityMetadata currentEntityMetadata = null,
            CdsEntityMetadataProvider metadataProvider = null,
            CultureInfo culture = null,
            EntityAttributeMetadataProvider entityAttributeMetadataProvider = null,
            DataverseFeatures dataverseFeatures = null)
            : base(currentEntityMetadata, metadataProvider, new PowerFxConfig(DefaultFeatures), culture, entityAttributeMetadataProvider, dataverseFeatures)
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
                    var returnType = BuildReturnType(binding.ResultType, _dataverseFeatures);

                    // SQL visitor will throw errors for SQL-specific constraints.
                    var sqlInfo = result.ApplySqlCompiler(_dataverseFeatures);
                    var res = sqlInfo._retVal;

                    var errors = new List<IDocumentError>();
                    if (!ValidateReturnType(new SqlCompileOptions(), res.Type, binding.Top.GetTextSpan(), out returnType, out var typeErrors, allowEmptyExpression: true, expression))
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
                    return new[] { new TexlError(binding.Top, DocumentErrorSeverity.Critical, TexlStrings.ErrGeneralError, ex.Message) };
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
                var sqlInfo = sqlResult.ApplySqlCompiler(_dataverseFeatures);
                var ctx = sqlInfo._ctx;
                var result = sqlInfo._retVal;
                var irNode = sqlResult.ApplyIR().TopNode;

                // if no function content generated (for scenarios which perform no logic), set the return
                if (ctx._sbContent.Length == 0)
                {
                    result = ctx.SetIntermediateVariable(irNode, fromRetVal: result);
                }

                if (!ValidateReturnType(options, result.Type, irNode.IRContext.SourceContext, out var retType, out var errors, sqlResult: sqlResult))
                {
                    var errorResult = new SqlCompileResult(errors);
                    errorResult.SanitizedFormula = sanitizedFormula;
                    return errorResult;
                }

                StringWriter tw = new StringWriter();

                var funcName = string.IsNullOrWhiteSpace(options.UdfName)
                    ? "fn_udf_" + Guid.NewGuid().ToString("n")
                    : options.UdfName;

                string verb = (options.CreateMode == SqlCompileOptions.Mode.Alter) ? "ALTER" : "CREATE";
                tw.WriteLine($"{verb} FUNCTION {funcName}(");

                // Write parameter list - these are the fields we depend on
                var parameters = ctx.GetParameters().ToList();
                for (var i = 0; i < parameters.Count(); i++)
                {
                    var del = (i == parameters.Count - 1) ? string.Empty : ",";
                    var fieldName = parameters[i].Item1.LogicalName;
                    var varName = ctx.GetVarName(fieldName, ctx.RootScope, null, allowCurrencyFieldProcessing: true);

                    // For exchange rate, DV uses scale 28 and precision 12 so maintaing parity with DV
                    string typeName = null;

                    if (fieldName.Equals("exchangerate"))
                    {
                        typeName = SqlStatementFormat.SqlExchangeRateType;
                    }
                    else
                    {
                        typeName = parameters[i].Item1.TypeCode == AttributeTypeCode.Money ? SqlStatementFormat.SqlBigType : SqlVisitor.ToSqlType(parameters[i].Item2, _dataverseFeatures);
                    }

                    tw.WriteLine($"    {varName} {typeName}{del} -- {fieldName}");
                }

                var returnType = SqlVisitor.ToSqlType(retType, _dataverseFeatures);

                // if the return type is numeric and type hint is of type integer then it is assignable, only in that
                // case use integer in UDF, actual return type of compiler will be decimal only
                if (SqlVisitor.Context.IsNumericType(retType) && options.TypeHints?.TypeHint == AttributeTypeCode.Integer)
                {
                    returnType = SqlStatementFormat.SqlIntegerType;
                }

                tw.WriteLine($") RETURNS {returnType}");

                // schemabinding only applies if there are no reference fields and formula field doesn't use any time bound functions
                var refFieldCount = ctx.GetReferenceFields().Count();
                if (refFieldCount == 0 && !ctx.ExpressionHasTimeBoundFunction)
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
                        var sqlType = field.Column.TypeCode == AttributeTypeCode.Money ? SqlStatementFormat.SqlBigType : SqlVisitor.ToSqlType(field.VarType, _dataverseFeatures);
                        tw.WriteLine($"{indent}DECLARE {field.VarName} {sqlType}");
                        string referencing = null;
                        string referenced = null;
                        if (field.Navigation != null)
                        {
                            referencing = field.Navigation.ReferencingFieldName;
                            var referencedLogical = field.Navigation.TargetFieldNames[0];
                            referenced = _metadataCache.GetColumnSchemaName(field.Navigation.TargetTableNames[0], field.Navigation.TargetFieldNames[0]);
                        }
                        else if (field.Column.RequiresReference() || field.IsReferenceFieldOnInheritedEntity)
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

                            var referencingVar = ctx.GetVarName(referencingPath, field.Scope, null, create: false, allowCurrencyFieldProcessing: true);
                            var tableSchemaName = GetTableSchemaName(field);

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
                    string tempVariableType = SqlVisitor.ToSqlType(temp.Item2, _dataverseFeatures);
                    tw.WriteLine($"{indent}DECLARE {temp.Item1} {tempVariableType}");
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
                        var selects = string.Join(",", pair.Value.Select((VarDetails field) => { return $"{field.VarName} = " + $"[{GetColumnSchemaName(field)}]"; }));
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
                    string dil = string.Empty;
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
                if (!ctx.TryUpdateOptionSetRelatedDependencies(dependentFields, _metadataCache, ref sqlResult))
                {
                    return sqlResult;
                }

                if (retType is OptionSetValueType optionSetRetType)
                {
                    _metadataCache.TryGetOptionSet(optionSetRetType.OptionSetName, out var optionSet);
                    if (optionSet != null && optionSet.OptionSetId != Guid.Empty)
                    {
                        // adding dependency for formula column to the option set returned by formula field.
                        sqlResult.OptionSetId = optionSet.OptionSetId;

                        // Removing the option set returned by option set formula field from DependentOptionSetIds,
                        // as this dependency will be handled when creating a optionset field.
                        if (sqlResult.DependentGlobalOptionSetIds.Contains(optionSet.OptionSetId))
                        {
                            sqlResult.DependentGlobalOptionSetIds.Remove(optionSet.OptionSetId);
                        }

                        if (!optionSet.IsGlobal)
                        {
                            // if optionset used by formula field is a local optionset from another field,
                            // add dependency between formula field and optionset field.
                            var key = optionSet.RelatedEntityName;

                            // Currently blocking related entity's field's local optionset to be used as result type in formula columns due to solution
                            // import challenges when related entity optionset field and formula field are getting created as part of same solution.
                            if (key != CurrentEntityName)
                            {
                                errors = new SqlCompileException(SqlCompileException.RelatedEntityOptionSetNotSupported, irNode.IRContext.SourceContext, "'" + optionSet.DisplayName + "'").GetErrors(irNode.IRContext.SourceContext);
                                var errorResult = new SqlCompileResult(errors) { SanitizedFormula = sanitizedFormula };
                                return errorResult;
                            }

                            if (!dependentFields.ContainsKey(key))
                            {
                                dependentFields[key] = new HashSet<string>();
                            }

                            dependentFields[key].Add(optionSet.RelatedColumnInvariantName);
                        }
                    }
                    else
                    {
                        errors = new SqlCompileException(SqlCompileException.InvalidOptionSet, irNode.IRContext.SourceContext, optionSetRetType.OptionSetName).GetErrors(irNode.IRContext.SourceContext);
                        var errorResult = new SqlCompileResult(errors) { SanitizedFormula = sanitizedFormula };
                        return errorResult;
                    }
                }

                // The top-level identifiers are the logical names of fields on the main entity
                sqlResult.ApplyDependencyAnalysis();
                var topLevelIdentifiers = dependentFields.ContainsKey(CurrentEntityName) ? dependentFields[CurrentEntityName] : new HashSet<string>();
                sqlResult.TopLevelIdentifiers.Clear();
                sqlResult.TopLevelIdentifiers.UnionWith(topLevelIdentifiers);

                // related identifiers are the the logical names of fields on other entities
                sqlResult.RelatedIdentifiers = dependentFields.Where(pair => pair.Key != CurrentEntityName).ToDictionary(pair => pair.Key, pair => pair.Value);

                sqlResult.DependentRelationships = ctx.GetDependentRelationships();

                sqlResult.ReturnType = retType;
                sqlResult.LogicalFormula = this.GetInvariantExpression(expression, null, _cultureInfo);
                sqlResult.SanitizedFormula = sanitizedFormula;

                sqlResult._unsupportedWarnings = ctx._unsupportedWarnings;
                return sqlResult;
            }
            catch (NotImplementedException)
            {
                var errorResult = new SqlCompileResult(new SqlCompileException(SqlCompileException.NotSupported, binding.Top.GetTextSpan()).GetErrors(binding.Top.GetTextSpan()));

                errorResult.SanitizedFormula = sanitizedFormula;
                return errorResult;
            }
            catch (Exception ex)
            {
                var errorResult = new SqlCompileResult(new[] { new TexlError(binding.Top, DocumentErrorSeverity.Critical, TexlStrings.ErrGeneralError, ex.Message) });

                errorResult.SanitizedFormula = sanitizedFormula;
                return errorResult;
            }
        }

        private string GetTableSchemaName(VarDetails field)
        {
            var tableSchemaName = _metadataCache.GetTableSchemaName(field.Table);

            //  Table Schema name returns table view and logical fields can only be referred from view.
            if (field.Column.IsLogical)
            {
                return tableSchemaName;
            }

            if (_secondaryMetadataCache != null && _secondaryMetadataCache.ShouldReferFieldFromExtensionTable(field.Table, field.Column.LogicalName, out var extensionTableName))
            {
                tableSchemaName = extensionTableName;
            }
            else
            {
                tableSchemaName = _secondaryMetadataCache != null && _secondaryMetadataCache.TryGetBaseTableName(field.Table, out var baseTableName) ?
                                    baseTableName : tableSchemaName + "Base";
            }

            return tableSchemaName;
        }

        private string GetColumnSchemaName(VarDetails field)
        {
            if (_secondaryMetadataCache == null)
            {
                return field.Column.SchemaName;
            }

            return _secondaryMetadataCache.GetColumnNameOnPrimaryTable(field);
        }

        private void EmitReturn(StringWriter tw, string indent, SqlVisitor.Context context, SqlVisitor.RetVal result, FormulaType returnType, SqlCompileOptions options)
        {
            // emit final range checks using the final type
            result.Type = returnType;

            context._sbContent = new System.Text.StringBuilder();
            context.PerformFinalRangeChecks(result, options, postCheck: true);
            tw.Write(context._sbContent);

            if (result.Type is DecimalType)
            {
                int precision = options.TypeHints?.Precision ?? DefaultPrecision;

                // In case type hint is coming as integer, internal computations are done in FX types only (number/decimal)
                // but on the way out UDF is returning integer so rounding off the value to 0 at end
                if (options.TypeHints?.TypeHint == AttributeTypeCode.Integer)
                {
                    precision = 0;
                }

                tw.WriteLine($"{indent}RETURN ROUND({result}, {precision})");
            }
            else if (result.Type is NumberType && options.TypeHints != null)
            {
                // In case of float result type, no rounding will be done if the type hint precision is not specified
                int precision = options.TypeHints.Precision;
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
        private static SqlCompileInfo SqlCompilerWorker(this CheckResult check, DataverseFeatures dataverseFeatures)
        {
            var binding = check.ApplyBindingInternal();

            var irResult = check.ApplyIR();
            var irNode = irResult.TopNode;
            var scopeSymbol = irResult.RuleScopeSymbol;

            var v = new SqlVisitor();

            SqlCompileResult sqlCheck = check as SqlCompileResult;

            var ctx = new SqlVisitor.Context(irNode, scopeSymbol, binding.ContextScope, secondaryMetadataCache: (check.Engine as PowerFx2SqlEngine)?.SecondaryMetadataCache, dataverseFeatures: dataverseFeatures);

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
        internal static SqlCompileInfo ApplySqlCompiler(this CheckResult check, DataverseFeatures dataverseFeatures)
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

            info = check.SqlCompilerWorker(dataverseFeatures);

            if (sqlCheck != null)
            {
                sqlCheck._sqlInfo = info;
            }

            return info;
        }
    }
}
