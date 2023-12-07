using Microsoft.PowerFx.Types;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;
using Microsoft.PowerFx.Core.IR.Nodes;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Generates a delegation filter expression for the Equal operator
    /// </summary>
    internal class DelegatedEq : DelegatedOperatorFunction
    {
        public DelegatedEq(DelegationHooks hooks, BinaryOpKind operation)
          : base(hooks, "__eq", operation)
        {
        }
    }
}