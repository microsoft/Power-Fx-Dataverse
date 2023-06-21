using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Generates a delegation filter expression for the Greater or Equal operator.
    /// </summary>
    internal class DelegatedGeq : DelegatedOperatorFunction
    {
        public DelegatedGeq(DelegationHooks hooks)
          : base(hooks, "__gte", ConditionOperator.GreaterEqual, FormulaType.String)
        {
        }
    }
}