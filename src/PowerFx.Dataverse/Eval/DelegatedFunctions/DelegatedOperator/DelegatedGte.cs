using Microsoft.PowerFx.Types;
using Microsoft.PowerFx.Core.IR.Nodes;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Generates a delegation filter expression for the Greater or Equal operator.
    /// </summary>
    internal class DelegatedGeq : DelegatedOperatorFunction
    {
        public DelegatedGeq(DelegationHooks hooks, BinaryOpKind operation)
          : base(hooks, "__gte", operation)
        {
        }
    }
}