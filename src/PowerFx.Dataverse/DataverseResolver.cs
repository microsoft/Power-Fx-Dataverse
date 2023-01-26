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
using System.Collections.Generic;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Resolver for Dataverse bindings. 
    /// </summary>
    class DataverseResolver : ComposedReadOnlySymbolTable
    {
        private CdsEntityMetadataProvider _provider;

        public DataverseResolver(CdsEntityMetadataProvider provider, ReadOnlySymbolTable functions)
            : base(functions)
        {            
            _provider = provider;
        }

        public override bool Lookup(DName name, out NameLookupInfo nameInfo, NameLookupPreferences preferences = NameLookupPreferences.None)
        {
            if (_provider.TryGetOptionSet(name, out var optionSet))
            {
                nameInfo = new NameLookupInfo(BindKind.OptionSet, DType.CreateOptionSetType(optionSet), DPath.Root, 0, optionSet, new DName(optionSet.DisplayName));
                return true;
            }

            return base.Lookup(name, out nameInfo, preferences);
        }

        public override bool LookupGlobalEntity(DName name, out NameLookupInfo lookupInfo)
        {
            if (_provider.TryGetDataSource(name, out var dataSource))
            {
                lookupInfo = new NameLookupInfo(BindKind.Data, dataSource.Schema, DPath.Root, 0, dataSource);
                return true;
            }

            return base.LookupGlobalEntity(name, out lookupInfo);
        }

    }
}
