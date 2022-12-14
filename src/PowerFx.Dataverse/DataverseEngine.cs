//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.AppMagic.Authoring.Importers.DataDescription;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Binding;
using Microsoft.PowerFx.Core.Errors;
using Microsoft.PowerFx.Core.Glue;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Dataverse.Functions;
using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;


namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Power Fx engine to allows binding to Dataverse <see cref="Entity"/>.
    /// </summary>
    public class DataverseEngine : IPowerFxScope
    {
        public PowerFxConfig Config { get; }

        private static readonly BindingConfig DataverseBindingConfig = new BindingConfig(useThisRecordForRuleScope: true);

        // The current entity that expressions are compiled against. 
        private readonly DataverseDataSourceInfo _currentDataSource;

        private CdsTableDefinition _currentEntity => _currentDataSource.CdsTableDefinition;

        protected string _currentEntityName => _currentEntity.Name;

        // Callback object for getting metadata for other entities, such as with relationships. 
        protected readonly CdsEntityMetadataProvider _metadataCache;

        protected readonly CultureInfo _cultureInfo;

        // the max supported expression length
        internal const int MaxExpressionLength = 1000;

        // $$$ - remove culture parameter and just get it from the config. 
        public DataverseEngine(
          EntityMetadata currentEntityMetadata,
          CdsEntityMetadataProvider metadataProvider,
          PowerFxConfig config,
          CultureInfo culture = null, string foo = "ll")
        {
            var xrmEntity = currentEntityMetadata ?? Empty();

            // if no provider is given, create a standalone provider to convert the metadata that will not support references
            _metadataCache = metadataProvider ?? new CdsEntityMetadataProvider(null);

            _currentDataSource = _metadataCache.FromXrm(xrmEntity);

            Config = config;
            Config.SetCoreFunctions(Library.FunctionList);

            _cultureInfo = culture ?? CultureInfo.InvariantCulture;
        }

        #region Intellisense / Language Server Protocol Support (LSP)
        public virtual CheckResult Check(string expression)
        {
            var result =  CheckInternal(expression, false);
            return result;
        }

        // performSqlValidations - true if this is sql codegen and we need to enforce sql restrictions. 
        //  false if this does not have sql codegen (such as run via interpreter) and should produce any legal IR tree. 
        protected CheckResult CheckInternal(string expression, bool performSqlValidations, bool useInvariantLocale = false)
        {
            if (expression == null) { throw new ArgumentNullException(nameof(expression)); }

            // use the appropriate language for checking, the user's for checking input, or invariant for loading the logical formula
            var languageSettings = useInvariantLocale ? CultureInfo.InvariantCulture : _cultureInfo;

            var parseOptions = new ParserOptions
            {
                Culture = languageSettings
            };
            var parseResult = parseOptions.Parse(expression);

            var functionList = Config.SymbolTable.Functions.ToArray();
            var resolver = new DataverseResolver(_metadataCache, functionList);

            var binding = TexlBinding.Run(
                new Glue2DocumentBinderGlue(),
                parseResult.Root,
                resolver,
                DataverseBindingConfig, 
                _currentDataSource.Schema.ToRecord());

            var result = new CheckResult(parseResult, binding);

            if (expression.Length > MaxExpressionLength)
            {
                result.SetErrors(new SqlCompileException(SqlCompileException.FormulaTooLong, binding.Top.GetTextSpan()).GetErrors(binding.Top.GetTextSpan()));
            }
            else if (result.IsSuccess)
            {
                try
                {
                    var returnType = BuildReturnType(binding.ResultType);

                    if (performSqlValidations)
                    {
                        var (irNode, scopeSymbol) = IRTranslator.Translate(binding);

                        var v = new SqlVisitor();
                        var ctx = new SqlVisitor.Context(irNode, scopeSymbol, binding.ContextScope, checkOnly: true);
                        var res = irNode.Accept(v, ctx);

                        var errors = new List<IDocumentError>();
                        if (!ValidateReturnType(new SqlCompileOptions(), res.type, binding.Top.GetTextSpan(), out returnType, out var typeErrors, allowEmptyExpression: true, expression))
                        {
                            errors.AddRange(typeErrors);
                        }

                        if (errors.Count > 0)
                        {
                            result.SetErrors(errors);
                        }
                    }

                    if (result.IsSuccess)
                    {
                        result.ReturnType = returnType;
                    }
                }
                catch (SqlCompileException ex)
                {
                    // append the entire span for any errors with an unknown span
                    result.SetErrors(ex.GetErrors(binding.Top.GetTextSpan()));
                }
                catch (NotImplementedException)
                {
                    result.SetErrors(
                        new SqlCompileException(SqlCompileException.NotSupported, binding.Top.GetTextSpan()).GetErrors(binding.Top.GetTextSpan())
                    );
                }
                catch (Exception ex)
                {
                    result.SetErrors(
                        new[] {
                        new TexlError(binding.Top, DocumentErrorSeverity.Critical, TexlStrings.ErrGeneralError, ex.Message)
                        }
                    );
                }
            }

            return result;
        }

        public IIntellisenseResult Suggest(string expression, int cursorPosition)
        {
            // don't perform SQL validations while identifying suggestions for the maker
            var result1 = this.CheckInternal(expression, performSqlValidations: false);
            var binding = result1._binding;
            var formula = new Formula(expression, null);
            formula.ApplyParse(result1.Parse);

            var context = new IntellisenseContext(expression, cursorPosition);
            var intellisense = new DataverseIntellisense(Config, _metadataCache);
            return intellisense.Suggest(context, binding, formula);
        }

        #endregion

        /// <summary>
        /// Translate an expression with invariant (logical) names to display names in the user's locale
        /// </summary>
        /// <param name="expression">The expression</param>
        /// <returns></returns>
        public string ConvertToDisplay(string expression)
        {
            return ConvertExpression(expression, toDisplay: true);
        }

        /// <summary>
        /// Conversion helper for Dataverse expressions.
        /// </summary>
        /// <param name="expression">The expression to convert.</param>
        /// <param name="toDisplay">True if converting logical -> display, false if display -> logical.</param>
        /// <returns>Expression in expected display or logical format.</returns>
        internal string ConvertExpression(string expression, bool toDisplay)
        {
            var resolver = new DataverseResolver(_metadataCache, Config.SymbolTable.Functions.ToArray());
            
            // We need to build the context type (used for ThisRecord scope) to pass to the expression formula helper 
            var currentDsType = FormulaType.Build(_currentDataSource.Schema.ToRecord());
            if (!(currentDsType is RecordType context))
            {
                return expression;
            }
            return ExpressionLocalizationHelper.ConvertExpression(expression, context, DataverseBindingConfig, resolver, new Glue2DocumentBinderGlue(), _cultureInfo, toDisplay: toDisplay);
        }

        // Helper to get an empty entity for cases where there's no metadata dependency. 
        public static EntityMetadata Empty()
        {
            var name = "placeholder";
            var attribute = new UniqueIdentifierAttributeMetadata
            {
                LogicalName = name,
                SchemaName = name,
                DisplayName = new Label(new LocalizedLabel(name, 1033), new LocalizedLabel[0])
            };
            // use reflection to set read-only properties on the entity
            Type attrType = typeof(UniqueIdentifierAttributeMetadata);
            attrType.GetProperty("IsValidForRead").SetValue(attribute, true);

            var entity = new EntityMetadata
            {
                LogicalName = name,
                EntitySetName = name,
                SchemaName = name,
                DisplayCollectionName = new Label(new LocalizedLabel(name, 1033), new LocalizedLabel[0]),
            };

            // use reflection to set read-only properties on the entity
            Type entityType = typeof(EntityMetadata);
            entityType.GetProperty("PrimaryNameAttribute").SetValue(entity, name);
            entityType.GetProperty("PrimaryIdAttribute").SetValue(entity, name);
            entityType.GetProperty("Attributes").SetValue(entity, new AttributeMetadata[] { attribute });

            return entity;
        }

        private protected bool ValidateReturnType(SqlCompileOptions options, FormulaType nodeType, Span sourceContext, out FormulaType returnType, out IEnumerable<IDocumentError> errors, bool allowEmptyExpression = false, string expression = null)
        {
            errors = null;
            returnType = BuildReturnType(nodeType);

            if (!SupportedReturnType(returnType) && !(allowEmptyExpression && returnType is BlankType && String.IsNullOrWhiteSpace(expression)))
            {
                errors = new SqlCompileException(SqlCompileException.ResultTypeNotSupported, sourceContext, returnType._type.GetKindString()).GetErrors(sourceContext);
                return false;
            }
            if (options.TypeHints?.TypeHint != null)
            {
                var hintType = options.TypeHints.TypeHint.FormulaType();
                if (returnType is NumberType)
                {
                    // TODO: better type validation
                    if (hintType is NumberType)
                    {
                        returnType = hintType;
                        return true;
                    }
                }
                else if (returnType == hintType)
                {
                    return true;
                }

                errors = new SqlCompileException(SqlCompileException.ResultTypeMustMatch, sourceContext, options.TypeHints.TypeHint, returnType._type.GetKindString()).GetErrors(sourceContext);
                return false;
            }

            return true;
        }

        internal static bool SupportedReturnType(FormulaType type)
        {
            return
                type is SqlDecimalType ||
                type is BooleanType ||
                type is StringType ||
                Library.IsDateTimeType(type);
        }

        internal static FormulaType BuildReturnType(DType type)
        {
            if (type.Kind == DKind.Number)
            {
                // The default numeric type is decimal
                return new SqlDecimalType();
            }
            else if (type.Kind == DKind.Currency)
            {
                // Currency isn't supported yet, for now, return decimal
                return new SqlDecimalType();
            }
            else
            {
                try
                {
                    var fxType = FormulaType.Build(type);
                    if (fxType == FormulaType.Unknown)
                    {
                        throw new NotImplementedException();
                    }
                    return fxType;
                }
                catch (NotImplementedException)
                {
                    // if the return type is not supported, report it as a failure
                    throw new SqlCompileException(SqlCompileException.ResultTypeNotSupported, null, type.GetKindString());
                }
            }
        }

        internal static FormulaType BuildReturnType(FormulaType type)
        {
            // if the type is TZI pass it along
            if (type == FormulaType.DateTimeNoTimeZone)
            {
                return type;
            }

            // otherwise, use the internal DType to produce the final return type
            return BuildReturnType(type._type);
        }
    }
}