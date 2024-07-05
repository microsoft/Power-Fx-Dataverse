using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Dataverse.Eval.Core;
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

            var filter = new FilterExpression(LogicalOperator.Or);
            var relations = new HashSet<LinkEntity>(new LinkEntityComparer());
            string partitionId = null;
            bool appendPartitionIdFilter = false;
            foreach (var arg in args)
            {
                var siblingFilter = ((DelegationFormulaValue)arg)._filter;
                var siblingRelation = ((DelegationFormulaValue)arg)._relation;
                var siblingPartitionId = ((DelegationFormulaValue)arg)._partitionId;
                if (partitionId != null && partitionId != siblingPartitionId)
                {
                    siblingFilter = DelegatedOperatorFunction.GenerateFilterExpression("partitionid", ConditionOperator.Equal, siblingPartitionId);
                    appendPartitionIdFilter = true;
                }
                else
                {
                    partitionId = ((DelegationFormulaValue)arg)._partitionId;
                }

                filter.AddFilter(siblingFilter);
                relations.UnionWith(siblingRelation);
            }

            if (appendPartitionIdFilter)
            {
                filter.AddFilter(DelegatedOperatorFunction.GenerateFilterExpression("partitionid", ConditionOperator.Equal, partitionId));
            }

            return new DelegationFormulaValue(filter, relations, null);
        }
    }
}
