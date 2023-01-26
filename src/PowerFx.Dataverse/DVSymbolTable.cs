//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Binding;
using Microsoft.PowerFx.Core.Binding.BindInfo;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using System;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Hook into symbol table to support Dataverse OptionSets which are populated 
    /// lazily from the metadata cache. 
    /// </summary>
    public class DVSymbolTable : SymbolTable
    {
        protected readonly CdsEntityMetadataProvider _metadataCache;

        // Loaded tables are tracked in the base class's AddVariable.

        public DVSymbolTable(CdsEntityMetadataProvider metadataProvider)
        {
            _metadataCache = metadataProvider ?? new CdsEntityMetadataProvider(null);
            DebugName = "DataverseGlobals";
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

    // Lazily load anything. 
    // See DVSymbolTable
    // See https://github.com/microsoft/Power-Fx/issues/1017
    public class DVLazySymbolTable : DVSymbolTable
    {
        // All possible tables we could add. 
        private readonly DisplayNameProvider _displayNameLookup;

        // Add (Logical,Display) name. 
        private readonly Action<string, string> _funcAdd;

        // Suppress VersionHash - we're logically fixed (to display names at startup). 
        private static readonly VersionHash _constant = VersionHash.New();
        internal override VersionHash VersionHash => _constant;

        public DVLazySymbolTable(
            CdsEntityMetadataProvider provider,
            DisplayNameProvider displayNameLookup,
            Action<string, string> funcAdd)
            : base(provider)
        {
            _displayNameLookup = displayNameLookup ?? throw new ArgumentNullException(nameof(displayNameLookup));
            _funcAdd = funcAdd ?? throw new ArgumentNullException(nameof(funcAdd));

            DebugName = "DataverseLazyGlobals";
        }

        internal override bool TryLookup(DName name, out NameLookupInfo nameInfo)
        {
            if (_displayNameLookup.TryGetLogicalName(name, out var logicalName))
            {
                // This callback will add the symbol, so that when we return, our caller will naturally find it. 
                // But that add will inc the VersionHash, so we override to disable it. 
                _funcAdd(logicalName.Value, name.Value);
            } 
            else if (_displayNameLookup.TryGetDisplayName(name, out var displayName))
            {
                _funcAdd(name.Value, displayName.Value);
            }

            return base.TryLookup(name, out nameInfo);
        }        
    }
}
