using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;
using static Microsoft.PowerFx.Dataverse.Eval.Core.PredicateIRVisitor;
using BinaryOpNode = Microsoft.PowerFx.Core.IR.Nodes.BinaryOpNode;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;
using ErrorNode = Microsoft.PowerFx.Core.IR.Nodes.ErrorNode;
using IntermediateNode = Microsoft.PowerFx.Core.IR.Nodes.IntermediateNode;
using RecordNode = Microsoft.PowerFx.Core.IR.Nodes.RecordNode;
using UnaryOpNode = Microsoft.PowerFx.Core.IR.Nodes.UnaryOpNode;

namespace Microsoft.PowerFx.Dataverse.Eval.Core
{
    internal class PredicateIRVisitor : IRNodeVisitor<RetVal, PredicateIRVisitor.Context>
    {
        private DelegationHooks _hooks;
        private CallNode _caller;

        private FormulaType _callerReturnType
        {
            get { return _caller.IRContext.ResultType; }
        }

        public PredicateIRVisitor(CallNode caller, DelegationHooks hooks)
        {
            if (caller == null || hooks == null)
            {
                throw new ArgumentNullException();
            }

            _hooks = hooks;
            _caller = caller;
        }

        public class RetVal
        {
            public bool isDelegable;
            public IntermediateNode node;

            public RetVal(IntermediateNode node, bool isDelegable)
            {
                this.node = node;
                this.isDelegable = isDelegable;
            }
        }

        public bool TryGetFieldName(IntermediateNode left, IntermediateNode right, out string fieldName, out IntermediateNode node)
        {
            if (TryGetFieldName(left, out var leftField) && !TryGetFieldName(right, out _))
            {
                fieldName = leftField;
                node = right;
                return true;
            }
            else if (TryGetFieldName(right, out var rightField) && !TryGetFieldName(left, out _))
            {
                fieldName = rightField;
                node = left;
                return true;
            }

            node = default;
            fieldName = default;
            return false;
        }

        public bool TryGetFieldName(IntermediateNode node, out string fieldName)
        {
            if (node is ScopeAccessNode scopeAccessNode)
            {
                if (scopeAccessNode.Value is ScopeAccessSymbol scopeAccessSymbol)
                {
                    if (scopeAccessSymbol.Parent.Id == _caller.Scope.Id)
                    {
                        fieldName = scopeAccessSymbol.Name;
                        return true;
                    }
                }
            }

            fieldName = default;
            return false;
        }

        public class Context
        {
        }

        public override RetVal Visit(TextLiteralNode node, Context context)
        {
            return new RetVal(node, false); // Leaf
        }

        public override RetVal Visit(NumberLiteralNode node, Context context)
        {
            return new RetVal(node, false); // Leaf
        }

        public override RetVal Visit(DecimalLiteralNode node, Context context)
        {
            return new RetVal(node, false); // Leaf
        }

        public override RetVal Visit(BooleanLiteralNode node, Context context)
        {
            return new RetVal(node, false); // Leaf
        }

        public override RetVal Visit(ColorLiteralNode node, Context context)
        {
            return new RetVal(node, false); // Leaf
        }

        public override RetVal Visit(RecordNode node, Context context)
        {
            return new RetVal(node, false);
        }

        public override RetVal Visit(ErrorNode node, Context context)
        {
            return new RetVal(node, false); // Leaf
        }

        public override RetVal Visit(LazyEvalNode node, Context context)
        {
            return node.Child.Accept(this, context);
        }

        public override RetVal Visit(CallNode node, Context context)
        {
            var funcName = node.Function.Name;
            var delegableArgs = ConvertArgsToDelegableArgs(node, context);

            if(delegableArgs == null)
            {
                return new RetVal(node, false);
            }

            if (funcName == BuiltinFunctionsCore.And.Name ||
                funcName == BuiltinFunctionsCore.Filter.Name)
            {
                var andNode = _hooks.MakeAndCall(_callerReturnType, delegableArgs);
                return new RetVal(andNode, true);
            }
            else if (funcName == BuiltinFunctionsCore.Or.Name)
            {
                var orNode = _hooks.MakeOrCall(_callerReturnType, delegableArgs);
                return new RetVal(orNode, true);
            }
            else if (funcName == BuiltinFunctionsCore.FirstN.Name)
            {
                if (node.Args.Count == 2)
                {
                    //var newNode = _hooks.MakeTopCall(tableArg, node.Args[1]);
                    //return Ret(newNode);
                }
            }

            return new RetVal(node, false);
        }

