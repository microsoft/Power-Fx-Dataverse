using Microsoft.PowerFx.Types;
using Microsoft.PowerFx.Core.IR.Nodes;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Generates a delegation filter expression for the Less or Equal operator.
    /// </summary>
    internal class DelegatedLeq : DelegatedOperatorFunction
    {
        public DelegatedLeq(DelegationHooks hooks, BinaryOpKind operation) 
            : base(hooks, "__lte", operation) { }
    }
}