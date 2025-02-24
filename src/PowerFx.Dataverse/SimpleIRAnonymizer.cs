// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Text;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse
{
    public static class IRExtensions
    {
        public static string GetAnonymousIR(this CheckResult check)
        {
            SimpleIRAnonymizer anonymizer = new SimpleIRAnonymizer();

            try
            {
                IRResult ir = check.ApplyIR();
                IRAnonymizerContext context = new IRAnonymizerContext();
                ir.TopNode.Accept(anonymizer, context);

                return context.Text;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    internal class IRAnonymizerContext
    {
        private readonly StringBuilder _sb;

        public string Text => _sb.ToString();

        public IRAnonymizerContext()
        {
            _sb = new StringBuilder();
        }

        public void Append(string str)
        {
            _sb.Append(str);
        }

        public void Append(char c)
        {
            _sb.Append(c);
        }

        public void Append(double dbl)
        {
            _sb.Append(dbl);
            _sb.Append(':');
            _sb.Append('n');
        }

        public void Append(decimal dec)
        {
            _sb.Append(dec);
            _sb.Append(':');
            _sb.Append('w');
        }

        public void Append(bool b)
        {
            _sb.Append(b);
            _sb.Append(':');
            _sb.Append('b');
        }
    }

    internal class SimpleIRAnonymizer : IRNodeVisitor<string, IRAnonymizerContext>
    {
        public override string Visit(TextLiteralNode node, IRAnonymizerContext context)
        {
            context.Append('"');
            context.Append(new string('x', node.LiteralValue.Length));
            context.Append(@""":s");
            return null;
        }

        public override string Visit(NumberLiteralNode node, IRAnonymizerContext context)
        {
            context.Append(0d);
            return null;
        }

        public override string Visit(DecimalLiteralNode node, IRAnonymizerContext context)
        {
            context.Append(1m);
            return null;
        }

        public override string Visit(BooleanLiteralNode node, IRAnonymizerContext context)
        {
            context.Append(node.LiteralValue);
            return null;
        }

        public override string Visit(ColorLiteralNode node, IRAnonymizerContext context)
        {
            context.Append("Color(");
            context.Append(node.LiteralValue.ToString());
            context.Append(')');
            return null;
        }

        public override string Visit(RecordNode node, IRAnonymizerContext context)
        {
            context.Append('{');
            int i = 0;
            foreach (var child in node.Fields)
            {
                if (i > 0)
                {
                    context.Append(", ");
                }

                i++;

                context.Append(child.Key);
                context.Append(": ");
                child.Value.Accept(this, context);
            }

            context.Append('}');
            return null;
        }

        public override string Visit(ErrorNode node, IRAnonymizerContext context)
        {
            context.Append("Error(");
            context.Append(node.ErrorHint);
            context.Append(')');
            return null;
        }

        public override string Visit(LazyEvalNode node, IRAnonymizerContext context)
        {
            context.Append("Lazy(");
            node.Child.Accept(this, context);
            context.Append(')');
            return null;
        }

        public override string Visit(CallNode node, IRAnonymizerContext context)
        {
            context.Append(node.Function.Name);
            context.Append(':');
            context.Append(node.IRContext.ResultType._type.ToString());

            // ignore node.Scope

            context.Append('(');

            int i = 0;
            foreach (var arg in node.Args)
            {
                if (i > 0)
                {
                    context.Append(", ");
                }

                i++;

                arg.Accept(this, context);
            }

            context.Append(')');
            return null;
        }

        public override string Visit(BinaryOpNode node, IRAnonymizerContext context)
        {
            context.Append(node.Op.ToString());
            context.Append(':');
            context.Append(node.IRContext.ResultType._type.ToString());
            context.Append('(');
            node.Left.Accept(this, context);
            context.Append(", ");
            node.Right.Accept(this, context);
            context.Append(')');

            return null;
        }

        public override string Visit(UnaryOpNode node, IRAnonymizerContext context)
        {
            context.Append(node.Op.ToString());
            context.Append(':');
            context.Append(node.IRContext.ResultType._type.ToString());
            context.Append('(');
            node.Child.Accept(this, context);
            context.Append(')');

            return null;
        }

        public override string Visit(ScopeAccessNode node, IRAnonymizerContext context)
        {
            context.Append("ScopeAccess(");

            if (node.Value is ScopeAccessSymbol s)
            {
                context.Append(s.Name);
            }
            else if (node.Value is ScopeSymbol sy)
            {
                context.Append($"Scope_{sy.Id}");
            }
            else
            {
                context.Append("Unknown");
            }

            context.Append(')');
            return null;
        }

        public override string Visit(RecordFieldAccessNode node, IRAnonymizerContext context)
        {
            context.Append("FieldAccess(");
            node.From.Accept(this, context);
            context.Append(", ");
            context.Append(node.Field.Value);
            context.Append(')');

            return null;
        }

        public override string Visit(ResolvedObjectNode node, IRAnonymizerContext context)
        {
            context.Append("ResolvedObject(");

            if (node.Value is NameSymbol n)
            {
                context.Append(n.Name);
            }
            else if (node.Value is IExternalEntity ee)
            {
                context.Append(ee.EntityName);
            }
            else if (node.Value is FormulaValue fv)
            {
                context.Append(fv.ToExpression());
            }
            else
            {
                context.Append("Unknown");
            }

            context.Append(')');
            return null;
        }

        public override string Visit(SingleColumnTableAccessNode node, IRAnonymizerContext context)
        {
            context.Append("SingleColumnAccess(");
            node.From.Accept(this, context);
            context.Append(", ");
            context.Append(node.Field.Value);
            context.Append(')');
            return null;
        }

        public override string Visit(ChainingNode node, IRAnonymizerContext context)
        {
            context.Append("Chained(");
            int i = 0;
            foreach (var child in node.Nodes)
            {
                if (i > 0)
                {
                    context.Append(',');
                }

                i++;

                child.Accept(this, context);
            }

            context.Append(')');
            return null;
        }

        public override string Visit(AggregateCoercionNode node, IRAnonymizerContext context)
        {
            context.Append("AggregateCoercion(");
            context.Append(node.Op.ToString());
            context.Append(", ");

            int i = 0;
            foreach (var field in node.FieldCoercions)
            {
                if (i > 0)
                {
                    context.Append(", ");
                }

                i++;

                field.Value.Accept(this, context);
                context.Append(" <- ");
                field.Value.Accept(this, context);
            }

            context.Append(')');
            return null;
        }
    }
}
