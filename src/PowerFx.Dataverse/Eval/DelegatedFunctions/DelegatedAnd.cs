using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Generates a delegation filter expression for the And operator
    /// </summary>
    internal class DelegatedAnd : DelegateFunction
    {
        public DelegatedAnd(DelegationHooks hooks)
          : base(hooks, "__and", FormulaType.Blank)
        {
        }

        public override async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            var filter = new FilterExpression(LogicalOperator.And);
            foreach ( var arg in args)
            {
                if(arg is DelegationInfoValue div && div._value is FilterExpression childFilter)
                {
                    filter.AddFilter(childFilter);
                }
                else
                {
                    throw new NotImplementedException();
                }
            }

            return new DelegationInfoValue(IRContext.NotInSource(ReturnFormulaType), filter);
        }
    }
}