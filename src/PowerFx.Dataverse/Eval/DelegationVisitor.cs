using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
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
using Span = Microsoft.PowerFx.Syntax.Span;

namespace Microsoft.PowerFx.Dataverse
{
    // Rewrite the tree to inject delegation.
    // If we encounter a dataverse table (something that should be delegated) during the walk, we either:
    // - successfully delegate, which means rewriting to a call an efficient DelegatedFunction,
    // - leave IR unchanged (don't delegate), but issue a warning. 
    internal class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        // Ideally, this would just be in Dataverse.Eval nuget, but 
        // Only Dataverse nuget has InternalsVisisble access to implement an IR walker. 
        // So implement the walker in lower layer, and have callbacks into Dataverse.Eval layer as needed. 
        private readonly DelegationHooks _hooks;
        private readonly int _maxRows;

        // For reporting delegation Warnings. 
        private readonly ICollection<ExpressionError> _errors;

        // $$$ lock?
        private readonly Stack<CallNode> _caller;

        private ScopeSymbol GetCallerScope()
        {
            if (_caller.Count == 0)
            {
                throw new InvalidOperationException("No caller");
            }

            return _caller.Peek().Scope;
        }

        private FormulaType GetCallerReturnType()
        {
            if (_caller.Count == 0)
            {
                throw new InvalidOperationException("No caller");
            }

            return _caller.Peek().IRContext.ResultType;
        }

        public DelegationIRVisitor(DelegationHooks hooks, ICollection<ExpressionError> errors, int maxRow)
        {
            _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
            _errors = errors ?? throw new ArgumentNullException(nameof(errors));
            _maxRows = maxRow;
            _caller = new Stack<CallNode>();
        }

        // Return Value passed through at each phase of the walk. 
        public class RetVal
        {
            public readonly IntermediateNode filter;
            public readonly IExternalTabularDataSource tableDS;
            public readonly IntermediateNode topCount;
            public readonly string tableLogicalName;
            public readonly DelegationHooks _hooks;

            // Original IR node for non-delegating path.
            // This should always be set. Even if we are attempting to delegate, we may need to use this if we can't support the delegation. 
            public readonly IntermediateNode _node;

            // If set, we're attempting to delegate the current expression specifeid by _node.
            public bool IsDelegating => _metadata != null;
                        
            
            // IR node that will resolve to the TableValue at runtime. 
            // From here, we can downcast at get the services. 
            public readonly IntermediateNode _sourceTableIRNode;

            // Table type  and original metadata for table that we're delegating to. 
            public readonly TableType _tableType;

            public readonly EntityMetadata _metadata;

            public RetVal(IntermediateNode node, IntermediateNode tableIRNode, TableType tableType, IExternalTabularDataSource tableDS, IntermediateNode filter, IntermediateNode count)
            {
                if (tableDS == null || tableType == null || node == null)
                {
                    throw new ArgumentNullException();
                }

                count ??= new CallNode(IRContext.NotInSource(FormulaType.Blank), BuiltinFunctionsCore.Blank);

                filter ??= _hooks.MakeBlankFilterCall(tableType);

                _sourceTableIRNode = tableIRNode;
                _tableType = tableType;
                _node = node;
                this.filter = filter;
                this.tableDS = tableDS;
                this.topCount = count;

                this.tableLogicalName = tableDS.TableMetadata.Name; // logical name
                if (tableDS.DataEntityMetadataProvider is CdsEntityMetadataProvider m2)
                {
                    if (m2.TryGetXrmEntityMetadata(tableLogicalName, out var metadata))
                    {
                        this._metadata = metadata;
                    }
                }

            }

