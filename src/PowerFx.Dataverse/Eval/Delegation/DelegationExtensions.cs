// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation
{
    internal static class DelegationExtensions
    {
        public static bool CanDelegateSummarize(this TableDelegationInfo tableDelegationInfo, FxColumnInfo columnInfo, SummarizeMethod method, bool isDataverseDelegation)
        {
            return tableDelegationInfo.CanDelegateSummarize(columnInfo, isDataverseDelegation) && tableDelegationInfo.CanDelegateSummarize(method, isDataverseDelegation);
        }

        public static bool CanDelegateSummarize(this TableDelegationInfo tableDelegationInfo, FxColumnInfo columnInfo, bool isDataverseDelegation)
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
            if (summarizeCapabilities.IsSummarizableProperty(columnInfo.RealColumnName))
            {
                return true;
            }

            return false;
        }

        public static bool CanDelegateSummarize(this TableDelegationInfo tableDelegationInfo, SummarizeMethod method, bool isDataverseDelegation)
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
            if (summarizeCapabilities.IsSummarizableMethod(method))
            {
                return true;
            }

            return false;
        }
    }
}
