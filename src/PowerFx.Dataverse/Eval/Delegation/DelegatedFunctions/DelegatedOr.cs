﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Generates a delegation filter expression for the Or operator.
    /// </summary>
    internal class DelegatedOr : DelegateFunction
    {
        public DelegatedOr(DelegationHooks hooks)
          : base(hooks, "__or", FormulaType.Blank)
        {
        }

        protected override async Task<FormulaValue> ExecuteAsync(IServiceProvider services, FormulaValue[] args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var filter = new FxFilterExpression(FxFilterOperator.Or);
            var joins = new HashSet<FxJoinNode>(new JoinComparer());
            string partitionId = null;
            bool appendPartitionIdFilter = false;
            foreach (var arg in args)
            {
                var siblingFilter = ((DelegationFormulaValue)arg)._filter;
                var siblingJoin = ((DelegationFormulaValue)arg)._join;
                var siblingPartitionId = ((DelegationFormulaValue)arg)._partitionId;
                if (partitionId != null && partitionId != siblingPartitionId)
                {
                    siblingFilter = DelegatedOperatorFunction.GenerateFilterExpression("partitionid", FxConditionOperator.Equal, siblingPartitionId, FieldFunction.None);
                    appendPartitionIdFilter = true;
                }
                else
                {
                    partitionId = ((DelegationFormulaValue)arg)._partitionId;
                }

                filter.AddFilter(siblingFilter);
                joins.UnionWith(siblingJoin);
            }

            if (appendPartitionIdFilter)
            {
                // Partition ID of elastic table, does not support FieldFunction.
                filter.AddFilter(DelegatedOperatorFunction.GenerateFilterExpression("partitionid", FxConditionOperator.Equal, partitionId, FieldFunction.None));
            }

            // OrderBy makes no sense here
            return new DelegationFormulaValue(filter, orderBy: null, join: joins);
        }
    }
}
