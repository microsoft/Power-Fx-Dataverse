using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Generates a delegation filter expression for the Greater than operator.
    /// </summary>
    internal class DelegatedGt : DelegatedOperatorFunction
    {
        public DelegatedGt(DelegationHooks hooks)
          : base(hooks, "__gt", ConditionOperator.GreaterThan, FormulaType.String)
        {
        }
    }
}