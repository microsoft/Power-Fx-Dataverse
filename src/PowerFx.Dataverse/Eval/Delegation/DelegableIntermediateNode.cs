using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation
{
    internal class DelegableIntermediateNode : IntermediateNode
    {
        private readonly IntermediateNode _node;

        public DelegableIntermediateNode(IntermediateNode node) 
            : base(node.IRContext)
        {
            if(node is DelegableIntermediateNode delegableNode)
            {
                node = delegableNode._node;
            }
            if( node is not (ResolvedObjectNode or ScopeAccessNode) )
            {
                throw new ArgumentException($"Invalid Arg type: {node.GetType()}, It can only be {nameof(ResolvedObjectNode)} or {nameof(ScopeAccessNode)}");
            }

            this._node = node;
        }
        public override TResult Accept<TResult, TContext>(IRNodeVisitor<TResult, TContext> visitor, TContext context)
        {
            return _node.Accept(visitor, context);
        }
    }
}
