﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AppMagic.Authoring.Importers.DataDescription;
using Microsoft.PowerFx.Core.Binding;
using Microsoft.PowerFx.Core.Errors;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Dataverse.Functions;
using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using BuiltinFunctionsCore = Microsoft.PowerFx.Core.Texl.BuiltinFunctionsCore;
using TexlFunction = Microsoft.PowerFx.Core.Functions.TexlFunction;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Power Fx engine to allows binding to Dataverse <see cref="Entity"/>.
    /// </summary>
    public class DataverseEngine : Engine
    {
        // The current entity that expressions are compiled against.
        private readonly DataverseDataSourceInfo _currentDataSource;

        private CdsTableDefinition CurrentEntity => _currentDataSource.CdsTableDefinition;

        protected string CurrentEntityName => CurrentEntity.Name;

#pragma warning disable SA1300 // Elements should begin with uppercase letter
        [Obsolete("Use CurrentEntityName instead.")]
        protected string _currentEntityName => CurrentEntity.Name;
#pragma warning restore SA1300 // Elements should begin with uppercase letter

        // Callback object for getting metadata for other entities, such as with relationships.
        protected readonly CdsEntityMetadataProvider _metadataCache;

        // Callback object for getting additional metadata which is not present in xrmentitymetadata like basetablename, isstoredonprimarytable, etc for entities.
        protected readonly EntityAttributeMetadataProvider _secondaryMetadataCache;

        protected readonly DataverseFeatures _dataverseFeatures;

        internal EntityAttributeMetadataProvider SecondaryMetadataCache => _secondaryMetadataCache;

        protected readonly CultureInfo _cultureInfo;

        // the max supported expression length
        internal const int MaxExpressionLength = 1000;

        // During solution import or during validation checks, we pass invariant expression which is stored in db, with cultureInfo as null. This Invariant
        // expression length could be sometimes greater than display expression length and allowed MaxExpressionLength. so, compiling formula will fail for
        // this invariant expression, which had once passed when compiled as display expression. So for invariant expressions, setting extra 500 buffer.
        internal const int MaxInvariantExpressionLength = 1500;

        internal static readonly TexlFunction[] FloatingPointFunctions = new[]
        {
            BuiltinFunctionsCore.Exp,
            BuiltinFunctionsCore.Float,
            BuiltinFunctionsCore.Power,
            BuiltinFunctionsCore.Sqrt,
            BuiltinFunctionsCore.Ln
        };

        // $$$ - remove culture parameter and just get it from the config.
        public DataverseEngine(
          EntityMetadata currentEntityMetadata,
          CdsEntityMetadataProvider metadataProvider,
          PowerFxConfig config,
          CultureInfo culture = null,
          EntityAttributeMetadataProvider entityAttributeMetadataProvider = null,
          DataverseFeatures dataverseFeatures = null)
            : base(config)
        {
            var xrmEntity = currentEntityMetadata ?? Empty();

            // if no provider is given, create a standalone provider to convert the metadata that will not support references
            _metadataCache = metadataProvider ?? new CdsEntityMetadataProvider(null) { NumberIsFloat = NumberIsFloat };

            _secondaryMetadataCache = entityAttributeMetadataProvider;

            _currentDataSource = _metadataCache.FromXrm(xrmEntity);

            this.SupportedFunctions = ReadOnlySymbolTable.NewDefault(Library.FunctionList);
            _cultureInfo = culture ?? CultureInfo.InvariantCulture;

            _dataverseFeatures = dataverseFeatures ?? new DataverseFeatures()
            {
                IsFloatingPointEnabled = false,
                IsOptionSetEnabled = false,
                UseMaxInvariantExpressionLength = false,
                UseLookupFieldNameWhenNavPropNameIsDiff = false
            };

            var functions = Library.FunctionList.ToList();

            // If Floating Point Feature is disabled then don't recommend Floating Point functions on Intellisense
            // but internal support for Float function would be there for IR nodes as we are not removing these functions from static library list
            if (!_dataverseFeatures.IsFloatingPointEnabled)
            {
                foreach (TexlFunction function in FloatingPointFunctions)
                {
                    if (functions.IndexOf(function) != -1)
                    {
                        functions.Remove(function);
                    }
                }
            }

            this.SupportedFunctions = ReadOnlySymbolTable.NewDefault(functions);
        }

        #region Critical Virtuals

        public const bool NumberIsFloat = false;

        public override ParserOptions GetDefaultParserOptionsCopy()
        {
            return new ParserOptions
            {
                Culture = _cultureInfo,
                MaxExpressionLength = (_dataverseFeatures.UseMaxInvariantExpressionLength && _cultureInfo == CultureInfo.InvariantCulture)
                                      ? MaxInvariantExpressionLength : MaxExpressionLength,
                NumberIsFloat = NumberIsFloat
            };
        }

        // RuleScope is like a default set of symbols.
        // Also implies that "ThisRecord." is available to Check's BindingContext.
        private protected override RecordType GetRuleScope()
        {
            return (RecordType)FormulaType.Build(_currentDataSource.Schema.ToRecord());
        }

        // Provide dataverse-specific intellisense
        private protected override IIntellisense CreateIntellisense()
        {
            var intellisense = new DataverseIntellisense(Config, _metadataCache);
            return intellisense;
        }

        // Critical to wire in DataverseResolver resolver.
        [Obsolete("Rework with SymbolTables")]
        private protected override INameResolver CreateResolver()
        {
            // This hook requires we manually include other symbols:
            // - Config.SymbolTable includes custom added functions (for interpreted case)
            // - this.SupportedFunctions is the functions builtin for SQL engine.
            var functionList = ReadOnlySymbolTable.Compose(Config.InternalConfigSymbols, this.SupportedFunctions);
            var resolver = new DataverseResolver(_metadataCache, functionList);
            return resolver;
        }

        #endregion Critical Virtuals

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

        private protected bool ValidateReturnType(SqlCompileOptions options, FormulaType nodeType, Span sourceContext, out FormulaType returnType, out IEnumerable<IDocumentError> errors, bool allowEmptyExpression = false, string expression = null, SqlCompileResult sqlResult = null)
        {
            errors = null;

            // currency Formula Type is not supported so casting it to decimal, however currency fields can be used in formula expression
            // so in that case, we need to return decimal
            if (nodeType._type.Kind == DKind.Currency)
            {
                returnType = BuildReturnType(FormulaType.Decimal, _dataverseFeatures);
            }
            else
            {
                // if Floating Point Feature is disabled this method will never return Number but it will return decimal in its place
                returnType = BuildReturnType(nodeType, _dataverseFeatures);
            }

            if (!SupportedReturnType(returnType) && !(allowEmptyExpression && returnType is BlankType && string.IsNullOrWhiteSpace(expression)))
            {
                errors = new SqlCompileException(SqlCompileException.ResultTypeNotSupported, sourceContext, returnType._type.GetKindString()).GetErrors(sourceContext);
                return false;
            }

            if (options.TypeHints?.TypeHint != null)
            {
                var hintType = options.TypeHints.TypeHint.FormulaType();

                if (returnType is DecimalType)
                {
                    if (SqlVisitor.Context.IsNumericType(hintType))
                    {
                        returnType = hintType;

                        if (sqlResult != null)
                        {
                            sqlResult.IsHintApplied = true;
                        }

                        return true;
                    }
                }
                else if (returnType == hintType)
                {
                    if (sqlResult != null)
                    {
                        sqlResult.IsHintApplied = true;
                    }

                    return true;
                }

                var displayType = returnType._type.GetKindString() == FormulaType.Number.ToString() ? SqlStatementFormat.Float : returnType._type.GetKindString();
                errors = new SqlCompileException(SqlCompileException.ResultTypeMustMatch, sourceContext, options.TypeHints.TypeHint, displayType).GetErrors(sourceContext);
                return false;
            }

            return true;
        }

        internal bool SupportedReturnType(FormulaType type)
        {
            return
                type is DecimalType ||
                type is BooleanType ||
                type is StringType ||
                Library.IsDateTimeType(type) ||
                (_dataverseFeatures.IsOptionSetEnabled && type is OptionSetValueType) ||
                (_dataverseFeatures.IsFloatingPointEnabled && type is NumberType); // Number is only supported if floating point is enabled
        }

        internal static FormulaType BuildReturnType(DType type, DataverseFeatures dataverseFeatures)
        {
            // if floating point feature is disabled then run on legacy functionality ie mapping number to decimal
            if (!dataverseFeatures.IsFloatingPointEnabled && type.Kind == DKind.Number)
            {
                return FormulaType.Decimal;
            }

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

        internal static FormulaType BuildReturnType(FormulaType type, DataverseFeatures dataverseFeatures)
        {
            // if the type is TZI pass it along
            if (type == FormulaType.DateTimeNoTimeZone)
            {
                return type;
            }

            // otherwise, use the internal DType to produce the final return type
            return BuildReturnType(type._type, dataverseFeatures);
        }
    }
}
