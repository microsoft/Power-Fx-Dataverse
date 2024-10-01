// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse.DataSource
{
    internal static class DelegationCapabilitiesUtils
    {
        public static bool DoesColumnSupportStartsEndsWith(this IDelegationMetadata ds, string col, FormulaType colType, bool isStartsWith)
        {
            Contracts.AssertValue(col);
            Contracts.AssertValue(ds);

            if (colType._type != DType.String)
            {
                return false;
            }

            var metadata = ds.FilterDelegationMetadata;
            if (metadata == null)
            {
                return false;
            }

            var capabilityToCheck = isStartsWith ? DelegationCapability.StartsWith : DelegationCapability.EndsWith;
            
            return metadata.IsDelegationSupportedByColumn(DPath.Root.Append(new DName(col)), DelegationCapability.Filter | capabilityToCheck);
        }
    }
}
