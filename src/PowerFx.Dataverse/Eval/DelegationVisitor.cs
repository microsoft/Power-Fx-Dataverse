using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Dataverse.Eval.Core;
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

            public readonly DelegationHooks _hooks;

            // Original IR node for non-delegating path.
            // This should always be set. Even if we are attempting to delegate, we may need to use this if we can't support the delegation. 
            public readonly IntermediateNode _originalNode;

            // If set, we're attempting to delegate the current expression specifeid by _node.
            public bool IsDelegating => _metadata != null;
                        
            
            // IR node that will resolve to the TableValue at runtime. 
            // From here, we can downcast at get the services. 
            public readonly ResolvedObjectNode _sourceTableIRNode;

            // Table type  and original metadata for table that we're delegating to. 
            public readonly TableType _tableType;

            public readonly EntityMetadata _metadata;

            public RetVal(DelegationHooks hooks , IntermediateNode originalNode, ResolvedObjectNode sourceTableIRNode, TableType tableType, IntermediateNode filter, IntermediateNode count, int _maxRows)
            {
                this._maxRows = new NumberLiteralNode(IRContext.NotInSource(FormulaType.Number), _maxRows);
                this._sourceTableIRNode = sourceTableIRNode ?? throw new ArgumentNullException(nameof(sourceTableIRNode));
                this._tableType = tableType ?? throw new ArgumentNullException(nameof(tableType));
                this._originalNode = originalNode ?? throw new ArgumentNullException(nameof(originalNode));
                this._hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));

                // topCount and filter are optional.
                this._topCount = count;
                this._filter = filter;

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

        // If RetVal just represent a table, then ok. 
        // If it's any other in-progress delegation, then it's a warning. 
        private RetVal MaterializeTableOnly(RetVal ret)
        {
            // IsBlank(table) // ok
            // IsBlank(Filter(table,true)) // warning

            return new RetVal(ret._originalNode);
        }

        public override IntermediateNode Materialize(RetVal ret)
        {
            // if ret has no filter or count, then we can just return the original node.
            if (ret.IsDelegating && (ret.hasFilter || ret.hasTopCount))
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
                        var ret = new RetVal(_hooks, node, node, aggType, filter: null, count: null, _maxRows);
                        return ret;
                    }
                }
            }   

            // Just a regular variable, don't bother delegating. 
            return Ret(node);
        }

        public override RetVal Visit(CallNode node, Context context)
        {
            var func = node.Function.Name;

            // Some functions don't require delegation.
            // Using a table diretly as arg0 here doesn't generate a warning. 
            if (func == BuiltinFunctionsCore.IsBlank.Name || 
                func == BuiltinFunctionsCore.IsError.Name || 
                func == "Patch" || func == "Collect")
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
            
            // we don't support delegation for functions with aliasing.
            if(func == BuiltinFunctionsCore.With.Name) 
            {
                var arg0 = node.Args[0] as RecordNode;
                foreach(var field in arg0.Fields)
                {
                    var fieldRetVal = field.Value.Accept(this, context);
                    if (fieldRetVal.IsDelegating)
                    {
                        CreateNotSupportedErrorAndReturn(node, fieldRetVal);
                    }
                }

                var arg1 = Materialize(node.Args[1].Accept(this, context));
                if (!ReferenceEquals(node.Args[1], arg1))
                {
                    var result = _hooks.MakeCallNode(node.Function, node.IRContext, new List<IntermediateNode> { arg0, arg1}, node.Scope);
                    return Ret(result);
                }

                return Ret(node);
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

            RetVal ret = func switch
            {
                _ when func == BuiltinFunctionsCore.LookUp.Name => ProcessLookUp(node, context, tableArg),
                _ when func == BuiltinFunctionsCore.Filter.Name => ProcessFilter(node, tableArg),
                _ when func == BuiltinFunctionsCore.FirstN.Name => ProcessFirstN(node, tableArg),
                _ when func == BuiltinFunctionsCore.First.Name => ProcessFirst(node, tableArg),
                _ => CreateNotSupportedErrorAndReturn(node, tableArg)
            };

            // Other delegating functions, continue to compose...
            // - Sort   
            return ret;
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

                        if (tableArg._originalNode is ResolvedObjectNode)
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
            var predicateVisitor = new PredicateVisitor(_hooks, _errors, _maxRows, node, tableArg._sourceTableIRNode);
            var pr = predicate.Accept(predicateVisitor, null);

            if (!pr.IsDelegating)
            {
                return CreateNotSupportedErrorAndReturn(node, tableArg);
            }

            // if tableArg was DV Table, delegate the call.
            if (tableArg._originalNode is ResolvedObjectNode)
            {
                var filterCombined = tableArg.AddFilter(pr.filter, node.Scope);
                result = new RetVal(_hooks ,node, tableArg._sourceTableIRNode, tableArg._tableType, filterCombined, count: null, _maxRows);
            }
            else
            {
                // if tableArg was a other delegation (e.g. Filter()), then we need to Materialize the call and can't delegate lookup.
                result = MaterializeTableAndAddWarning(tableArg, node);
            }

            return result;
        }

        private RetVal ProcessFilter(CallNode node, RetVal tableArg)
        {
            if (node.Args.Count != 2) {
                return CreateNotSupportedErrorAndReturn(node, tableArg);
            }

            var predicate = node.Args[1];
            var predicateVisitor = new PredicateVisitor(_hooks, _errors, _maxRows, node, tableArg._sourceTableIRNode);
            var pr = predicate.Accept(predicateVisitor, null);

            if (!pr.IsDelegating)
            {
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
                var filterCombined = tableArg.AddFilter(pr.filter, node.Scope);
                result = new RetVal(_hooks, node, tableArg._sourceTableIRNode, tableArg._tableType, filterCombined, count: null, _maxRows);
            }

            return result;
        }

        private RetVal ProcessFirst(CallNode node, RetVal tableArg)
        {
            var countOne = new NumberLiteralNode(IRContext.NotInSource(FormulaType.Number), 1);
            var filter = tableArg.Filter;
            var res = new RetVal(_hooks, node, tableArg._sourceTableIRNode, tableArg._tableType, filter, countOne, _maxRows);
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
            return new RetVal(_hooks, node, tableArg._sourceTableIRNode, tableArg._tableType, filter, topCount, _maxRows);
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
    }
}
