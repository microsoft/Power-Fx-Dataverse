//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Core.Binding;
using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Core.Texl.Intellisense;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Types.Enums;
using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.Intellisense.IntellisenseData;
using Microsoft.PowerFx.Syntax;
using Microsoft.Xrm.Sdk.Metadata;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.PowerFx.Dataverse
{
    internal class DataverseIntellisenseData : IntellisenseData
    {
        /// <summary>
        /// Not all of the keywords provided by <see cref="TexlLexer"/> are valid dataverse identifiers, and
        /// should therefore be excluded from being suggested
        /// </summary>
        private static readonly List<string> _restrictedKeywords = new List<string>() { TexlLexer.KeywordSelf };

        /// <summary>
        /// Not all of the enums provided by <see cref="EnumSymbols"/> have meaning in dataverse and
        /// should therefore be excluded from being suggested
        /// Note that ErrorKind is supported by the infra, but not in maker scenarios, so should be excluded
        /// </summary>
        private readonly static string[] _supportedEnums = new [] { Microsoft.PowerFx.Core.Utils.LanguageConstants.TimeUnitEnumString };

        /// <summary>
        /// Not all functions supported by the name resolver should be suggested for makers
        /// </summary>
        private readonly static TexlFunction[] _excludedFunctions = new[]
        {
            BuiltinFunctionsCore.Error,
            BuiltinFunctionsCore.Today,
            BuiltinFunctionsCore.IsToday
        };

        /// <summary>
        /// The metadata provider for getting backing CDS entity metadata and option set information
        /// </summary>
        private CdsEntityMetadataProvider _provider;

        public DataverseIntellisenseData(IIntellisenseContext context, PowerFxConfig config, DType expectedType, TexlBinding binding, TexlFunction curFunc, TexlNode curNode, int argIndex, int argCount, IsValidSuggestion isValidSuggestionFunc, IList<DType> missingTypes, List<CommentToken> comments, CdsEntityMetadataProvider provider)
            : base(config, config.EnumStore, context, expectedType, binding, curFunc, curNode, argIndex, argCount, isValidSuggestionFunc, missingTypes, comments)
        {
            _provider = provider;
        }

        internal override IEnumerable<EnumSymbol> EnumSymbols => base.EnumSymbols.Where(symbol => _supportedEnums.Contains(symbol.EntityName) );

        public override void AddSuggestionsForConstantKeywords() =>
            IntellisenseHelper.AddSuggestionsForMatches(
                this,
                TexlLexer.GetConstantKeywords(false).Where(keyword => !_restrictedKeywords.Contains(keyword)),
                SuggestionKind.KeyWord,
                SuggestionIconKind.Other,
                requiresSuggestionEscaping: false);

        /// <summary>
        /// Determine whether to add a column purely based on its DType
        /// </summary>
        /// <param name="type">The DType of the column</param>
        /// <returns>Whether the column should be removed from suggestions</returns>
        internal override bool TryAddCustomColumnTypeSuggestions(DType type)
        {
            // filter out data entity fields that are not lookups (e.g. N-1 relationships)
            return type.Kind == DKind.DataEntity && type.CdsColumnDefinition(type.ExpandInfo.Name).TypeCode != Xrm.Sdk.Metadata.AttributeTypeCode.Lookup;
        }

        internal override bool ShouldSuggestFunction(TexlFunction function)
        {
            return !_excludedFunctions.Contains(function);
        }

        internal override void AddCustomSuggestionsForGlobals() =>
            IntellisenseHelper.AddSuggestionsForMatches(
                this,
                _provider.OptionSets.Select(option => TexlLexer.EscapeName(option.DisplayName)).Distinct(),
                SuggestionKind.Global,
                SuggestionIconKind.Other,
                requiresSuggestionEscaping: false);

        /// <summary>
        /// Determine whether to include a suggestion based on the type and suggestion text
        /// </summary>
        /// <param name="suggestion">Candidate suggestion string</param>
        /// <param name="type">Type of the node at the caller's context</param>
        /// <returns>Whether the provided candidate suggestion is valid per the provided type.</returns>
        internal override bool DetermineSuggestibility(string suggestion, DType type)
        {
            if (type.Kind != DKind.DataEntity && DType.TryGetLogicalNameForColumn(type, TexlLexer.UnescapeName(suggestion), out var logicalName))
            {
                var table = type.CdsTableDefinitionOrDefault();
                if (table != default)
                {
                    // if the DType is linked to CDS data source, filter out suggestions on virtual tables
                    if (table.IsVirtual)
                    {
                        return false;
                    }

                    // attempt to get the attribute type to filter out attributes that are not supported
                    var column = table.CdsColumnDefinitionOrDefault(logicalName);
                    if (column != default)
                    {
                        var attributeType = column.TypeCode;
                        switch (attributeType)
                        {
                            case AttributeTypeCode.Double:
                                return false;
                            case AttributeTypeCode.Integer:
                                return column.FormatName == IntegerFormat.None.ToString();
                            case AttributeTypeCode.String:
                                return column.FormatName == StringFormat.Text.ToString();
                            case AttributeTypeCode.Virtual:
                                // multi-select are suggested, but files and images are not
                                return column.IsOptionSet;
                            default:
                                return true;
                        }
                    }
                }
            }
            return base.DetermineSuggestibility(suggestion, type);
        }
    }
}
