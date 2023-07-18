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
    public class DataverseEngine : Engine
    {
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
          CultureInfo culture = null,
          bool numberIsFloat = false)
            : base(config)
        {
            var xrmEntity = currentEntityMetadata ?? Empty();

            // if no provider is given, create a standalone provider to convert the metadata that will not support references
            _metadataCache = metadataProvider ?? new CdsEntityMetadataProvider(null) { NumberIsFloat = numberIsFloat };

            _currentDataSource = _metadataCache.FromXrm(xrmEntity);

            this.SupportedFunctions = ReadOnlySymbolTable.NewDefault(Library.FunctionList);
            _cultureInfo = culture ?? CultureInfo.InvariantCulture;
        }

        #region Critical Virtuals

        // https://github.com/microsoft/Power-Fx-Dataverse/issues/117
        // 
        public const bool NumberIsFloat = true;

        public override ParserOptions GetDefaultParserOptionsCopy()
        {
            return new ParserOptions
            {
                 Culture = _cultureInfo,
                 MaxExpressionLength =  MaxExpressionLength,
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
            var functionList = ReadOnlySymbolTable.Compose(Config.SymbolTable, this.SupportedFunctions);
            var resolver = new DataverseResolver(_metadataCache, functionList);
            return resolver;
        }

#endregion

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
                type is DecimalType ||
                type is BooleanType ||
                type is StringType ||
                Library.IsDateTimeType(type);
        }

        internal static FormulaType BuildReturnType(DType type)
        {
            if (type.Kind == DKind.Currency)
            {
                // Currency isn't supported yet, for now, return decimal
                return FormulaType.Decimal;
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
