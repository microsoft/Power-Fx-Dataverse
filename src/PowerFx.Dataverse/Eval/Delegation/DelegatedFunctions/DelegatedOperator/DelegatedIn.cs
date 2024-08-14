// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Types;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Generates a delegation filter expression for In call (case insensitive).
    /// </summary>
    internal class DelegatedIn : DelegatedOperatorFunction
    {
        public DelegatedIn(DelegationHooks hooks, BinaryOpKind operation)
          : base(hooks, "__in", operation)
        {
        }
    }
}
