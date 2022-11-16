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

        public PowerFx2SqlEngine(
            EntityMetadata currentEntityMetadata = null,
            CdsEntityMetadataProvider metadataProvider = null,
            CultureInfo culture = null)
            : base(currentEntityMetadata, metadataProvider, new PowerFxConfig(culture), culture)
        {
        }

        public override CheckResult Check(string expression)
        {
            // For sql codegen, enable sql restrictions. 
            return CheckInternal(expression, performSqlValidations : true);
        }

        // Compile the formula to SQL that can be called from CDS. 
        // Expression: "new_CurrencyPrice * new_Quantity";
        public SqlCompileResult Compile(string expression, SqlCompileOptions options)
        {
            // don't perform SQL validations during the initial check, as they will be done as part of the compilation
            var result1 = this.CheckInternal(expression, performSqlValidations: false);

            // attempt to produce a sanitized formula, success or failure, for reporting
            var binding = result1._binding;
            var sanitizedFormula = StructuralPrint.Print(binding.Top, binding, new NameSanitizer(binding));

            if (!result1.IsSuccess)
            {
                SqlCompileResult sqlResult = new SqlCompileResult(result1);
                sqlResult.SanitizedFormula = sanitizedFormula;

                // Note if we fail due to unsupported functions. 
                sqlResult._unsupportedWarnings = new List<string>();
                foreach (var error in result1.Errors)
                {
                    if (error.MessageKey == "ErrUnknownFunction")
                    {
                        sqlResult._unsupportedWarnings.Add(error.Message);
                    }
                }
                return sqlResult;
            }

            try
            {
                var (irNode, scopeSymbol) = IRTranslator.Translate(binding);

                var v = new SqlVisitor();
                var ctx = new SqlVisitor.Context(irNode, scopeSymbol, binding.ContextScope);
                var result = irNode.Accept(v, ctx);

                // if no function content generated (for scenarios which perform no logic), set the return
                if (ctx._sbContent.Length == 0)
                {
                    result = ctx.SetIntermediateVariable(irNode, fromRetVal: result);
                }

                if (!ValidateReturnType(options, result.type, irNode.IRContext.SourceContext, out var retType, out var errors))
                {
                    var errorResult = new SqlCompileResult();
                    errorResult.SetErrors(errors);
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
                // schemabinding only applies if there are no reference fields
                var refFieldCount = ctx.GetReferenceFields().Count();
                if (refFieldCount == 0)
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

                var sqlResult = new SqlCompileResult(result1);
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
                sqlResult.TopLevelIdentifiers = dependentFields.ContainsKey(_currentEntityName) ? dependentFields[_currentEntityName] : new HashSet<string>();

                // related identifiers are the the logical names of fields on other entities
                sqlResult.RelatedIdentifiers = dependentFields.Where(pair => pair.Key != _currentEntityName).ToDictionary(pair => pair.Key, pair => pair.Value);

                sqlResult.DependentRelationships = ctx.GetDependentRelationships();

                sqlResult.ReturnType = retType;
                sqlResult.LogicalFormula = ConvertExpression(expression, toDisplay: false);
                sqlResult.SanitizedFormula = sanitizedFormula;

                sqlResult._unsupportedWarnings = ctx._unsupportedWarnings;
                return sqlResult;
            }
            catch (SqlCompileException sqlEx)
            {
                var errorResult = new SqlCompileResult();
                errorResult.SetErrors(sqlEx.GetErrors(binding.Top.GetTextSpan()));
                errorResult.SanitizedFormula = sanitizedFormula;
                errorResult._unsupportedWarnings = new List<string>
                {
                    sqlEx.Message
                };
                return errorResult;
            }
            catch (NotImplementedException)
            {
                var errorResult = new SqlCompileResult();
                errorResult.SetErrors(
                    new SqlCompileException(SqlCompileException.NotSupported, binding.Top.GetTextSpan()).GetErrors(binding.Top.GetTextSpan())
                );
                errorResult.SanitizedFormula = sanitizedFormula;
                return errorResult;
            }
            catch (Exception ex)
            {
                var errorResult = new SqlCompileResult();
                errorResult.SetErrors(
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

            if (result.type is NumberType)
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
}
