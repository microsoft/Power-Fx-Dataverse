using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using System;

namespace Microsoft.PowerFx.Dataverse.Eval.Core
{
    // Search the IR for a condition.
    // This will walk the entire tree until it gets a non-null TResult, and then returns it. 
    // Beware some traversals (record fields, etc) are unordered. 
    internal class SearchIRVisitor<TResult, TContext> : IRNodeVisitor<TResult, TContext>
        where TResult : class
    {
        public override TResult Visit(TextLiteralNode node, TContext context)
        {
            return null; // Leaf
        }

        public override TResult Visit(NumberLiteralNode node, TContext context)
        {
            return null; // Leaf
        }

        public override TResult Visit(DecimalLiteralNode node, TContext context)
        {
            return null; // Leaf
        }

        public override TResult Visit(BooleanLiteralNode node, TContext context)
        {
            return null; // Leaf
        }

        public override TResult Visit(ColorLiteralNode node, TContext context)
        {
            return null; // Leaf
        }

        public override TResult Visit(RecordNode node, TContext context)
        {
            foreach (var child in node.Fields)
            {
                var ret = child.Value.Accept(this, context);
                if (ret != null)
                {
                    return ret;
                }
            }
            return null;
        }

        public override TResult Visit(ErrorNode node, TContext context)
        {
            return null; // Leaf
        }

        public override TResult Visit(LazyEvalNode node, TContext context)
        {
            return node.Child.Accept(this, context);
        }

        public override TResult Visit(CallNode node, TContext context)
        {
            foreach (var arg in node.Args)
            {
                var ret = arg.Accept(this, context);
                if (ret != null)
                {
                    return ret;
                }
            }
            return null;
        }

        public override TResult Visit(BinaryOpNode node, TContext context)
        {
            return
                node.Left.Accept(this, context) ??
                node.Right.Accept(this, context);
        }

        public override TResult Visit(UnaryOpNode node, TContext context)
        {
            return node.Child.Accept(this, context);
        }

        public override TResult Visit(ScopeAccessNode node, TContext context)
        {
            return null;
        }

        public override TResult Visit(RecordFieldAccessNode node, TContext context)
        {
            return node.From.Accept(this, context);
        }

        public override TResult Visit(ResolvedObjectNode node, TContext context)
        {
            return null;
        }

        public override TResult Visit(SingleColumnTableAccessNode node, TContext context)
        {
            throw new NotImplementedException();
        }

        public override TResult Visit(ChainingNode node, TContext context)
        {
            foreach (var child in node.Nodes)
            {
                var ret = child.Accept(this, context);
                if (ret != null)
                {
                    return ret;
                }
            }
            return null;
        }

        public override TResult Visit(AggregateCoercionNode node, TContext context)
        {
            var ret2 = node.Child.Accept(this, context);
            if (ret2 != null)
            {
                return ret2;
            }

            foreach (var child in node.FieldCoercions)
            {
                var ret = child.Value.Accept(this, context);
                if (ret != null)
                {
                    return ret;
                }
            }
            return null;
        }
    }
}