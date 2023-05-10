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
    internal class DelegatedGeq : DelegateFunction
    {
        public DelegatedGeq(DelegationHooks hooks, TableType tableType)
          : base(hooks, "__gte", tableType, FormulaType.String)
        {
        }

        public override async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            var result = DelegationHelper.OperatorFilter(args, ConditionOperator.GreaterEqual, ReturnFormulaType);
            return result;
        }
    }
}