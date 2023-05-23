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
    /// Generates a delegation filter expression for the Greater than operator.
    /// </summary>
    internal class DelegatedGt : DelegatedOperatorFunction
    {
        public DelegatedGt(DelegationHooks hooks)
          : base(hooks, "__gt", ConditionOperator.GreaterThan, FormulaType.Blank, FormulaType.String)
        {
        }
    }
}