            // Non-delegating path 
            public RetVal(IntermediateNode node)
            {
                _node = node;
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
            else if(TryGetFieldName(left, out var leftField2) && TryGetFieldName(right, out var rightField2))
            {
                if(leftField2 == rightField2)
                {
                    // Issue warning
                    var min = left.IRContext.SourceContext.Lim;
                    var lim = right.IRContext.SourceContext.Min;
                    var span = new Span(min, lim);
                    var reason = new ExpressionError
                    {
                        MessageKey = "WrnDelagationPredicate",
                        Span = span,
                        Severity = ErrorSeverity.Warning
                    };
                    this.AddError(reason);

                    fieldName = default;
                    node = default;
                    return false;
                }
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

                    var callerId = GetCallerScope().Id;
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

        // Does this match:
        //    primaryKey=value
        private bool MatchPrimaryId(IntermediateNode primaryIdField, IntermediateNode value, RetVal tableArg)
        {
            if (primaryIdField is ScopeAccessNode left1)
            {
                if (left1.Value is ScopeAccessSymbol s)
                {
                    var fieldName = s.Name;
                    if (fieldName == tableArg._metadata.PrimaryIdAttribute)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // Normalize order? (Id=Guid) vs (Guid=Id)
        private bool TryMatchPrimaryId(IntermediateNode left, IntermediateNode right, out IntermediateNode primaryIdField, out IntermediateNode guidValue, RetVal tableArg)
        {
            if (MatchPrimaryId(left, right, tableArg))
            {
                primaryIdField = left;
                guidValue = right;
                return true;
            }
            else if (MatchPrimaryId(right, left, tableArg))
            {
                primaryIdField = right;
                guidValue = left;
                return true;
            }

            primaryIdField = null;
            guidValue = null;
            return false;
        }

        // If RetVal just represent a table, then ok. 
        // If it's any other in-progress delegation, then it's a warning. 
        private RetVal MaterializeTableOnly(RetVal ret)
        {
            // IsBlank(table) // ok
            // IsBlank(Filter(table,true)) // warning

            return new RetVal(ret._node);
        }

        public override IntermediateNode Materialize(RetVal ret)
        {
            if (ret.IsDelegating && ret._sourceTableIRNode != null)
            {
                var res = _hooks.MakeQueryExecutorCall(ret);
                return res;
            }

            return ret._node;            
        }

        protected override RetVal Ret(IntermediateNode node)
        {
            return new RetVal(node);
        }

        // ResolvedObject is a symbol injected by the host.
        // All Table references start as resolved objects. 
        public override RetVal Visit(ResolvedObjectNode node, Context context)
        {
            if (!context._ignoreDelegation && node.IRContext.ResultType is TableType aggType)
            {
                // Does the resolve object refer to a dataverse Table?

                if (node.Value is NameSymbol nameSym)
                {
                    var symbolTable = nameSym.Owner;

                    // We need to tell the difference between a direct table, 
                    // and another global variable that has that table's type (such as global := Filter(table, true). 
                    bool isRealTable = _hooks.IsDelegableSymbolTable(symbolTable);
                    if (isRealTable)
                    {
                        var type = aggType._type;

                        // Verify type match 
                        var ads = type.AssociatedDataSources.FirstOrDefault();
                        if (ads != null)
                        {
                            var filter = _hooks.MakeBlankFilterCall(aggType);
                            var ret = new RetVal(node, node, aggType, ads, filter, count: null);
                            return ret;
                        }
                    }
                }
            }   

            // Just a regular variable, don't bother delegating. 
            return Ret(node);
        }

        // BinaryOpNode can be only materialized when called via CallNode.
        public override RetVal Visit(BinaryOpNode node, Context context)
        {
            if (_caller.Count == 0)
            {
                return new RetVal(node);
            }

            var caller = _caller.Peek();

            var callerReturnType = GetCallerReturnType();
            var callerScope = GetCallerScope();
            var ads = callerReturnType._type.AssociatedDataSources.FirstOrDefault();

            // $$$ check aggregate
            TableType tableType;
            if (callerReturnType is RecordType recordType)
            {
                tableType = recordType.ToTable();
            }
            else
            {
                tableType = (TableType)callerReturnType;
            }

            // Either left or right is field (not both)
            if (!TryGetFieldName(node.Left, node.Right, out var fieldName, out var rightNode))
            {
                return new RetVal(node);
            }

            var findThisRecord = ThisRecordIRVisitor.FindThisRecordUsage(caller, rightNode);
            if (findThisRecord != null)
            {
                var reason = new ExpressionError
                {
                    MessageKey = "WrnDelagationRefersThisRecord",
                    MessageArgs = new object[] { caller.Function.Name },
                    Span = findThisRecord.Span,
                    Severity = ErrorSeverity.Warning
                };
                AddError(reason);
                return new RetVal(node);
            }

            var findBehaviorFunc = BehaviorIRVisitor.Find(rightNode);
            if (findBehaviorFunc != null)
            {
                return CreateBehaviorErrorAndReturn(caller, findBehaviorFunc);
            }

            rightNode = Materialize(rightNode.Accept(this, context));
            RetVal ret;
            switch (node.Op)
            {
                case BinaryOpKind.EqNumbers:
                case BinaryOpKind.EqBoolean:
                case BinaryOpKind.EqText:
                case BinaryOpKind.EqDate:
                case BinaryOpKind.EqTime:
                case BinaryOpKind.EqDateTime:
                case BinaryOpKind.EqGuid:
                case BinaryOpKind.EqDecimals:
                    var eqNode = _hooks.MakeEqCall(tableType, fieldName, rightNode, callerScope);
                    ret = new RetVal(node, null, tableType, ads, eqNode, count: null);
                    return ret;
                case BinaryOpKind.LtNumbers:
                case BinaryOpKind.LtDecimals:
                case BinaryOpKind.LtDateTime:
                case BinaryOpKind.LtDate:
                case BinaryOpKind.LtTime:
                    var ltNode = _hooks.MakeLtCall(tableType, fieldName, rightNode, callerScope);
                    ret = new RetVal(node, null, tableType, ads, ltNode, count: null);
                    return ret;
                case BinaryOpKind.LeqNumbers:
                case BinaryOpKind.LeqDecimals:
                case BinaryOpKind.LeqDateTime:
                case BinaryOpKind.LeqDate:
                case BinaryOpKind.LeqTime:
                    var leqNode = _hooks.MakeLeqCall(tableType, fieldName, rightNode, callerScope);
                    ret = new RetVal(node, null, tableType, ads, leqNode, count: null);
                    return ret;
                case BinaryOpKind.GtNumbers:
                case BinaryOpKind.GtDecimals:
                case BinaryOpKind.GtDateTime:
                case BinaryOpKind.GtDate:
                case BinaryOpKind.GtTime:
                    var gtNode = _hooks.MakeGtCall(tableType, fieldName, rightNode, callerScope);
                    ret = new RetVal(node, null, tableType, ads, gtNode, count: null);
                    return ret;
                case BinaryOpKind.GeqNumbers:
                case BinaryOpKind.GeqDecimals:
                case BinaryOpKind.GeqDateTime:
                case BinaryOpKind.GeqDate:
                case BinaryOpKind.GeqTime:
                    var geqNode = _hooks.MakeGeqCall(tableType, fieldName, rightNode, callerScope);
                    ret = new RetVal(node, null, tableType, ads, geqNode, count: null);
                    return ret;
                default:
                    return new RetVal(node);
            }
        }

        public override RetVal Visit(CallNode node, Context context)
        {
            var func = node.Function.Name;

            // Some functions don't require delegation.
            // Using a table diretly as arg0 here doesn't generate a warning. 
            if (func == "IsBlank" || func == "IsError" || func == "Patch" || func == "Collect")
            {
                RetVal arg0c = node.Args[0].Accept(this, context);

                arg0c = MaterializeTableOnly(arg0c);
                                
                return base.Visit(node, context, arg0c);
            }

            if (node.Args.Count == 0)
            {
                // Delegated functions require arg0 is the table. 
                // So a 0-length args can't delegate.
                return base.Visit(node, context);
            }

            if(func == BuiltinFunctionsCore.And.Name)
            {
                return ProcessAnd(node, context);
            }
            else if(func == BuiltinFunctionsCore.Or.Name)
            {
                return ProcessOr(node, context);
            }

            // Only below function fulfills assumption that first arg is Table
            if(!(node.Function.ParamTypes.Length > 0 && node.Function.ParamTypes[0].IsTable))
            {
                return base.Visit(node, context);
            }

            RetVal tableArg = node.Args[0].Accept(this, context);

            if (!tableArg.IsDelegating)
            {
                return base.Visit(node, context, tableArg);
            }

            _caller.Push(node);
            RetVal ret = func switch
            {
                _ when func == BuiltinFunctionsCore.LookUp.Name => ProcessLookUp(node, context, tableArg),
                _ when func == BuiltinFunctionsCore.Filter.Name => ProcessFilter(node, context, tableArg),
                _ when func == BuiltinFunctionsCore.FirstN.Name => ProcessFirstN(node, tableArg),
                _ => CreateNotSupportedErrorAndReturn(node, tableArg)
            };

            // Other delegating functions, continue to compose...
            // - First, 
            // - Filter
            // - Sort   
            _caller.Pop();
            return ret;
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

            var callerReturnType = GetCallerReturnType();
            var ads = callerReturnType._type.AssociatedDataSources.FirstOrDefault();
            TableType tableType = callerReturnType is RecordType recordType ? recordType.ToTable() : (TableType)callerReturnType;

            if (isDelegating)
            {
                var filter = filterDelegate(delegatedChild);
                var rVal = new RetVal(node, null, tableType, ads, filter, null);
                return rVal;
            }

            return new RetVal(node);
        }

        public RetVal ProcessOr(CallNode node, Context context)
        {
            return ProcessAndOr(node, context, delegatedChildren => _hooks.MakeOrCall(GetCallerReturnType(), delegatedChildren, node.Scope));
        }

        public RetVal ProcessAnd(CallNode node, Context context)
        {
            return ProcessAndOr(node, context, delegatedChildren => _hooks.MakeAndCall(GetCallerReturnType(), delegatedChildren, node.Scope));
        }

        private RetVal ProcessLookUp(CallNode node, Context context, RetVal tableArg)
        {
            RetVal result;
            if (node.Args.Count != 2)
            {
                return CreateNotSupportedErrorAndReturn(node, tableArg);
            }

            var predicate = node.Args[1];

            // Pattern match to see if predicate is GUID delegable.
            if (predicate is LazyEvalNode arg1b && 
                arg1b.Child is BinaryOpNode binOp &&
                binOp.Op == BinaryOpKind.EqGuid &&
                TryMatchPrimaryId(binOp.Left, binOp.Right, out _, out var guidValue, tableArg))
            {
                // Pattern match to see if predicate is delegable.
                //  Lookup(Table, Id=Guid) 
                var retVal2 = guidValue.Accept(this, context);
                var right = Materialize(retVal2);

                var findThisRecord = ThisRecordIRVisitor.FindThisRecordUsage(node, right);
                if (findThisRecord == null)
                {
                    var findBehaviorFunc = BehaviorIRVisitor.Find(right);
                    if (findBehaviorFunc != null)
                    {
                        CreateBehaviorErrorAndReturn(node, findBehaviorFunc);
                    }
                    else
                    {
                        // We can successfully delegate this call. 
                        // __retrieveGUID(table, guid);

                        if (tableArg._node is ResolvedObjectNode)
                        {
                            var newNode = _hooks.MakeRetrieveCall(tableArg, right);
                            return Ret(newNode);
                        }
                        else
                        {
                            // if tableArg was a other delegation (e.g. Filter()), then we need to Materialize the call and can't delegate lookup.
                            var tableCallNode = Materialize(tableArg);
                            var args = new List<IntermediateNode>() { tableCallNode, node.Args[1] };
                            CallNode newCall;
                            if (node.Scope != null)
                            {
                                newCall = new CallNode(node.IRContext, node.Function, node.Scope, args);
                            }
                            else
                            {
                                newCall = new CallNode(node.IRContext, node.Function, args);
                            }

                            return CreateNotSupportedErrorAndReturn(newCall, tableArg);
                        }
                    }
                }
            }

            var pr = predicate is LazyEvalNode lazyEvalNode
            ? lazyEvalNode.Child.Accept(this, context)
            : predicate.Accept(this, context);

            if (!pr.IsDelegating)
            {
                return CreateNotSupportedErrorAndReturn(node, tableArg);
            }

            // if tableArg was DV Table, delegate the call.
            if (tableArg._node is ResolvedObjectNode)
            {
                var filters = new List<IntermediateNode>() { tableArg.filter, pr.filter };
                var filterCombined = _hooks.MakeAndCall(tableArg._tableType, filters, node.Scope);
                result = new RetVal(node, tableArg._sourceTableIRNode, tableArg._tableType, tableArg.tableDS, filterCombined, tableArg.topCount);
            }
            else
            {
                // if tableArg was a other delegation (e.g. Filter()), then we need to Materialize the call and can't delegate lookup.

                var tableCallNode = Materialize(tableArg);
                var args = new List<IntermediateNode>() { tableCallNode, node.Args[1] };
                if (node.Scope != null)
                {
                    result = new RetVal(new CallNode(node.IRContext, node.Function, node.Scope, args));
                }
                else
                {
                    result = new RetVal(new CallNode(node.IRContext, node.Function, args));
                }
            }

            return result;
        }

        private RetVal ProcessFilter(CallNode node, Context context, RetVal tableArg)
        {
            if (node.Args.Count != 2) {
                return CreateNotSupportedErrorAndReturn(node, tableArg);
            }

            var predicate = node.Args[1];
            var pr = predicate is LazyEvalNode lazyEvalNode
                ? lazyEvalNode.Child.Accept(this, context)
                : predicate.Accept(this, context);

            if (!pr.IsDelegating)
            {
                return CreateNotSupportedErrorAndReturn(node, tableArg);
            }

            // Since table was delegating it potentially has filter attached to it, so also add that filter to the new filter.
            var filters = new List<IntermediateNode>() { tableArg.filter, pr.filter };
            var filterCombined = _hooks.MakeAndCall(tableArg._tableType, filters, node.Scope);
            var result = new RetVal(node, tableArg._sourceTableIRNode, tableArg._tableType, tableArg.tableDS, filterCombined, tableArg.topCount);

            return result;
        }

        private RetVal ProcessFirstN(CallNode node, RetVal tableArg)
        {
            if (node.Args.Count != 2)
            {
                return CreateNotSupportedErrorAndReturn(node, tableArg);
            }

            // Since table was delegating it potentially has filter attached to it, so also add that filter to the new node.
            var filter = tableArg.filter;
            var topCount = node.Args[1];
            return new RetVal(node, tableArg._sourceTableIRNode, tableArg._tableType, tableArg.tableDS, filter, topCount);
        }

        private RetVal CreateNotSupportedErrorAndReturn(CallNode node, RetVal tableArg)
        {
            var reason = new ExpressionError
            {
                MessageKey = "WrnDelagationTableNotSupported",
                MessageArgs = new object[] { tableArg?._metadata.LogicalName ?? "table", _maxRows},
                Span = tableArg?._sourceTableIRNode.IRContext.SourceContext ?? new Span(1,2),
                Severity = ErrorSeverity.Warning
            };
            this.AddError(reason);

            return new RetVal(node);
        }

        private RetVal CreateBehaviorErrorAndReturn(CallNode node, BehaviorIRVisitor.RetVal findBehaviorFunc)
        {
            var reason = new ExpressionError
            {
                MessageKey = "WrnDelagationBehaviorFunction",
                MessageArgs = new object[] { node.Function.Name, findBehaviorFunc.Name },
                Span = findBehaviorFunc.Span,
                Severity = ErrorSeverity.Warning
            };

            AddError(reason);
            return new RetVal(node);
        }
    }
}