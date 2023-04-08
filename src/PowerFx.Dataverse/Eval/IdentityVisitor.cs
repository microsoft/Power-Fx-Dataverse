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
    internal abstract class IdentityIRVisitor<TRet, TCtx> : IRNodeVisitor<TRet, TCtx>
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
            throw new NotImplementedException();
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

            for (int i = 0; i < node.Args.Count; i++)
            {
                var arg = node.Args[i];
                var result = Materialize(arg.Accept(this, context));
                if (object.ReferenceEquals(arg, result))
                {

                }
            }
            throw new NotImplementedException();
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
            throw new NotImplementedException();
        }

        public override TRet Visit(ScopeAccessNode node, TCtx context)
        {
            throw new NotImplementedException();
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
            throw new NotImplementedException();
        }

        public override TRet Visit(SingleColumnTableAccessNode node, TCtx context)
        {
            throw new NotImplementedException();
        }

        public override TRet Visit(ChainingNode node, TCtx context)
        {
            throw new NotImplementedException();
        }

        public override TRet Visit(AggregateCoercionNode node, TCtx context)
        {
            throw new NotImplementedException();
        }
    }
}