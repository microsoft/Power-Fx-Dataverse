// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.PowerFx.Core.IR.Nodes;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Generates a delegation filter expression for the Equal operator.
    /// </summary>
    internal class DelegatedEndsWith : DelegatedOperatorFunction
    {
        public DelegatedEndsWith(DelegationHooks hooks)
          : base(hooks, "__endsWith", BinaryOpKind.Invalid, ParentOperation.EndsWith)
        {
        }
    }
}
