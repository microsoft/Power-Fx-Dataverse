using Microsoft.AppMagic.Authoring.Importers.DataDescription;
using Microsoft.AppMagic.Authoring.Importers.ServiceConfig;
using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Texl.Builtins;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Dataverse.DataSource;
using Microsoft.PowerFx.Dataverse.Functions;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using BuiltinFunctionsCore = Microsoft.PowerFx.Core.Texl.BuiltinFunctionsCore;
using Span = Microsoft.PowerFx.Syntax.Span;

namespace Microsoft.PowerFx.Dataverse
{
    // Rewrite the tree inject delegation 
    internal abstract class RewritingIRVisitor<TRet, TCtx> : IRNodeVisitor<TRet, TCtx>
    {
        protected abstract IntermediateNode Materialize(TRet ret);
        protected abstract TRet Ret(IntermediateNode node);


        public override TRet Visit(TextLiteralNode node, TCtx context)
        {
            return Ret(node);
        }

        public override TRet Visit(NumberLiteralNode node, TCtx context)
        {
            return Ret(node);
        }

        public override TRet Visit(DecimalLiteralNode node, TCtx context)
        {
            return Ret(node);
        }

        public override TRet Visit(BooleanLiteralNode node, TCtx context)
        {
            return Ret(node);
        }

        public override TRet Visit(ColorLiteralNode node, TCtx context)
        {
            return Ret(node);
        }

        public override TRet Visit(RecordNode node, TCtx context)
        {
            var newFields = VisitDict(node.Fields, context);

            if (newFields != null)
            {
                var newNode = new RecordNode(node.IRContext, newFields);
                return Ret(newNode);
            }
            return Ret(node);
        }

        // Visit each field. If no changes, then return null, signalling to caller to just pass the origina dictionary. 
        // If anything changes, return a new dictionary with the updates. 
        private IReadOnlyDictionary<DName, IntermediateNode> VisitDict(IReadOnlyDictionary<DName, IntermediateNode>  fields, TCtx context)
        { 
            // Dictionary order is arbitrary, so just make a copy upfront rather than enumerate multiple times. 
            Dictionary<DName, IntermediateNode> newFields = new Dictionary<DName, IntermediateNode>();

            bool rewrite = false;
            foreach(var kv in fields)
            {
                var child = Materialize(kv.Value.Accept(this, context));

                newFields[kv.Key] = child;

                if (!object.ReferenceEquals(child, kv.Value))
                {
                    rewrite = true;
                }
            }

            if (rewrite)
            {
                return newFields;
            }
            return null;
        }

        public override TRet Visit(ErrorNode node, TCtx context)
        {
            return Ret(node);
        }

        public override TRet Visit(LazyEvalNode node, TCtx context)
        {
            return Ret(node);
        }

        public override TRet Visit(CallNode node, TCtx context)
        {
            // Derived visitor gets first chance. 
            // Can callback to base if it doesn't handle. 

            TRet arg0 = default(TRet);
            if (node.Args.Count > 0)
            {
                arg0 = node.Args[0].Accept(this, context);
            }
            return Visit(node, context, arg0);
        }

        // Return null if no change. 
        // Else returns a new copy ofthe list with changes. 
        private IList<IntermediateNode> VisitList(IList<IntermediateNode> list, TCtx context, TRet arg0 = default(TRet))
        {
            List<IntermediateNode> newArgs = null;

            for (int i = 0; i < list.Count; i++)
            {
                var arg = list[i];
                var ret = (i == 0 && arg0 != null) ? arg0 : arg.Accept(this, context);

                var result = Materialize(ret);

                if (newArgs == null && !object.ReferenceEquals(arg, result))
                {
                    newArgs = new List<IntermediateNode>(list.Count);

                    // Copy previous
                    for (int j = 0; j < i; j++)
                    {
                        newArgs.Add(list[j]);
                    }
                }

                if (newArgs != null)
                {
                    newArgs.Add(result);
                }
            }

            return newArgs;
        }

        // Pass in arg0 is we already called Accept on it.
        public TRet Visit(CallNode node, TCtx context, TRet arg0)
        {
            var newArgs = VisitList(node.Args, context, arg0);

            if (newArgs == null)
            {
                // No change
                return Ret(node);
            }

            // Copy over to new node 
            if (node.Scope == null)
            {
                return Ret(new CallNode(node.IRContext, node.Function, newArgs));
            } else
            {
                return Ret(new CallNode(node.IRContext, node.Function, node.Scope, newArgs));
            }
        }

        public override TRet Visit(BinaryOpNode node, TCtx context)
        {
            var left = Materialize(node.Left.Accept(this, context));
            var right = Materialize(node.Right.Accept(this, context));

            if (object.ReferenceEquals(left, node.Left) && object.ReferenceEquals(right, node.Right))
            {
                // same
                return Ret(node);
            }

            // A branch was rewritten, create new node. 
            var newNode = new BinaryOpNode(node.IRContext, node.Op, left, right);
            return Ret(newNode);
        }

        public override TRet Visit(UnaryOpNode node, TCtx context)
        {
            var child = Materialize(node.Child.Accept(this, context));
            if (object.ReferenceEquals(child, node.Child))
            {
                return Ret(node.Child);
            }
            var newNode = new UnaryOpNode(node.IRContext, node.Op, child);
            return Ret(newNode);
        }

        public override TRet Visit(ScopeAccessNode node, TCtx context)
        {
            return Ret(node);
        }

        public override TRet Visit(RecordFieldAccessNode node, TCtx context)
        {
            var left = Materialize(node.From.Accept(this, context));
            if (object.ReferenceEquals(left, node))
            {
                return Ret(node);
            }
            var newNode = new RecordFieldAccessNode(node.IRContext, left, node.Field);
            return Ret(newNode);
        }

        public override TRet Visit(ResolvedObjectNode node, TCtx context)
        {
            return Ret(node);
        }

        public override TRet Visit(SingleColumnTableAccessNode node, TCtx context)
        {
            throw new NotImplementedException();
        }

        public override TRet Visit(ChainingNode node, TCtx context)
        {
            var newArgs = VisitList(node.Nodes, context);
            if (newArgs != null)
            {
                var newNode = new ChainingNode(node.IRContext, newArgs);
                return Ret(newNode);
            }
            return Ret(node);
        }

        public override TRet Visit(AggregateCoercionNode node, TCtx context)
        {
            IntermediateNode child = Materialize(node.Child.Accept(this, context));

            var newFields = VisitDict(node.FieldCoercions, context);

            if (newFields != null || !object.ReferenceEquals(child, node.Child))
            {
                var newNode = new AggregateCoercionNode(node.IRContext, node.Op, node.Scope, child, newFields);
                return Ret(newNode);
            }
            return Ret(node);

        }
    }
}