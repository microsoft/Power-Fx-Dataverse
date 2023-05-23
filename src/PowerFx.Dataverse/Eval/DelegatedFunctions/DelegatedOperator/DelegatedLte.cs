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
    /// Generates a delegation filter expression for the Less or Equal operator.
    /// </summary>
    internal class DelegatedLeq : DelegatedOperatorFunction
    {
        public DelegatedLeq(DelegationHooks hooks) : base(hooks, "__lte", ConditionOperator.LessEqual, FormulaType.Blank, FormulaType.String) { }
    }
}