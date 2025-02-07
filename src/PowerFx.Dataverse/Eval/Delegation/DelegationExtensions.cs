// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation
{
    internal static class DelegationExtensions
    {
        public static bool CanDelegateSummarize(this TableDelegationInfo tableDelegationInfo, string columnName, SummarizeMethod summarizeMethod, bool isDataverseDelegation)
        {
            if (isDataverseDelegation)
            {
                return true;
            }

            if (tableDelegationInfo == null || tableDelegationInfo.SummarizeCapabilities == null)
            {
                return false;
            }

            var summarizeCapabilities = tableDelegationInfo.SummarizeCapabilities;
            if (summarizeCapabilities.IsSummarizableProperty(columnName, summarizeMethod))
            {
                return true;
            }

            return false;
        }
    }
}
