using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

        protected override async Task<FormulaValue> ExecuteAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            var filter = new FilterExpression(LogicalOperator.And);
            var relations = new HashSet<LinkEntity>(new LinkEntityComparer());
            foreach ( var arg in args)
            {
                var siblingFilter = ((DelegationFormulaValue)arg)._value;
                var siblingRelation = ((DelegationFormulaValue)arg)._relation;
                filter.AddFilter(siblingFilter);
                relations.UnionWith(siblingRelation);
            }

            return new DelegationFormulaValue(filter, relations);
        }
    }
}
