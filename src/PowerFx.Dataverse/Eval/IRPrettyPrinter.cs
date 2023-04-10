using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using System;
using System.Linq;
using System.Text;

namespace Microsoft.PowerFx.Dataverse
{
    // Pretty printer for tests. 
    // Stable  - tests rely on this, so we need to fix the format 
    // Compact / No newlines - ideally can fit on a single line in Theorys
    // Deterministic ordering - when traversing dictionaries
    // Unambiguous - additional parenthesis as needed to show (a+b)+c vs. a+(b+c). 
    internal class PrettyPrintIR : IRNodeVisitor<PrettyPrintIR.RetVal, PrettyPrintIR.Context>
    {
        private StringBuilder _sb = new StringBuilder();

        public class RetVal
        {
        }
        public class Context
        {

        }

        internal static string ToString(CheckResult check)
        {
            var irNode = check.ApplyIR();
            return ToString(irNode.TopNode);
        }

        public static string ToString(IntermediateNode node)
        {
            var visitor = new PrettyPrintIR();
            var ret = node.Accept(visitor, new Context());
            return visitor._sb.ToString();  
        }

        public override RetVal Visit(TextLiteralNode node, Context context)
        {
            _sb.Append(node.LiteralValue);
            return null;
        }

        public override RetVal Visit(NumberLiteralNode node, Context context)
        {
            _sb.Append(node.LiteralValue);
            return null;
        }

        public override RetVal Visit(DecimalLiteralNode node, Context context)
        {
            _sb.Append(node.LiteralValue);
            return null;
        }

        public override RetVal Visit(BooleanLiteralNode node, Context context)
        {
            _sb.Append(node.LiteralValue);
            return null;
        }

        public override RetVal Visit(ColorLiteralNode node, Context context)
        {
            _sb.Append(node.LiteralValue);
            return null;
        }

        public override RetVal Visit(RecordNode node, Context context)
        {
            _sb.Append('{');
            int i = 0;
            foreach(var child in node.Fields.OrderBy(x => x.Key))
            {
                if (i > 0)
                {
                    _sb.Append(", ");
                }
                i++;

                _sb.Append(child.Key);
                _sb.Append(":");
                child.Value.Accept(this, context);

            }
            _sb.Append('}');
            return null;
        }

        public override RetVal Visit(ErrorNode node, Context context)
        {
            throw new NotImplementedException();
        }

        public override RetVal Visit(LazyEvalNode node, Context context)
        {
            return node.Child.Accept(this, context);
        }

        public override RetVal Visit(CallNode node, Context context)
        {
            _sb.Append(node.Function.Name);
            _sb.Append('(');

            int i = 0;
            foreach (var arg in node.Args) // ordered
            {
                if (i > 0)
                {
                    _sb.Append(", ");
                }
                i++;

                arg.Accept(this, context);

            }

            _sb.Append(')');
            return null;
        }

        public override RetVal Visit(BinaryOpNode node, Context context)
        {            
            _sb.Append(node.Op.ToString());
            _sb.Append('(');
            node.Left.Accept(this, context);            
            _sb.Append(',');
            node.Right.Accept(this, context);
            _sb.Append(')');

            return null;
        }

        public override RetVal Visit(UnaryOpNode node, Context context)
        {
            _sb.Append(node.Op.ToString());
            _sb.Append('(');
            node.Child.Accept(this, context);
            _sb.Append(')');

            return null;
        }

        public override RetVal Visit(ScopeAccessNode node, Context context)
        {
            if (node.Value is ScopeAccessSymbol s)
            {
                _sb.Append(s.Name);               
            }
            else
            {

            }

            return null;
        }

        public override RetVal Visit(RecordFieldAccessNode node, Context context)
        {
            _sb.Append('(');
            node.From.Accept(this, context);
            _sb.Append(").");
            _sb.Append(node.Field.Value);

            return null;
        }

        public override RetVal Visit(ResolvedObjectNode node, Context context)
        {
            if (node.Value is NameSymbol n)
            {
                _sb.Append(n.Name);
            }
            else
            {
                throw new NotImplementedException();
            }
            return null;
        }

        public override RetVal Visit(SingleColumnTableAccessNode node, Context context)
        {
            throw new NotImplementedException();
        }

        public override RetVal Visit(ChainingNode node, Context context)
        {
            _sb.Append('(');
            foreach(var child in node.Nodes)
            {
                child.Accept(this, context);
                _sb.Append(';');
            }
            _sb.Append(')');
            return null;
        }

        public override RetVal Visit(AggregateCoercionNode node, Context context)
        {
            throw new NotImplementedException();
        }
    } 
}
