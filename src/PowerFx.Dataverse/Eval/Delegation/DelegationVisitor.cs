using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;
using BinaryOpNode = Microsoft.PowerFx.Core.IR.Nodes.BinaryOpNode;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;
using RecordNode = Microsoft.PowerFx.Core.IR.Nodes.RecordNode;
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

        public DelegationIRVisitor(DelegationHooks hooks, ICollection<ExpressionError> errors, int maxRow)
        {
            _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
            _errors = errors ?? throw new ArgumentNullException(nameof(errors));
            _maxRows = maxRow;
        }

        // Return Value passed through at each phase of the walk. 
        public class RetVal
        {

            private readonly IntermediateNode _filter;

            public bool hasFilter => _filter != null;

            public IntermediateNode Filter => _filter ?? _hooks.MakeBlankFilterCall();

            private readonly IntermediateNode _topCount;

            private readonly NumberLiteralNode _maxRows;

            public bool hasTopCount => _topCount != null;

            public IntermediateNode TopCountOrDefault => _topCount ?? _maxRows;

            public bool hasColumnSet => _columnSet != null;

            public readonly IEnumerable<IntermediateNode> _columnSet;

            public readonly DelegationHooks _hooks;

            // Original IR node for non-delegating path.
            // This should always be set. Even if we are attempting to delegate, we may need to use this if we can't support the delegation. 
            public readonly IntermediateNode _originalNode;

            // If set, we're attempting to delegate the current expression specifeid by _node.
            public bool IsDelegating => _metadata != null;
                        
            
            // IR node that will resolve to the TableValue at runtime. 
            // From here, we can downcast at get the services. Ideally would be either Scope node or ResolvedObjectNode
            public readonly DelegableIntermediateNode _sourceTableIRNode;

            // Table type  and original metadata for table that we're delegating to. 
            public readonly TableType _tableType;

            public readonly EntityMetadata _metadata;

            public RetVal(DelegationHooks hooks , IntermediateNode originalNode, IntermediateNode sourceTableIRNode, TableType tableType, IntermediateNode filter, IntermediateNode count, int _maxRows, IEnumerable<IntermediateNode> columnSet)
            {
                this._maxRows = new NumberLiteralNode(IRContext.NotInSource(FormulaType.Number), _maxRows);
                this._sourceTableIRNode = new DelegableIntermediateNode(sourceTableIRNode ?? throw new ArgumentNullException(nameof(sourceTableIRNode)));
                this._tableType = tableType ?? throw new ArgumentNullException(nameof(tableType));
                this._originalNode = originalNode ?? throw new ArgumentNullException(nameof(originalNode));
                this._hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));

                // topCount and filter are optional.
                this._topCount = count;
                this._filter = filter;
                this._columnSet = columnSet;
                var tableDS = tableType._type.AssociatedDataSources.FirstOrDefault();
                if (tableDS != null)
                {
                    var tableLogicalName = tableDS.TableMetadata.Name; // logical name
                    if (tableDS.DataEntityMetadataProvider is CdsEntityMetadataProvider m2)
                    {
                        if (m2.TryGetXrmEntityMetadata(tableLogicalName, out var metadata))
                        {
                            this._metadata = metadata;
                        }
                    }
                }

            }

            // Non-delegating path 
            public RetVal(IntermediateNode node)
            {
                _originalNode = node;
            }

            internal IntermediateNode AddFilter(IntermediateNode newFilter, ScopeSymbol scope)
            {
                if(_filter != null)
                {
                    var combinedFilter = new List<IntermediateNode> { _filter, newFilter };
                    var result = _hooks.MakeAndCall(_tableType, combinedFilter, scope);
                    return result;
                }
                
                return newFilter;
            }
        }

        public class Context
        {
            public bool _ignoreDelegation;

            public bool IsPredicateEvalInProgress => CallerNode != null && CallerTableNode != null;

            public readonly CallNode CallerNode;

            public readonly DelegableIntermediateNode CallerTableNode;

            public readonly Stack<IDictionary<string, RetVal>> WithScopes;

            public Context()
            {
                WithScopes = new ();
            }

            private Context(bool ignoreDelegation, Stack<IDictionary<string, RetVal>> withScopes, CallNode callerNode, DelegableIntermediateNode callerTableNode)
            {
                WithScopes = withScopes;
                CallerNode = callerNode;
                CallerTableNode = callerTableNode;
                _ignoreDelegation = ignoreDelegation;
            }

            public Context GetContextForPredicateEval(CallNode callerNode, DelegableIntermediateNode callerTableNode)
            {
                return new Context(this._ignoreDelegation, this.WithScopes, callerNode, callerTableNode);
            }

            internal void PushWithScope(IDictionary<string, RetVal> withScope)
            {
                WithScopes.Push(withScope);
            }

            internal IDictionary<string, RetVal> PopWithScope()
            {
                return WithScopes.Pop();
            }
        }

        // If an attempted delegation can't be complete, then fail it. 
        private void AddError(ExpressionError error)
        {
            _errors.Add(error);
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

        // Issue warning on typo:
        //  Filter(table, id=id)
        //  LookUp(table, id=id)
        //
        // It's legal (so must be warning, not error). Likely, correct behavior is:
        //  LookUp(table, ThisRecord.id=[@id])
        private void CheckForNopLookup(CallNode node)
        {
            var func = node.Function.Name;
            if (func == "LookUp" || func == "Filter")
            {
                if (node.Args.Count == 2)
                {
                    if (node.Args[1] is LazyEvalNode arg1b && arg1b.Child is BinaryOpNode predicate)
                    {
                        var left = predicate.Left;
                        var right = predicate.Right;

                        if (left is ScopeAccessNode left1 && right is ScopeAccessNode right1)
                        {
                            if (left1.Value is ScopeAccessSymbol left2 && right1.Value is ScopeAccessSymbol right2)
                            {
                                if (left2.Parent.Id == right2.Parent.Id &&
                                    left2.Name == right2.Name)
                                {
                                    var reason = new ExpressionError()
                                    {
                                        Span = predicate.IRContext.SourceContext,
                                        Severity = ErrorSeverity.Warning,
                                        ResourceKey = TexlStrings.WrnDelegationPredicate
                                    };
                                    this.AddError(reason);
                                }
                            }
                        }
                    }
                }
            }
        }

        public override IntermediateNode Materialize(RetVal ret)
        {
            // if ret has no filter or count, then we can just return the original node.
            if (ret.IsDelegating && (ret.hasFilter || ret.hasTopCount || ret.hasColumnSet))
            {
                var res = _hooks.MakeQueryExecutorCall(ret);
                return res;
            }

            return ret._originalNode;            
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
                        var ret = new RetVal(_hooks, node, node, aggType, filter: null, count: null, _maxRows, columnSet: null);
                        return ret;
                    }
                }
            }   

            // Just a regular variable, don't bother delegating. 
            return Ret(node);
        }

        // BinaryOpNode can be only materialized when called via CallNode.
        public override RetVal Visit(BinaryOpNode node, Context context)
        {
            if(!context.IsPredicateEvalInProgress)
            {
                return base.Visit(node, context);
            }

            var caller = context.CallerNode;
            var callerReturnType = caller.IRContext.ResultType;
            var callerSourceTable = context.CallerTableNode;
            var callerScope = caller.Scope;

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
            if (!TryGetFieldName(context, node.Left, node.Right, node.Op, out var fieldName, out var rightNode, out var operation))
            {
                return new RetVal(node);
            }

            var findThisRecord = ThisRecordIRVisitor.FindThisRecordUsage(caller, rightNode);
            if (findThisRecord != null)
            {
                CreateThisRecordErrorAndReturn(caller, findThisRecord);
            }

            var findBehaviorFunc = BehaviorIRVisitor.Find(rightNode);
            if (findBehaviorFunc != null)
            {
                return CreateBehaviorErrorAndReturn(caller, findBehaviorFunc);
            }

            var retDelegationVisitor = rightNode.Accept(this, context);
            if (retDelegationVisitor.IsDelegating)
            {
                rightNode = Materialize(retDelegationVisitor);
            }
            else
            {
                rightNode = retDelegationVisitor._originalNode;
            }

            RetVal ret;

            if (IsOpKindEqualityComparison(operation))
            {
                var eqNode = _hooks.MakeEqCall(callerSourceTable, tableType, fieldName, operation, rightNode, callerScope);
                ret = CreateBinaryOpRetVal(context, node, eqNode);
            }
            else if (IsOpKindInequalityComparison(operation))
            {
                var neqNode = _hooks.MakeNeqCall(callerSourceTable, tableType, fieldName, operation, rightNode, callerScope);
                ret = CreateBinaryOpRetVal(context, node, neqNode);
            }
            else if (IsOpKindLessThanComparison(operation))
            {
                var ltNode = _hooks.MakeLtCall(callerSourceTable, tableType, fieldName, operation, rightNode, callerScope);
                ret = CreateBinaryOpRetVal(context, node, ltNode);
            }
            else if (IsOpKindLessThanEqualComparison(operation))
            {
                var leqNode = _hooks.MakeLeqCall(callerSourceTable, tableType, fieldName, operation, rightNode, callerScope);
                ret = CreateBinaryOpRetVal(context, node, leqNode);
            }
            else if (IsOpKindGreaterThanComparison(operation))
            {
                var gtNode = _hooks.MakeGtCall(callerSourceTable, tableType, fieldName, operation, rightNode, callerScope);
                ret = CreateBinaryOpRetVal(context, node, gtNode);
            }
            else if (IsOpKindGreaterThanEqalComparison(operation))
            {
                var geqNode = _hooks.MakeGeqCall(callerSourceTable, tableType, fieldName, operation, rightNode, callerScope);
                ret = CreateBinaryOpRetVal(context, node, geqNode);
            }
            else
            {
                ret = new RetVal(node);
            }

            return ret;
        }

        internal static bool IsOpKindEqualityComparison(BinaryOpKind op)
        {
            return op == BinaryOpKind.EqBoolean ||
                op == BinaryOpKind.EqCurrency ||
                op == BinaryOpKind.EqDate ||
                op == BinaryOpKind.EqDateTime ||
                op == BinaryOpKind.EqDecimals ||
                op == BinaryOpKind.EqGuid ||
                op == BinaryOpKind.EqNumbers ||
                op == BinaryOpKind.EqText ||
                op == BinaryOpKind.EqTime ||
                op == BinaryOpKind.EqOptionSetValue;
        }

        internal static bool IsOpKindInequalityComparison(BinaryOpKind op)
        {
            return op == BinaryOpKind.NeqBoolean ||
                op == BinaryOpKind.NeqCurrency ||
                op == BinaryOpKind.NeqDate ||
                op == BinaryOpKind.NeqDateTime ||
                op == BinaryOpKind.NeqDecimals ||
                op == BinaryOpKind.NeqGuid ||
                op == BinaryOpKind.NeqNumbers ||
                op == BinaryOpKind.NeqText ||
                op == BinaryOpKind.NeqTime ||
                op == BinaryOpKind.NeqOptionSetValue;
        }

        internal static bool IsOpKindLessThanComparison(BinaryOpKind op)
        {
            return op == BinaryOpKind.LtNumbers ||
                op == BinaryOpKind.LtDecimals ||
                op == BinaryOpKind.LtDateTime ||
                op == BinaryOpKind.LtDate ||
                op == BinaryOpKind.LtTime;
        }

        internal static bool IsOpKindLessThanEqualComparison(BinaryOpKind op)
        {
            return op == BinaryOpKind.LeqNumbers ||
                op == BinaryOpKind.LeqDecimals ||
                op == BinaryOpKind.LeqDateTime ||
                op == BinaryOpKind.LeqDate ||
                op == BinaryOpKind.LeqTime;
        }

        internal static bool IsOpKindGreaterThanComparison(BinaryOpKind op)
        {
            return op == BinaryOpKind.GtNumbers ||
                op == BinaryOpKind.GtDecimals ||
                op == BinaryOpKind.GtDateTime ||
                op == BinaryOpKind.GtDate ||
                op == BinaryOpKind.GtTime;
        }

        internal static bool IsOpKindGreaterThanEqalComparison(BinaryOpKind op)
        {
            return op == BinaryOpKind.GeqNumbers ||
                op == BinaryOpKind.GeqDecimals ||
                op == BinaryOpKind.GeqDateTime ||
                op == BinaryOpKind.GeqDate ||
                op == BinaryOpKind.GeqTime;
        }

        private RetVal CreateBinaryOpRetVal(Context context, IntermediateNode node, IntermediateNode eqNode)
        {
            var callerTable = context.CallerTableNode;
            var callerTableReturnType = callerTable.IRContext.ResultType as TableType ?? throw new InvalidOperationException("CallerTable ReturnType should always be TableType");
            return new RetVal(_hooks, node, callerTable, callerTableReturnType, eqNode, count: null, _maxRows, columnSet: null);
        }

        public override RetVal Visit(LazyEvalNode node, Context context)
        {
            if(!context.IsPredicateEvalInProgress)
            {
                return base.Visit(node, context);
            }
            var child = node.Child.Accept(this, context);

            if (child.IsDelegating)
            {
                return child;
            }
            else
            {
                if (!ReferenceEquals(child._originalNode, node.Child))
                {
                    node = new LazyEvalNode(node.IRContext, child._originalNode);
                }
                
                return Ret(node);
            }
        }

        public override RetVal Visit(SingleColumnTableAccessNode node, Context context)
        {
            var maybeDelegatedfrom = Materialize(node.From.Accept(this, context));

            if (!ReferenceEquals(node.From, maybeDelegatedfrom))
            {
                var delegatedSCTANode = new SingleColumnTableAccessNode(node.IRContext, maybeDelegatedfrom, node.Field);
                return Ret(delegatedSCTANode);
            }

            return Ret(node);
        }

        public override RetVal Visit(CallNode node, Context context)
        {
            var func = node.Function.Name;

            if (func == BuiltinFunctionsCore.And.Name && context.IsPredicateEvalInProgress)
            {
                return ProcessAnd(node, context);
            }
            else if (func == BuiltinFunctionsCore.Or.Name && context.IsPredicateEvalInProgress)
            {
                return ProcessOr(node, context);
            }
            else if (func == BuiltinFunctionsCore.IsBlank.Name && context.IsPredicateEvalInProgress)
            {
                return ProcessIsBlank(node, context);
            }

            // Some functions don't require delegation.
            // Using a table diretly as arg0 here doesn't generate a warning. 
            if (func == BuiltinFunctionsCore.IsBlank.Name || 
                func == BuiltinFunctionsCore.IsError.Name || 
                func == "Patch" || func == "Collect")
            {
                RetVal arg0c = node.Args[0].Accept(this, context);
                                
                return base.Visit(node, context, arg0c);
            }

            if (node.Args.Count == 0)
            {
                // Delegated functions require arg0 is the table. 
                // So a 0-length args can't delegate.
                return base.Visit(node, context);
            }
            
            // Since With supports scopes, it needs to be processed differently.
             if(func == BuiltinFunctionsCore.With.Name) 
            {
                return ProcessWith(node, context);
            }

            // Only below function fulfills assumption that first arg is Table
            if(!(node.Function.ParamTypes.Length > 0 && node.Function.ParamTypes[0].IsTable))
            {
                return base.Visit(node, context);
            }

            RetVal tableArg;
            // special casing Scope access for With()
            if (node.Args[0] is ScopeAccessNode scopedFirstArg && scopedFirstArg.IRContext.ResultType is TableType && scopedFirstArg.Value is ScopeAccessSymbol scopedSymbol
                && TryGetScopedVariable(context.WithScopes, scopedSymbol.Name, out var scopedNode))
            {
                tableArg = scopedNode;
            }
            else
            {
                tableArg = node.Args[0].Accept(this, context);
            }

            if (!tableArg.IsDelegating)
            {
                return base.Visit(node, context, tableArg);
            }

            RetVal ret = func switch
            {
                _ when func == BuiltinFunctionsCore.LookUp.Name => ProcessLookUp(node, tableArg, context),
                _ when func == BuiltinFunctionsCore.Filter.Name => ProcessFilter(node, tableArg, context),
                _ when func == BuiltinFunctionsCore.FirstN.Name => ProcessFirstN(node, tableArg),
                _ when func == BuiltinFunctionsCore.First.Name => ProcessFirst(node, tableArg),
                _ when func == BuiltinFunctionsCore.ShowColumns.Name => ProcessShowColumn(node, tableArg),
                _ => ProcessOtherFunctions(node, tableArg)
            };

            // Other delegating functions, continue to compose...
            // - Sort   
            return ret;
        }

        private RetVal ProcessIsBlank(CallNode node, Context context)
        {
            if (TryGetFieldName(context, node, out var fieldName))
            {
                var blankNode = new CallNode(IRContext.NotInSource(FormulaType.Blank), BuiltinFunctionsCore.Blank);

                // BinaryOpKind doesn't matter for IsBlank because all value will be compared to null, so just use EqText.
                var eqNode = _hooks.MakeEqCall(context.CallerTableNode, context.CallerTableNode.IRContext.ResultType, fieldName, BinaryOpKind.EqText, blankNode, context.CallerNode.Scope);
                var ret = CreateBinaryOpRetVal(context, node, eqNode);
                return ret;
            }

            RetVal arg0c = node.Args[0].Accept(this, context);

            return base.Visit(node, context, arg0c);
        }

        private RetVal ProcessOtherFunctions(CallNode node, RetVal tableArg)
        {
            var maybeDelegableTable = Materialize(tableArg);
            // If TableArg was delegable, then replace it and no need to add warning. As expr like Concat(Filter(), expr) works fine.
            if (!ReferenceEquals(node.Args[0], maybeDelegableTable))
            {
                var delegableArgs = new List<IntermediateNode>() { maybeDelegableTable };
                delegableArgs.AddRange(node.Args.Skip(1));
                CallNode delegableCallNode;
                if(node.Scope != null)
                {
                    delegableCallNode = new CallNode(node.IRContext, node.Function, node.Scope, delegableArgs);
                }
                else
                {
                    delegableCallNode = new CallNode(node.IRContext, node.Function, delegableArgs);
                }

                return Ret(delegableCallNode);
            }

            return CreateNotSupportedErrorAndReturn(node, tableArg);
        }

        private RetVal ProcessShowColumn(CallNode node, RetVal tableArg)
        {
            var filter = tableArg.hasFilter ? tableArg.Filter : null;
            var count = tableArg.hasTopCount ? tableArg.TopCountOrDefault : null;

            // change to original node to current node and appends columnSet.
            var resultingTable = new RetVal(_hooks, node, tableArg._sourceTableIRNode, tableArg._tableType, filter, count, _maxRows, node.Args.Skip(1));

            if (node is CallNode maybeGuidCall && maybeGuidCall.Function is DelegatedRetrieveGUIDFunction)
            {
                var guidCallWithColSet = _hooks.MakeRetrieveCall(resultingTable, maybeGuidCall.Args[1]);
                return Ret(guidCallWithColSet);
            }

            return resultingTable;
        }

        private RetVal ProcessWith(CallNode node, Context context)
        {
            var arg0 = (RecordNode)node.Args[0];
            var arg1 = (LazyEvalNode)node.Args[1];

            var withScope = RecordNodeToDictionary(arg0, context);
            var maybeDelegatedArg0 = new RecordNode(arg0.IRContext, withScope.ToDictionary(kv => new DName(kv.Key), kv => Materialize(kv.Value)));

            context.PushWithScope(withScope);
            var arg1MaybeDelegable = Materialize(arg1.Child.Accept(this, context));
            var poppedWithScope = context.PopWithScope();
            
            if(withScope != poppedWithScope)
            {
                throw new InvalidOperationException("With scope stack is corrupted");
            }

            if (!ReferenceEquals(arg1MaybeDelegable,arg1.Child))
            {
                var lazyArg1 = new LazyEvalNode(arg1.Child.IRContext, arg1MaybeDelegable);
                var delegatedWith = new CallNode(node.IRContext, node.Function, node.Scope, new List<IntermediateNode>() { maybeDelegatedArg0, lazyArg1 });
                return Ret(delegatedWith);
            }
            else
            {
                return Ret(new CallNode(node.IRContext, node.Function, node.Scope, new List<IntermediateNode>() { maybeDelegatedArg0, arg1 }));
            }
        }

        private IDictionary<string, RetVal> RecordNodeToDictionary(RecordNode arg0, Context context)
        {
            var scope = new Dictionary<string, RetVal>();
            foreach(var field in arg0.Fields)
            {
                var valueRetVal = field.Value.Accept(this, context);
                scope.Add(field.Key.Value, valueRetVal);
            }

            return scope;
        }

        private bool TryGetScopedVariable(Stack<IDictionary<string, RetVal>> withScopes, string variable, out RetVal node)
        {
            if(withScopes.Count() == 0)
            {
                node = default;
                return false;
            }

            foreach (var kv in withScopes)
            {
                if (kv.TryGetValue(variable, out node))
                {
                    return true;
                }
            }

            node = default;
            return false;
        }

        private RetVal ProcessLookUp(CallNode node, RetVal tableArg, Context context)
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
                CheckForNopLookup(node);

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

                        if (IsTableArgLookUpDelegable(context, tableArg))
                        {
                            var newNode = _hooks.MakeRetrieveCall(tableArg, right);
                            return Ret(newNode);
                        }
                        else
                        {
                            // if tableArg was another delegation (e.g. Filter()), then we need to Materialize the call and can't delegate lookup.
                            return MaterializeTableAndAddWarning(tableArg, node);
                        }
                    }
                }
                else
                {
                    return CreateThisRecordErrorAndReturn(node, findThisRecord);
                }
            }

            // Pattern match to see if predicate is delegable when field is non primary key.
            var predicteContext = context.GetContextForPredicateEval(node, tableArg._sourceTableIRNode);
            var pr = predicate.Accept(this, predicteContext);

            if (!pr.IsDelegating)
            {
                // Though entire predicate is not delegable, pr._originalNode may still have delegation buried inside it.
                if (!ReferenceEquals(pr._originalNode, predicate))
                {
                    node = new CallNode(node.IRContext, node.Function, node.Scope, new List<IntermediateNode>() { node.Args[0], pr._originalNode });
                }

                return CreateNotSupportedErrorAndReturn(node, tableArg);
            }

            // if tableArg was DV Table, delegate the call.
            if (IsTableArgLookUpDelegable(context, tableArg))
            {
                var filterCombined = tableArg.AddFilter(pr.Filter, node.Scope);
                result = new RetVal(_hooks ,node, tableArg._sourceTableIRNode, tableArg._tableType, filterCombined, count: null, _maxRows, tableArg._columnSet);
            }
            else
            {
                // if tableArg was a other delegation (e.g. Filter()), then we need to Materialize the call and can't delegate lookup.
                result = MaterializeTableAndAddWarning(tableArg, node);
            }

            return result;
        }

        private bool IsTableArgLookUpDelegable(Context context, RetVal tableArg)
        {
            if (tableArg._originalNode is ResolvedObjectNode 
                || (tableArg._sourceTableIRNode.InnerNode is ScopeAccessNode scopedTableArg
                    && scopedTableArg.Value is ScopeAccessSymbol scopedSymbol
                    && TryGetScopedVariable(context.WithScopes, scopedSymbol.Name, out var scopedNode)
                    && scopedNode._originalNode is ResolvedObjectNode)
                || (tableArg.IsDelegating && tableArg._originalNode is CallNode callNode && callNode.Function.Name == BuiltinFunctionsCore.ShowColumns.Name)
                )
            {
                return true;
            }

            return false;
        }

        private RetVal ProcessFilter(CallNode node, RetVal tableArg, Context context)
        {
            if (node.Args.Count != 2) {
                return CreateNotSupportedErrorAndReturn(node, tableArg);
            }

            var predicate = node.Args[1];
            var predicteContext = context.GetContextForPredicateEval(node, tableArg._sourceTableIRNode);
            var pr = predicate.Accept(this, predicteContext);

            if (!pr.IsDelegating)
            {
                // Though entire predicate is not delegable, pr._originalNode may still have delegation buried inside it.
                if(!ReferenceEquals(pr._originalNode, predicate))
                {
                    node = new CallNode(node.IRContext, node.Function, node.Scope, new List<IntermediateNode>() { node.Args[0], pr._originalNode });
                }

                return CreateNotSupportedErrorAndReturn(node, tableArg);
            }

            RetVal result;

            // If tableArg has top count, that means we need to materialize the tableArg and can't delegate.
            if (tableArg.hasTopCount)
            {
                result = MaterializeTableAndAddWarning(tableArg, node);
            }
            else
            {
                // Since table was delegating it potentially has filter attached to it, so also add that filter to the new filter.
                var filterCombined = tableArg.AddFilter(pr.Filter, node.Scope);
                result = new RetVal(_hooks, node, tableArg._sourceTableIRNode, tableArg._tableType, filterCombined, count: null, _maxRows, tableArg._columnSet);
            }

            return result;
        }

        private RetVal ProcessFirst(CallNode node, RetVal tableArg)
        {
            var countOne = new NumberLiteralNode(IRContext.NotInSource(FormulaType.Number), 1);
            var filter = tableArg.Filter;
            var res = new RetVal(_hooks, node, tableArg._sourceTableIRNode, tableArg._tableType, filter, countOne, _maxRows, tableArg._columnSet);
            return res;
        }

        private RetVal ProcessFirstN(CallNode node, RetVal tableArg)
        {
            // Add default count of 1 if not specified.
            if(node.Args.Count == 1)
            {
                node.Args.Add(new NumberLiteralNode(IRContext.NotInSource(FormulaType.Number), 1));
            }
            else if (node.Args.Count != 2)
            {
                return CreateNotSupportedErrorAndReturn(node, tableArg);
            }

            // Since table was delegating it potentially has filter attached to it, so also add that filter to the new node.
            var filter = tableArg.Filter;
            var topCount = node.Args[1];
            return new RetVal(_hooks, node, tableArg._sourceTableIRNode, tableArg._tableType, filter, topCount, _maxRows, tableArg._columnSet);
        }

        private RetVal MaterializeTableAndAddWarning(RetVal tableArg, CallNode node)
        {
            var tableCallNode = Materialize(tableArg);
            var args = new List<IntermediateNode>() { tableCallNode, node.Args[1] };
            var newCall = _hooks.MakeCallNode(node.Function, node.IRContext, args, node.Scope);
            return CreateNotSupportedErrorAndReturn(newCall, tableArg);
        }

        private RetVal CreateNotSupportedErrorAndReturn(CallNode node, RetVal tableArg)
        {
            var reason = new ExpressionError()
            {
                MessageArgs = new object[] { tableArg?._metadata.LogicalName ?? "table", _maxRows },
                Span = tableArg?._sourceTableIRNode.IRContext.SourceContext ?? new Span(1, 2),
                Severity = ErrorSeverity.Warning,
                ResourceKey = TexlStrings.WrnDelegationTableNotSupported
            };
            this.AddError(reason);

            return new RetVal(node);
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
                MessageArgs = new object[] { node.Function.Name },
                Span = findThisRecord.Span,
                Severity = ErrorSeverity.Warning,
                ResourceKey = TexlStrings.WrnDelegationRefersThisRecord
            };
            AddError(reason);
            return new RetVal(node);
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
                    if (delegatedArg.hasFilter)
                    {
                        delegatedChild.Add(delegatedArg.Filter);
                    }
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
                var rVal = CreateBinaryOpRetVal(context, node, filter);
                return rVal;
            }

            return new RetVal(node);
        }

        public RetVal ProcessOr(CallNode node, Context context)
        {
            var callerReturnType = context.CallerNode.IRContext.ResultType;
            return ProcessAndOr(node, context, delegatedChildren => _hooks.MakeOrCall(callerReturnType, delegatedChildren, node.Scope));
        }

        public RetVal ProcessAnd(CallNode node, Context context)
        {
            var callerReturnType = context.CallerNode.IRContext.ResultType;
            return ProcessAndOr(node, context, delegatedChildren => _hooks.MakeAndCall(callerReturnType, delegatedChildren, node.Scope));
        }

        public bool TryGetFieldName(Context context, IntermediateNode left, IntermediateNode right, BinaryOpKind op, out string fieldName, out IntermediateNode node, out BinaryOpKind opKind)
        {
            if (TryGetFieldName(context, left, out var leftField) && !TryGetFieldName(context, right, out _))
            {
                fieldName = leftField;
                node = right;
                opKind = op;
                return true;
            }
            else if (TryGetFieldName(context, right, out var rightField) && !TryGetFieldName(context, left, out _))
            {
                fieldName = rightField;
                node = left;
                if (TryInvertLeftRight(op, out var invertedOp))
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
            else if (TryGetFieldName(context, left, out var leftField2) && TryGetFieldName(context, right, out var rightField2))
            {
                if (leftField2 == rightField2)
                {
                    // Issue warning
                    if (IsOpKindEqualityComparison(op))
                    {
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
                    }

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

        private bool TryInvertLeftRight(BinaryOpKind op, out BinaryOpKind invertedOp)
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

        public bool TryGetFieldName(Context context, IntermediateNode node, out string fieldName)
        {
            IntermediateNode maybeScopeAccessNode;

            // If the node had injected float coercion, then we need to pull scope access node from it.
            if (node is CallNode functionCall && (functionCall.Function == BuiltinFunctionsCore.Float || functionCall.Function == BuiltinFunctionsCore.Value || functionCall.Function.Name == BuiltinFunctionsCore.IsBlank.Name))
            {
                maybeScopeAccessNode = functionCall.Args[0];
            }
            else
            {
                maybeScopeAccessNode = node;
            }

            if (maybeScopeAccessNode is ScopeAccessNode scopeAccessNode)
            {
                if (scopeAccessNode.Value is ScopeAccessSymbol scopeAccessSymbol)
                {
                    var callerScope = context.CallerNode.Scope;
                    var callerId = callerScope.Id;
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
    }
}
