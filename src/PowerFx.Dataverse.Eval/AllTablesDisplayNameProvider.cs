//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Utils;

namespace Microsoft.PowerFx.Dataverse
{
    // Display name provider for all tables in an org.
    internal class AllTablesDisplayNameProvider : DisplayNameProvider
    {
        private readonly Dictionary<string, string> _display2logical = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _logical2Display = new Dictionary<string, string>();

        public override IEnumerable<KeyValuePair<DName, DName>> LogicalToDisplayPairs
        {
            get
            {
                foreach (var kv in _logical2Display)
                {
                    yield return new KeyValuePair<DName, DName>(new DName(kv.Key), new DName(kv.Value));
                }
            }
        }

        public void Add(string logical, string display)
        {
            _logical2Display[logical] = display;
            _display2logical[display] = logical;
        }

        public bool TryGetLogicalName(string display, out string logical)
        {
            return _display2logical.TryGetValue(display, out logical);
        }

        public bool TryGetDisplayName(string logical, out string display)
        {
            return _logical2Display.TryGetValue(logical, out display);
        }

        public override bool TryGetLogicalName(DName displayName, out DName logicalName)
        {
            if (TryGetLogicalName(displayName.Value, out var logical))
            {
                logicalName = new DName(logical);
                return true;
            }
            return false;
        }

        public override bool TryGetDisplayName(DName logicalName, out DName displayName)
        {
            if (TryGetDisplayName(logicalName.Value, out var display))
            {
                displayName = new DName(display);
                return true;
            }
            return false;
        }
    }
}
