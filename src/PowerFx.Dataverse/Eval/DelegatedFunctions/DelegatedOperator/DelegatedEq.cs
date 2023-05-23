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
    internal class DelegatedEq : DelegatedOperatorFunction
    {
        public DelegatedEq(DelegationHooks hooks)
          : base(hooks, "__eq", ConditionOperator.Equal, FormulaType.Blank, FormulaType.String)
        {
        }
    }
}