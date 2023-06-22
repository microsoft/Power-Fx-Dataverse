using Microsoft.PowerFx.Types;
using Microsoft.PowerFx.Core.IR.Nodes;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Generates a delegation filter expression for the Less than operator.
    /// </summary>
    internal class DelegatedLt  : DelegatedOperatorFunction
    {
        public DelegatedLt(DelegationHooks hooks, BinaryOpKind operation) 
            : base(hooks, "__lt", operation) { }
    }
}