using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Dataverse.Eval.DelegatedFunctions;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Generates a delegation filter expression for the Equal operator
    /// </summary>
    internal class DelegatedGt : DelegateFunction
    {
        public DelegatedGt(DelegationHooks hooks, TableType tableType)
          : base(hooks, "__gt", tableType, FormulaType.String)
        {
        }

        public override async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            var result = DelegationHelper.OperatorFilter(args, ConditionOperator.GreaterThan, ReturnFormulaType);
            return result;
        }
    }
}