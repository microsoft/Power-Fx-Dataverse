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
    internal class DelegatedOr : DelegateFunction
    {
        public DelegatedOr(DelegationHooks hooks, TableType tableType)
          : base(hooks, "__or", tableType, tableType, FormulaType.Number)
        {
        }

        public override async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            var filter = new FilterExpression(LogicalOperator.Or);
            foreach (var arg in args)
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