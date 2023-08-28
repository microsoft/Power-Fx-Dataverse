using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;
using BinaryOpNode = Microsoft.PowerFx.Core.IR.Nodes.BinaryOpNode;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;
using PowerFxStringResources = Microsoft.PowerFx.Core.Localization.StringResources;
using Span = Microsoft.PowerFx.Syntax.Span;

namespace Microsoft.PowerFx.Dataverse
{
    // Rewrite the tree to inject delegation.
    // If we encounter a dataverse table (something that should be delegated) during the walk, we either:
    // - successfully delegate, which means rewriting to a call an efficient DelegatedFunction,
    // - leave IR unchanged (don't delegate), but issue a warning. 
    internal class PredicateVisitor : RewritingIRVisitor<PredicateVisitor.RetVal, PredicateVisitor.Context>
    {
        // Ideally, this would just be in Dataverse.Eval nuget, but 
        // Only Dataverse nuget has InternalsVisisble access to implement an IR walker. 
        // So implement the walker in lower layer, and have callbacks into Dataverse.Eval layer as needed. 
        private readonly DelegationHooks _hooks;
        private readonly int _maxRows;

        // For reporting delegation Warnings. 
        private readonly ICollection<ExpressionError> _errors;

        private readonly CallNode _caller;

        private readonly ResolvedObjectNode _callerSourceTable;

        private readonly ScopeSymbol _callerScope;

        private readonly FormulaType _callerReturnType;

        public PredicateVisitor(DelegationHooks hooks, ICollection<ExpressionError> errors, int maxRow, CallNode caller, ResolvedObjectNode callerTable)
        {
            _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
            _errors = errors ?? throw new ArgumentNullException(nameof(errors));

            _caller = caller ?? throw new ArgumentNullException(nameof(caller));
            _maxRows = maxRow;
            _callerScope = caller.Scope;
            _callerReturnType = caller.IRContext.ResultType;
            _callerSourceTable = callerTable;
        }

        // Return Value passed through at each phase of the walk. 
        public class RetVal
        {
            public readonly IntermediateNode filter;
            public readonly IntermediateNode topCount;

            // Original IR node for non-delegating path.
            // This should always be set. Even if we are attempting to delegate, we may need to use this if we can't support the delegation. 
            public readonly IntermediateNode _originalNode;

            // If set, we're attempting to delegate the current expression specifeid by _node.
            public bool IsDelegating => filter != null;

            public RetVal(IntermediateNode originalNode, IntermediateNode filter, IntermediateNode count)
            {
                if (originalNode == null)
                {
                    throw new ArgumentNullException();
                }
                _originalNode = originalNode;
                this.filter = filter; 
                this.topCount = count;
            }

            // Non-delegating path 
            public RetVal(IntermediateNode node)
            {
                _originalNode = node;
            }
        }

        public class Context
        {
            public bool _ignoreDelegation;
        }

        // If an attempted delegation can't be complete, then fail it. 
        private void AddError(ExpressionError error)
        {
            _errors.Add(error);
        }

        public bool TryGetFieldName(IntermediateNode left, IntermediateNode right, BinaryOpKind op, out string fieldName, out IntermediateNode node, out BinaryOpKind opKind)
        {
            if (TryGetFieldName(left, out var leftField) && !TryGetFieldName(right, out _))
            {
                fieldName = leftField;
                node = right;
                opKind = op;
                return true;
            }
            else if (TryGetFieldName(right, out var rightField) && !TryGetFieldName(left, out _))
            {
                fieldName = rightField;
                node = left;
                if(InvertLeftRight(op, out var invertedOp))
                {
                    opKind = invertedOp;
                    return true;
                }
                else
                {
                    opKind = default;
                    return false;
                }
            }
            else if(TryGetFieldName(left, out var leftField2) && TryGetFieldName(right, out var rightField2))
            {
                if(leftField2 == rightField2)
                {
                    // Issue warning
                    var min = left.IRContext.SourceContext.Lim;
                    var lim = right.IRContext.SourceContext.Min;
                    var span = new Span(min, lim);
                    var reason = new ExpressionError()
                    {
                        Span = span,
                        Severity = ErrorSeverity.Warning,
                        ResourceKey = TexlStrings.WrnDelegationPredicate
                    };
                    this.AddError(reason);

                    opKind = op;
                    fieldName = default;
                    node = default;
                    return false;
                }
            }

            opKind = op;
            node = default;
            fieldName = default;
            return false;
        }

