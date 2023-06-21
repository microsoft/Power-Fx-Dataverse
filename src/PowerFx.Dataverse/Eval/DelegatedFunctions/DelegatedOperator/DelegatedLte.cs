using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Generates a delegation filter expression for the Less or Equal operator.
    /// </summary>
    internal class DelegatedLeq : DelegatedOperatorFunction
    {
        public DelegatedLeq(DelegationHooks hooks) 
            : base(hooks, "__lte", ConditionOperator.LessEqual, FormulaType.String) { }
    }
}