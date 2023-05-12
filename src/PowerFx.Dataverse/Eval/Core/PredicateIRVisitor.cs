using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Drawing;
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
    internal class PredicateIRVisitor : IRNodeVisitor<QueryExpressionHelper, PredicateIRVisitor.Context>
    {
        private DelegationHooks _hooks;
        private CallNode _caller;
        private readonly ICollection<ExpressionError> _errors;

        private FormulaType _callerReturnType
        {
            get { return _caller.IRContext.ResultType; }
        }

        public PredicateIRVisitor(CallNode caller, DelegationHooks hooks, ICollection<ExpressionError> errors)
        {
            if (caller == null || hooks == null)
            {
                throw new ArgumentNullException();
            }

            _hooks = hooks;
            _caller = caller;
            _errors = errors;
        }

        private void AddError(ExpressionError error)
        {
            _errors.Add(error);
        }

        public class QueryExpressionHelper
        {
            public bool CanGenerateQuery;
            public IntermediateNode node;
            internal int? _top;

            public QueryExpressionHelper(IntermediateNode node, bool isDelegable, int? top = null)
            {
                this.node = node;
                this.CanGenerateQuery = isDelegable;
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

        public override QueryExpressionHelper Visit(TextLiteralNode node, Context context)
        {
            return new QueryExpressionHelper(node, false); // Leaf
        }

        public override QueryExpressionHelper Visit(NumberLiteralNode node, Context context)
        {
            return new QueryExpressionHelper(node, false); // Leaf
        }

        public override QueryExpressionHelper Visit(DecimalLiteralNode node, Context context)
        {
            return new QueryExpressionHelper(node, false); // Leaf
        }

        public override QueryExpressionHelper Visit(BooleanLiteralNode node, Context context)
        {
            return new QueryExpressionHelper(node, false); // Leaf
        }

        public override QueryExpressionHelper Visit(ColorLiteralNode node, Context context)
        {
            return new QueryExpressionHelper(node, false); // Leaf
        }

        public override QueryExpressionHelper Visit(RecordNode node, Context context)
        {
            return new QueryExpressionHelper(node, false);
        }

        public override QueryExpressionHelper Visit(ErrorNode node, Context context)
        {
            return new QueryExpressionHelper(node, false); // Leaf
        }

        public override QueryExpressionHelper Visit(LazyEvalNode node, Context context)
        {
            return node.Child.Accept(this, context);
        }

        public override QueryExpressionHelper Visit(CallNode node, Context context)
        {
            var funcName = node.Function.Name;

            var delegableArgs = ConvertArgsToDelegableArgs(node, context);

            if(delegableArgs == null)
            {
                return new QueryExpressionHelper(node, false);
            }

            if (funcName == BuiltinFunctionsCore.And.Name ||
                funcName == BuiltinFunctionsCore.Filter.Name)
            {
                var andNode = _hooks.MakeAndCall(_callerReturnType, delegableArgs);
                return new QueryExpressionHelper(andNode, true);
            }
            else if (funcName == BuiltinFunctionsCore.Or.Name)
            {
                var orNode = _hooks.MakeOrCall(_callerReturnType, delegableArgs);
                return new QueryExpressionHelper(orNode, true);
            }
            else if (funcName == BuiltinFunctionsCore.LookUp.Name)
            {
                var orNode = _hooks.MakeOrCall(_callerReturnType, delegableArgs);
                return new QueryExpressionHelper(orNode, true);
            }

            return new QueryExpressionHelper(node, false);
        }

        public IList<IntermediateNode> ConvertArgsToDelegableArgs(CallNode node, Context context)
        {
            var isFuncDelegable = true;
            var delegableArgs = new IntermediateNode[node.Args.Count];
            for (var i = 0; i < node.Args.Count && isFuncDelegable; i++)
            {
                var arg = node.Args[i];
                var retVal = arg.Accept(this, context);
                isFuncDelegable = isFuncDelegable && retVal.CanGenerateQuery;
                delegableArgs[i] = retVal.node;
            }

            var result = isFuncDelegable ? delegableArgs : null;
            return result;
        }

        public override QueryExpressionHelper Visit(BinaryOpNode node, Context context)
        {
            var left = node.Left.Accept(this, context);
            var right = node.Right.Accept(this, context);

            if(!(left.CanGenerateQuery || right.CanGenerateQuery))
            {
                // $$$ Maybe we can leverage? 
                //var findThisRecordLeft = ThisRecordIRVisitor.FindThisRecordUsage(_caller, left.node);
                //var findThisRecordRight = ThisRecordIRVisitor.FindThisRecordUsage(_caller, right.node);
                //if (findThisRecordLeft == null && findThisRecordRight == null)
                //{
                //    var blankFilter = _hooks.MakeBlankFilterCall(_callerReturnType);
                //    return new QueryExpressionHelper(blankFilter, true);
                //}
                return new QueryExpressionHelper(node, false);
            }

            // Either left or right is field (not both)
            if (!TryGetFieldName(left.node, right.node, out var fieldName, out var rightNode))
            {
                return new QueryExpressionHelper(node, false);
            }

            var findThisRecord = ThisRecordIRVisitor.FindThisRecordUsage(_caller, rightNode);
            if (findThisRecord != null)
            {
                var reason = new ExpressionError
                {
                    MessageKey = "WrnDelagationRefersThisRecord",
                    MessageArgs = new object[] { _caller.Function.Name },
                    Span = findThisRecord.Span,
                    Severity = ErrorSeverity.Warning
                };
                AddError(reason);
                return new QueryExpressionHelper(node, false);
            }

            var findBehaviorFunc = BehaviorIRVisitor.Find(rightNode);
            if (findBehaviorFunc != null)
            {
                var reason = new ExpressionError
                {
                    MessageKey = "WrnDelagationBehaviorFunction",
                    MessageArgs = new object[] { _caller.Function.Name, findBehaviorFunc.Name },
                    Span = findBehaviorFunc.Span,
                    Severity = ErrorSeverity.Warning
                };
                AddError(reason);
                return new QueryExpressionHelper(node, false);
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
                    return new QueryExpressionHelper(eqNode, true);
                case BinaryOpKind.LtNumbers:
                case BinaryOpKind.LtDecimals:
                case BinaryOpKind.LtDateTime:
                case BinaryOpKind.LtDate:
                case BinaryOpKind.LtTime:
                    var ltNode = _hooks.MakeLtCall(_callerReturnType, fieldName, rightNode);
                    return new QueryExpressionHelper(ltNode, true);
                case BinaryOpKind.LeqNumbers:
                case BinaryOpKind.LeqDecimals:
                case BinaryOpKind.LeqDateTime:
                case BinaryOpKind.LeqDate:
                case BinaryOpKind.LeqTime:
                    var leqNode = _hooks.MakeLeqCall(_callerReturnType, fieldName, rightNode);
                    return new QueryExpressionHelper(leqNode, true);
                case BinaryOpKind.GtNumbers:
                case BinaryOpKind.GtDecimals:
                case BinaryOpKind.GtDateTime:
                case BinaryOpKind.GtDate:
                case BinaryOpKind.GtTime:
                    var gtNode = _hooks.MakeGtCall(_callerReturnType, fieldName, rightNode);
                    return new QueryExpressionHelper(gtNode, true);
                case BinaryOpKind.GeqNumbers:
                case BinaryOpKind.GeqDecimals:
                case BinaryOpKind.GeqDateTime:
                case BinaryOpKind.GeqDate:
                case BinaryOpKind.GeqTime:
                    var geqNode = _hooks.MakeGeqCall(_callerReturnType, fieldName, rightNode);
                    return new QueryExpressionHelper(geqNode, true);
                default:
                    return new QueryExpressionHelper(node, false);
            }
        }

        public override QueryExpressionHelper Visit(UnaryOpNode node, Context context)
        {
            return new QueryExpressionHelper(node, false);
        }

        public override QueryExpressionHelper Visit(ScopeAccessNode node, Context context)
        {
            if (node.Value is ScopeAccessSymbol x)
            {
                if (x.Parent.Id == _caller.Scope.Id)
                {
                    return new QueryExpressionHelper(node, true);
                }
            }

            return new QueryExpressionHelper(node, false);
        }

        public override QueryExpressionHelper Visit(RecordFieldAccessNode node, Context context)
        {
            return new QueryExpressionHelper(node, false);
        }

        public override QueryExpressionHelper Visit(ResolvedObjectNode node, Context context)
        {
            return new QueryExpressionHelper(node, false);
        }

        public override QueryExpressionHelper Visit(SingleColumnTableAccessNode node, Context context)
        {
            return new QueryExpressionHelper(node, false);
        }

        public override QueryExpressionHelper Visit(ChainingNode node, Context context)
        {
            return new QueryExpressionHelper(node, false);
        }

        public override QueryExpressionHelper Visit(AggregateCoercionNode node, Context context)
        {
            var ret2 = node.Child.Accept(this, context);
            if (ret2 != null)
            {
                return ret2;
            }

            return new QueryExpressionHelper(node, false);
        }
    }
}