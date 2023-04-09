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
    // Walk everything
    // Propagate first non-null TResult
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
            foreach(var child in node.Fields)
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
            foreach(var arg in node.Args)
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
            foreach(var child in node.Nodes)
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

            foreach(var child in node.FieldCoercions)
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