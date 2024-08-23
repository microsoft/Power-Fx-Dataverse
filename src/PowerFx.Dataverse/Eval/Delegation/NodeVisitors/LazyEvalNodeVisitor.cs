// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        public override RetVal Visit(LazyEvalNode node, Context context)
        {
            RetVal child = node.Child.Accept(this, context);

            if (child.IsDelegating)
            {
                if (!context.IsPredicateEvalInProgress)
                {
                    node = new LazyEvalNode(node.IRContext, Materialize(child));

                    // We want to preserve isDelegating flag here
                    return new RetVal(node, true);
                }

                return child;
            }
            else
            {
                if (!ReferenceEquals(child.OriginalNode, node.Child))
                {
                    node = new LazyEvalNode(node.IRContext, child.OriginalNode);
                }

                return Ret(node);
            }
        }
    }
}
