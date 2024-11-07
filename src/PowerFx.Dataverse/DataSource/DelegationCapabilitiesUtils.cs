// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.Contracts;
using System.Text;
using Microsoft;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse;
using Microsoft.PowerFx.Dataverse.DataSource;
using Microsoft.PowerFx.Dataverse.Eval;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse.DataSource
{
    internal static class DelegationCapabilitiesUtils
    {
        public static bool DoesColumnSupportStartsEndsWith(this IDelegationMetadata dm, string col, FormulaType colType, bool isStartsWith)
        {
            Contracts.AssertValue(col);
            Contracts.AssertValue(dm);

            if (colType._type != DType.String)
            {
                return false;
            }

            var metadata = dm.FilterDelegationMetadata;

            if (metadata == null)
            {
                return false;
            }

            var capabilityToCheck = isStartsWith ? DelegationCapability.StartsWith : DelegationCapability.EndsWith;
            
            return metadata.IsDelegationSupportedByColumn(DPath.Root.Append(new DName(col)), DelegationCapability.Filter | capabilityToCheck);
        }

        public static bool DoesColumnSupportFunction(this IDelegationMetadata dm, FieldFunction fieldFunction, string col)
        {
            Contracts.AssertValue(col);
            Contracts.AssertValue(dm);

            var metadata = dm.FilterDelegationMetadata;
            if (metadata == null)
            {
                return false;
            }

            var capabilityToCheck = DelegationCapability.None;

            switch (fieldFunction)
            {
                case FieldFunction.Year:
                    capabilityToCheck = DelegationCapability.Year;
                    break;
                case FieldFunction.Month:
                    capabilityToCheck = DelegationCapability.Month;
                    break;
                case FieldFunction.Hour:
                    capabilityToCheck = DelegationCapability.Hour;
                    break;
                default:
                    return false;
            }

            return metadata.IsDelegationSupportedByColumn(DPath.Root.Append(new DName(col)), DelegationCapability.Filter | capabilityToCheck);
        }
    }
}
