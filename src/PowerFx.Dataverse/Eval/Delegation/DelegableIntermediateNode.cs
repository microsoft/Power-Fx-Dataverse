// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation
{
    internal class DelegableIntermediateNode : IntermediateNode
    {
        internal readonly IntermediateNode InnerNode;

        public DelegableIntermediateNode(IntermediateNode node)
            : base(node.IRContext)
        {
            if (node is DelegableIntermediateNode delegableNode)
            {
                node = delegableNode.InnerNode;
            }

            if (node is not(ResolvedObjectNode or ScopeAccessNode))
            {
                throw new ArgumentException($"Invalid Arg type: {node.GetType()}, It can only be {nameof(ResolvedObjectNode)} or {nameof(ScopeAccessNode)}");
            }

            this.InnerNode = node;
        }

        public override TResult Accept<TResult, TContext>(IRNodeVisitor<TResult, TContext> visitor, TContext context)
        {
            return InnerNode.Accept(visitor, context);
        }

        public override string ToString() => $"Delegable({InnerNode}";
    }
}
