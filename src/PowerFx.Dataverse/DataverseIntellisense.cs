//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Core.Binding;
using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.Texl.Intellisense;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.Intellisense.IntellisenseData;
using Microsoft.PowerFx.Syntax;
using System.Collections.Generic;

namespace Microsoft.PowerFx.Dataverse
{
    internal class DataverseIntellisense : Microsoft.PowerFx.Intellisense.Intellisense
    {
        private readonly CdsEntityMetadataProvider _provider;

        internal DataverseIntellisense(PowerFxConfig config, CdsEntityMetadataProvider provider) : base(config, config.EnumStore, IntellisenseProvider.SuggestionHandlers)
        {
            _provider = provider;
        }

        protected internal override IntellisenseData CreateData(IIntellisenseContext context, DType expectedType, TexlBinding binding, TexlFunction curFunc, TexlNode curNode, int argIndex, int argCount, IsValidSuggestion isValidSuggestionFunc, IList<DType> missingTypes, List<CommentToken> comments)
        {
            return new DataverseIntellisenseData(context, _config, expectedType, binding, curFunc, curNode, argIndex, argCount, isValidSuggestionFunc, missingTypes, comments, _provider);
        }
    }
}
