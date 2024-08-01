// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Types;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Generates a delegation filter expression for the Greater than operator.
    /// </summary>
    internal class DelegatedGt : DelegatedOperatorFunction
    {
        public DelegatedGt(DelegationHooks hooks, BinaryOpKind operation)
          : base(hooks, "__gt", operation)
        {
        }
    }
}
