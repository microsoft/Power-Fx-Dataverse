// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation
{
    internal static class Extensions
    {
        public static IEnumerable<string> GetPrimaryKeyNames(this AggregateType aggregateType)
        {
            if (aggregateType._type.AssociatedDataSources.Any())
            {
                // CDS
                if (aggregateType._type.AssociatedDataSources.First() is DataverseDataSourceInfo dataverseSourceInfo)
                {
                    return new List<string>() { dataverseSourceInfo.PrimaryKeyName };
                }

                // CDP
                if (aggregateType._type.AssociatedDataSources.First() is DataSourceInfo cdpSourceInfo)
                {
                    return cdpSourceInfo.DelegationInfo.PrimaryKeyNames;
                }
            }

            return new List<string>();
        }
    }
}
