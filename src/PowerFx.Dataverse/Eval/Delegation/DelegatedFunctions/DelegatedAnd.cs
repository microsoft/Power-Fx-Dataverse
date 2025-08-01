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
    /// Generates a delegation filter expression for the And operator.
    /// </summary>
    internal class DelegatedAnd : DelegateFunction
    {
        public DelegatedAnd(DelegationHooks hooks)
          : base(hooks, "__and", FormulaType.Blank)
        {
        }

        protected override async Task<FormulaValue> ExecuteAsync(IServiceProvider services, FormulaValue[] args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var filter = new FxFilterExpression(FxFilterOperator.And);
            var joins = new HashSet<FxJoinNode>(new JoinComparer());
            string partitionId = null;
            bool appendPartitionIdFilter = false;

            foreach (var arg in args)
            {
                var siblingFilter = ((DelegationFormulaValue)arg)._filter;
                var siblingRelation = ((DelegationFormulaValue)arg)._join;
                var siblingPartitionId = ((DelegationFormulaValue)arg)._partitionId;

                if (siblingPartitionId != null)
                {
                    if (partitionId != null && partitionId != siblingPartitionId)
                    {
                        siblingFilter = DelegatedOperatorFunction.GenerateFilterExpression("partitionid", FxConditionOperator.Equal, siblingPartitionId, FieldFunction.None);
                        appendPartitionIdFilter = true;
                    }
                    else
                    {
                        partitionId = ((DelegationFormulaValue)arg)._partitionId;
                    }
                }

                filter.AddFilter(siblingFilter);
                joins.UnionWith(siblingRelation);
            }

            if (appendPartitionIdFilter)
            {
                // Partition ID of elastic table, does not support FieldFunction.
                filter.AddFilter(DelegatedOperatorFunction.GenerateFilterExpression("partitionid", FxConditionOperator.Equal, partitionId, FieldFunction.None));
            }

            // OrderBy makes no sense here
            return new DelegationFormulaValue(filter, orderBy: null, partitionId: partitionId, join: joins);
        }
    }
}
