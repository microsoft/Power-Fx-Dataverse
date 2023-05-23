using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Dataverse.Eval.DelegatedFunctions;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Threading;
using System.Threading.Tasks;
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

        public override async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            var filter = new FilterExpression(LogicalOperator.Or);
            foreach (var arg in args)
            {
                if(arg is DelegationFormulaValue div)
                {
                    var childFilter = div._value;
                    filter.AddFilter(childFilter);
                }
                else
                {
                    throw DelegationHelper.CommonException.InvalidInputArg;
                }
            }

            return new DelegationFormulaValue(filter);
        }
    }
}