        public IList<IntermediateNode> ConvertArgsToDelegableArgs(CallNode node, Context context)
        {
            var isFuncDelegable = true;
            var delegableArgs = new IntermediateNode[node.Args.Count];
            for (var i = 0; i < node.Args.Count && isFuncDelegable; i++)
            {
                var arg = node.Args[i];
                var retVal = arg.Accept(this, context);
                isFuncDelegable = isFuncDelegable && retVal.isDelegable;
                delegableArgs[i] = retVal.node;
            }

            var result = isFuncDelegable ? delegableArgs : null;
            return result;
        }

        public override RetVal Visit(BinaryOpNode node, Context context)
        {
            var left = node.Left.Accept(this, context);
            var right = node.Right.Accept(this, context);

            if(!(left.isDelegable || right.isDelegable))
            {
                return new RetVal(node, false);
            }

            // Either left or right is field (not both)
            if (!TryGetFieldName(left.node, right.node, out var fieldName, out var rightNode))
            {
                return new RetVal(node, false);
            }

            switch (node.Op)
            {
                case BinaryOpKind.EqNumbers:
                case BinaryOpKind.EqBoolean:
                case BinaryOpKind.EqText:
                case BinaryOpKind.EqDate:
                case BinaryOpKind.EqTime:
                case BinaryOpKind.EqDateTime:
                case BinaryOpKind.EqCurrency: // ?
                case BinaryOpKind.EqGuid:
                case BinaryOpKind.EqNull:
                case BinaryOpKind.EqDecimals:
                    var eqNode = _hooks.MakeEqCall(_callerReturnType, fieldName, rightNode);
                    return new RetVal(eqNode, true);
                case BinaryOpKind.LtNumbers:
                case BinaryOpKind.LtDecimals:
                case BinaryOpKind.LtDateTime:
                case BinaryOpKind.LtDate:
                case BinaryOpKind.LtTime:
                    var ltNode = _hooks.MakeLtCall(_callerReturnType, fieldName, rightNode);
                    return new RetVal(ltNode, true);
                case BinaryOpKind.LeqNumbers:
                case BinaryOpKind.LeqDecimals:
                case BinaryOpKind.LeqDateTime:
                case BinaryOpKind.LeqDate:
                case BinaryOpKind.LeqTime:
                    var leqNode = _hooks.MakeLeqCall(_callerReturnType, fieldName, rightNode);
                    return new RetVal(leqNode, true);
                case BinaryOpKind.GtNumbers:
                case BinaryOpKind.GtDecimals:
                case BinaryOpKind.GtDateTime:
                case BinaryOpKind.GtDate:
                case BinaryOpKind.GtTime:
                    var gtNode = _hooks.MakeGtCall(_callerReturnType, fieldName, rightNode);
                    return new RetVal(gtNode, true);
                case BinaryOpKind.GeqNumbers:
                case BinaryOpKind.GeqDecimals:
                case BinaryOpKind.GeqDateTime:
                case BinaryOpKind.GeqDate:
                case BinaryOpKind.GeqTime:
                    var geqNode = _hooks.MakeGeqCall(_callerReturnType, fieldName, rightNode);
                    return new RetVal(geqNode, true);
                default:
                    return new RetVal(node, false);
            }
        }

        public override RetVal Visit(UnaryOpNode node, Context context)
        {
            return new RetVal(node, false);
        }

        public override RetVal Visit(ScopeAccessNode node, Context context)
        {
            if (node.Value is ScopeAccessSymbol x)
            {
                if (x.Parent.Id == _caller.Scope.Id)
                {
                    return new RetVal(node, true);
                }
            }

            return new RetVal(node, false);
        }

        public override RetVal Visit(RecordFieldAccessNode node, Context context)
        {
            return new RetVal(node, false);
        }

        public override RetVal Visit(ResolvedObjectNode node, Context context)
        {
            return new RetVal(node, false);
        }

        public override RetVal Visit(SingleColumnTableAccessNode node, Context context)
        {
            return new RetVal(node, false);
        }

        public override RetVal Visit(ChainingNode node, Context context)
        {
            foreach (var child in node.Nodes)
            {
                var ret = child.Accept(this, context);
                if (ret != null)
                {
                    return ret;
                }
            }

            return new RetVal(node, false);
        }

        public override RetVal Visit(AggregateCoercionNode node, Context context)
        {
            var ret2 = node.Child.Accept(this, context);
            if (ret2 != null)
            {
                return ret2;
            }

            return new RetVal(node, false);
        }
    }
}