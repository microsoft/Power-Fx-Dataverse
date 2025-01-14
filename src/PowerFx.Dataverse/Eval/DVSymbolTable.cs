// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core;
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
    public class DVSymbolTable : SymbolTable, IGlobalSymbolNameResolver
    {
        public const string SymTableName = "DataverseGlobals";

        protected readonly CdsEntityMetadataProvider _metadataCache;

        internal const string SingleColumnTableFieldName = "Value";

        IEnumerable<KeyValuePair<string, NameLookupInfo>> IGlobalSymbolNameResolver.GlobalSymbols
        {
            get
            {
                // Below does same as the base class.
                foreach (var variable in _variables)
                {
                    yield return variable;
                }

                // Below is for option sets.
                var options = _metadataCache.OptionSets;
                foreach (var option in options)
                {
                    if (this.TryLookup(option.EntityName, out var optionNameInfo))
                    {
                        yield return new KeyValuePair<string, NameLookupInfo>(option.DisplayName, optionNameInfo);
                    }
                    else
                    {
                        // Lookup should never have failed.
                        throw new InvalidOperationException();
                    }
                }
            }
        }

        // Loaded tables are tracked in the base class's AddVariable.

        public DVSymbolTable(CdsEntityMetadataProvider metadataProvider)
        {
            _metadataCache = metadataProvider ?? new CdsEntityMetadataProvider(null);
            DebugName = SymTableName;
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
