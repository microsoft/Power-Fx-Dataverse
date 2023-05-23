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
    /// Generates a delegation filter expression for the Less than operator.
    /// </summary>
    internal class DelegatedLt  : DelegatedOperatorFunction
    {
        public DelegatedLt(DelegationHooks hooks) : base(hooks, "__lt", ConditionOperator.LessThan, FormulaType.Blank, FormulaType.String) { }
    }
}