        private bool InvertLeftRight(BinaryOpKind op, out BinaryOpKind invertedOp)
        {
            switch (op)
            {
                case BinaryOpKind.LtNumbers:
                    invertedOp = BinaryOpKind.GtNumbers;
                    return true;
                case BinaryOpKind.LtDecimals: 
                    invertedOp = BinaryOpKind.GtDecimals;
                    return true;
                case BinaryOpKind.LtDateTime: 
                    invertedOp = BinaryOpKind.GtDateTime;
                    return true;
                case BinaryOpKind.LtDate: 
                    invertedOp = BinaryOpKind.GtDate;
                    return true;
                case BinaryOpKind.LtTime: 
                    invertedOp = BinaryOpKind.GtTime;
                    return true;
                case BinaryOpKind.LeqNumbers: 
                    invertedOp = BinaryOpKind.GeqNumbers;
                    return true;
                case BinaryOpKind.LeqDecimals: 
                    invertedOp = BinaryOpKind.GeqDecimals;
                    return true;
                case BinaryOpKind.LeqDateTime: 
                    invertedOp = BinaryOpKind.GeqDateTime;
                    return true;
                case BinaryOpKind.LeqDate: 
                    invertedOp = BinaryOpKind.GeqDate;
                    return true;
                case BinaryOpKind.LeqTime: 
                    invertedOp = BinaryOpKind.GeqTime;
                    return true;
                case BinaryOpKind.GtNumbers: 
                    invertedOp = BinaryOpKind.LtNumbers;
                    return true;
                case BinaryOpKind.GtDecimals: 
                    invertedOp = BinaryOpKind.LtDecimals;
                    return true;
                case BinaryOpKind.GtDateTime: 
                    invertedOp = BinaryOpKind.LtDateTime;
                    return true;
                case BinaryOpKind.GtDate: 
                    invertedOp = BinaryOpKind.LtDate;
                    return true;
                case BinaryOpKind.GtTime: 
                    invertedOp = BinaryOpKind.LtTime;
                    return true;
                case BinaryOpKind.GeqNumbers: 
                    invertedOp = BinaryOpKind.LeqNumbers;
                    return true;
                case BinaryOpKind.GeqDecimals: 
                    invertedOp = BinaryOpKind.LeqDecimals;
                    return true;
                case BinaryOpKind.GeqDateTime: 
                    invertedOp = BinaryOpKind.LeqDateTime;
                    return true;
                case BinaryOpKind.GeqDate: 
                    invertedOp = BinaryOpKind.LeqDate;
                    return true;
                case BinaryOpKind.GeqTime: 
                    invertedOp = BinaryOpKind.LeqTime;
                    return true;
                default:
                    invertedOp = default;
                    return false;
            }
        }

        public bool TryGetFieldName(IntermediateNode node, out string fieldName)
        {
            IntermediateNode maybeScopeAccessNode;

            // If the node had injected float coercion, then we need to pull scope access node from it.
            if(node is CallNode maybeFloat && maybeFloat.Function == BuiltinFunctionsCore.Float)
            {
                maybeScopeAccessNode = maybeFloat.Args[0];
            }
            else
            {
                maybeScopeAccessNode = node;
            }

            if (maybeScopeAccessNode is ScopeAccessNode scopeAccessNode)
            {
                if (scopeAccessNode.Value is ScopeAccessSymbol scopeAccessSymbol)
                {
                    var callerId = _callerScope.Id;
                    if (scopeAccessSymbol.Parent.Id == callerId)
                    {
                        fieldName = scopeAccessSymbol.Name;
                        return true;
                    }
                }
            }

            fieldName = default;
            return false;
        }

        public override IntermediateNode Materialize(RetVal ret)
        {
            return ret._originalNode;
        }

        protected override RetVal Ret(IntermediateNode node)
        {
            return new RetVal(node);
        }

