// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx.Core.Entities;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation
{
    internal static class DelegationExtensions
    {
        public static bool CanDelegateSummarize(this TableDelegationInfo tableDelegationInfo, string columnName, SummarizeMethod method, bool isDataverseDelegation)
        {
            return tableDelegationInfo.CanDelegateSummarize(columnName, isDataverseDelegation) && tableDelegationInfo.CanDelegateSummarize(method, isDataverseDelegation);
        }

        public static bool CanDelegateSummarize(this TableDelegationInfo tableDelegationInfo, string columnName, bool isDataverseDelegation)
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
            if (summarizeCapabilities.IsSummarizableProperty(columnName))
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
