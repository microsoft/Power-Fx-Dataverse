//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Core.Binding;
using Microsoft.PowerFx.Core.Binding.BindInfo;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Hook into symbol table to support Dataverse OptionSets which are populated 
    /// lazily from the metadata cache. 
    /// </summary>
    public class DVSymbolTable : SymbolTable
    {
        private readonly CdsEntityMetadataProvider _metadataCache;

        // Loaded tables are tracked in the base class's AddVariable.

        public DVSymbolTable(CdsEntityMetadataProvider metadataProvider)
        {
            _metadataCache = metadataProvider ?? new CdsEntityMetadataProvider(null);
        }

        // This requires internal types in PowerFx, so it needs to be in PowerFx.Dataverse
        // (which has InternalsVisibleTo) and not in Dataverse.Eval.
        // Hook to get symbols directly from Dataverse MetadataCache. 
        // Especially for OptionSets. 
        internal override bool TryLookup(DName name, out NameLookupInfo nameInfo)
        {
            if (_metadataCache.TryGetOptionSet(name, out var optionSet))
            {
                nameInfo = new NameLookupInfo(BindKind.OptionSet, DType.CreateOptionSetType(optionSet), DPath.Root, 0, optionSet, new DName(optionSet.DisplayName));
                return true;
            }

            // Caller will still find Tables that were added via AddVariable.
            nameInfo = default;
            return false;
        }
    }
}
