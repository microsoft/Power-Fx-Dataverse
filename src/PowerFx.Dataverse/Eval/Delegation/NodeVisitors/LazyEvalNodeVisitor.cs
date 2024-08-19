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
            var child = node.Child.Accept(this, context);

            if (child.IsDelegating)
            {
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