        // BinaryOpNode can be only materialized when called via CallNode.
        public override RetVal Visit(BinaryOpNode node, Context context)
        {
            TableType tableType;
            if (_callerReturnType is RecordType recordType)
            {
                tableType = recordType.ToTable();
            }
            else
            {
                tableType = (TableType)_callerReturnType;
            }

            // Either left or right is field (not both)
            if (!TryGetFieldName(node.Left, node.Right, node.Op, out var fieldName, out var rightNode, out var operation))
            {
                return new RetVal(node);
            }

            var findThisRecord = ThisRecordIRVisitor.FindThisRecordUsage(_caller, rightNode);
            if (findThisRecord != null)
            {
                CreateThisRecordErrorAndReturn(_caller, findThisRecord);
            }

            var findBehaviorFunc = BehaviorIRVisitor.Find(rightNode);
            if (findBehaviorFunc != null)
            {
                return CreateBehaviorErrorAndReturn(_caller, findBehaviorFunc);
            }

            var delegationVisitor = new DelegationIRVisitor(_hooks, _errors, _maxRows);
            var delegationContext = new DelegationIRVisitor.Context();
            var retDelegationVisitor = rightNode.Accept(delegationVisitor, delegationContext);
            if (retDelegationVisitor.IsDelegating)
            {
                rightNode = delegationVisitor.Materialize(retDelegationVisitor);
            }
            else
            {
                rightNode = retDelegationVisitor._originalNode;
            }

            RetVal ret;

            // Money can't be delegated, tracks the issue https://github.com/microsoft/Power-Fx-Dataverse/issues/238
            if(TryDisableMoneyComaprison(node, fieldName, out var result))
            {
                return result;
            }

            switch (operation)
            {
                case BinaryOpKind.EqNumbers:
                case BinaryOpKind.EqBoolean:
                case BinaryOpKind.EqText:
                case BinaryOpKind.EqDate:
                case BinaryOpKind.EqTime:
                case BinaryOpKind.EqDateTime:
                case BinaryOpKind.EqGuid:
                case BinaryOpKind.EqDecimals:
                case BinaryOpKind.EqCurrency:
                    var eqNode = _hooks.MakeEqCall(_callerSourceTable, tableType, fieldName, operation, rightNode, _callerScope);
                    ret = new RetVal(node, eqNode, count: null);
                    return ret;
                case BinaryOpKind.NeqNumbers:
                case BinaryOpKind.NeqBoolean:
                case BinaryOpKind.NeqText:
                case BinaryOpKind.NeqDate:
                case BinaryOpKind.NeqTime:
                case BinaryOpKind.NeqDateTime:
                case BinaryOpKind.NeqGuid:
                case BinaryOpKind.NeqDecimals:
                case BinaryOpKind.NeqCurrency:
                    var neqNode = _hooks.MakeNeqCall(_callerSourceTable, tableType, fieldName, operation, rightNode, _callerScope);
                    ret = new RetVal(node, neqNode, count: null);
                    return ret;
                case BinaryOpKind.LtNumbers:
                case BinaryOpKind.LtDecimals:
                case BinaryOpKind.LtDateTime:
                case BinaryOpKind.LtDate:
                case BinaryOpKind.LtTime:
                    var ltNode = _hooks.MakeLtCall(_callerSourceTable, tableType, fieldName, operation, rightNode, _callerScope);
                    ret = new RetVal(node, ltNode, count: null);
                    return ret;
                case BinaryOpKind.LeqNumbers:
                case BinaryOpKind.LeqDecimals:
                case BinaryOpKind.LeqDateTime:
                case BinaryOpKind.LeqDate:
                case BinaryOpKind.LeqTime:
                    var leqNode = _hooks.MakeLeqCall(_callerSourceTable, tableType, fieldName, operation, rightNode, _callerScope);
                    ret = new RetVal(node, leqNode, count: null);
                    return ret;
                case BinaryOpKind.GtNumbers:
                case BinaryOpKind.GtDecimals:
                case BinaryOpKind.GtDateTime:
                case BinaryOpKind.GtDate:
                case BinaryOpKind.GtTime:
                    var gtNode = _hooks.MakeGtCall(_callerSourceTable, tableType, fieldName, operation, rightNode, _callerScope);
                    ret = new RetVal(node, gtNode, count: null);
                    return ret;
                case BinaryOpKind.GeqNumbers:
                case BinaryOpKind.GeqDecimals:
                case BinaryOpKind.GeqDateTime:
                case BinaryOpKind.GeqDate:
                case BinaryOpKind.GeqTime:
                    var geqNode = _hooks.MakeGeqCall(_callerSourceTable, tableType, fieldName, operation, rightNode, _callerScope);
                    ret = new RetVal(node, geqNode, count: null);
                    return ret;
                default:
                    return new RetVal(node);
            }
        }

        // Used to stop Money delegation, tracks the issue https://github.com/microsoft/Power-Fx-Dataverse/issues/238
        private bool TryDisableMoneyComaprison(BinaryOpNode node, string fieldName, out RetVal result)
        {
            var callerTableType = (TableType)_callerSourceTable.IRContext.ResultType;
            var tableDS = callerTableType._type.AssociatedDataSources.FirstOrDefault();
            EntityMetadata metadata = null;
            if (tableDS != null)
            {
                var tableLogicalName = tableDS.TableMetadata.Name; // logical name
                if (tableDS.DataEntityMetadataProvider is CdsEntityMetadataProvider m2)
                {
                    if (!m2.TryGetXrmEntityMetadata(tableLogicalName, out metadata))
                    {
                        throw new InvalidOperationException($"Meta-data not found for table: {tableLogicalName}");
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Meta-data provider should be CDS");
                }
            }
            else
            {
                throw new InvalidOperationException($"Table type should have data source");
            }

            if (!metadata.TryGetAttribute(fieldName, out var attributeMetadata))
            {
                throw new InvalidOperationException($"Meta-data not found for field: {fieldName}");
            }

            if (attributeMetadata.AttributeType == AttributeTypeCode.Money)
            {
                result = new RetVal(node);
                return true;
            }

            result = default;
            return false;
        }

        public override RetVal Visit(CallNode node, Context context)
        {
            var func = node.Function.Name;
            if (func == BuiltinFunctionsCore.And.Name)
            {
                return ProcessAnd(node, context);
            }
            else if (func == BuiltinFunctionsCore.Or.Name)
            {
                return ProcessOr(node, context);
            }

            var delegationVisitor = new DelegationIRVisitor(_hooks, _errors, _maxRows);
            var delegationContext = new DelegationIRVisitor.Context();
            var retDelegationVisitor = node.Accept(delegationVisitor, delegationContext);
            var res = delegationVisitor.Materialize(retDelegationVisitor);
            return new RetVal(res);
        }

        public override RetVal Visit(LazyEvalNode node, Context context)
        {
            var child = node.Child.Accept(this, context);

            if (child.IsDelegating)
            {
                return child;
            }
            else
            {
                return Ret(node);
            }
        }

        private RetVal ProcessAndOr(CallNode node, Context context, Func<IList<IntermediateNode>, IntermediateNode> filterDelegate)
        {
            bool isDelegating = true;
            List<IntermediateNode> delegatedChild = new();

            foreach (var arg in node.Args)
            {
                var delegatedArg = arg is LazyEvalNode lazyEvalNode
                ? lazyEvalNode.Child.Accept(this, context)
                : arg.Accept(this, context);

                if (delegatedArg.IsDelegating)
                {
                    delegatedChild.Add(delegatedArg.filter);
                }
                else
                {
                    isDelegating = false;
                    break;
                }
            }

            if (isDelegating)
            {
                var filter = filterDelegate(delegatedChild);
                var rVal = new RetVal(node, filter, count: null);
                return rVal;
            }

            return new RetVal(node);
        }

        public RetVal ProcessOr(CallNode node, Context context)
        {
            return ProcessAndOr(node, context, delegatedChildren => _hooks.MakeOrCall(_callerReturnType, delegatedChildren, node.Scope));
        }

        public RetVal ProcessAnd(CallNode node, Context context)
        {
            return ProcessAndOr(node, context, delegatedChildren => _hooks.MakeAndCall(_callerReturnType, delegatedChildren, node.Scope));
        }

        private RetVal CreateBehaviorErrorAndReturn(CallNode node, BehaviorIRVisitor.RetVal findBehaviorFunc)
        {
            var reason = new ExpressionError()
            {
                MessageArgs = new object[] { node.Function.Name, findBehaviorFunc.Name },
                Span = findBehaviorFunc.Span,
                Severity = ErrorSeverity.Warning,
                ResourceKey = TexlStrings.WrnDelegationBehaviorFunction
            };

            AddError(reason);
            return new RetVal(node);
        }

        private RetVal CreateThisRecordErrorAndReturn(CallNode node, ThisRecordIRVisitor.RetVal findThisRecord)
        {
            var reason = new ExpressionError()
            {
                MessageArgs = new object[] { _caller.Function.Name },
                Span = findThisRecord.Span,
                Severity = ErrorSeverity.Warning,
                ResourceKey = TexlStrings.WrnDelegationRefersThisRecord
            };

            AddError(reason);
            return new RetVal(node);
        }
    }
}
