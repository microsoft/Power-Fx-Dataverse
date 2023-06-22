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

        public DelegationIRVisitor(DelegationHooks hooks, ICollection<ExpressionError> errors, int maxRows)
        {
            _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
            _errors = errors ?? throw new ArgumentNullException(nameof(errors));
            _maxRows = maxRows;
        }

        // Return Value passed through at each phase of the walk. 
        public class RetVal
        {
            // Non-delegating path 
            public RetVal(IntermediateNode node)
            {
                _node = node;
            }

            // Delegating path 
            public RetVal(IntermediateNode node, EntityMetadata metadata, ResolvedObjectNode tableIRNode, TableType tableType)
                : this(node)
            {
                _metadata = metadata;
                _sourceTableIRNode = tableIRNode;
                _tableType = tableType;
            }

            // Original IR node for non-delegating path.
            // This should always be set. Even if we are attempting to delegate, we may need to use this if we can't support the delegation. 
            public readonly IntermediateNode _node;

            // If set, we're attempting to delegate the current expression specifeid by _node.
            public bool IsDelegating => _metadata != null;
                        
            
            // IR node that will resolve to the TableValue at runtime. 
            // From here, we can downcast at get the services. 
            public readonly ResolvedObjectNode _sourceTableIRNode;

            // Table type  and original metadata for table that we're delegating to. 
            public readonly TableType _tableType;

            public readonly EntityMetadata _metadata;
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
            if (ret.IsDelegating)
            {
                // Look at delegation info and attempt to generate a delegation call. 
                // Some delegation operations can span multiple nodes, like FirstN(Filter(..))
                // If we can't, then issue a warning.

                // Failed to delegate. 
                var reason = new ExpressionError
                {
                    MessageKey = "WrnDelagationTableNotSupported",
                    MessageArgs = new object[] { ret._metadata.LogicalName, _maxRows },
                    Span = ret._sourceTableIRNode.IRContext.SourceContext,
                    Severity = ErrorSeverity.Warning
                };
                this.AddError(reason);                
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
                            var tableLogicalName = ads.TableMetadata.Name; // logical name

                            if (ads.DataEntityMetadataProvider is CdsEntityMetadataProvider m2)
                            {
                                if (m2.TryGetXrmEntityMetadata(tableLogicalName, out var metadata))
                                {
                                    // It's a delegatable table. 
                                    var ret = new RetVal(node, metadata, node, aggType);

                                    return ret;
                                }
                            }
                        }
                    }
                }
            }   

            // Just a regular variable, don't bother delegating. 
            return Ret(node);
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
                                    var reason = new ExpressionError
                                    {
                                        MessageKey = "WrnDelagationPredicate",
                                        Span = predicate.IRContext.SourceContext,
                                        Severity = ErrorSeverity.Warning
                                    };
                                    this.AddError(reason);
                                }
                            }
                        }
                    }
                }
            }
        }

        public override RetVal Visit(LazyEvalNode node, Context context)
        {
            if (node.Child is ResolvedObjectNode)
            {
                return Ret(node);
            }

            return base.Visit(node, context);
        }

        public override RetVal Visit(CallNode node, Context context)
        {
            var func = node.Function.Name;

            CheckForNopLookup(node);

            // Some functions don't require delegation.
            // Using a table diretly as arg0 here doesn't generate a warning. 
            if (func == "IsBlank" || func == "IsError" || func == "Patch" || func == "Collect")
            {
                RetVal arg0c = node.Args[0].Accept(this, context);

                arg0c = MaterializeTableOnly(arg0c);
                
                // This is what actually adds a warning message
                return base.Visit(node, context, arg0c);
            }

            if (node.Args.Count == 0)
            {
                // Delegated functions require arg0 is the table. 
                // So a 0-length args can't delegate.
                return base.Visit(node, context);
            }

            RetVal tableArg = node.Args[0].Accept(this, context);

            if (!tableArg.IsDelegating)
            {
                return base.Visit(node, context, tableArg);
            }

            if (func == "LookUp")
            {
                if (node.Args.Count == 2)
                {
                    var arg1 = node.Args[1]; // the predicate to analyze. 

                    ExpressionError reason = new ExpressionError
                    {
                        MessageKey = "WrnDelagationOnlyPrimaryKeyField",
                        MessageArgs = new object[] { func, tableArg._metadata.PrimaryIdAttribute },
                        Span = arg1.IRContext.SourceContext,
                        Severity = ErrorSeverity.Warning
                    };

                    if (arg1 is LazyEvalNode arg1b && arg1b.Child is BinaryOpNode binOp)
                    {
                        var i1 = binOp.Left.IRContext.SourceContext.Min;
                        var i2 = binOp.Right.IRContext.SourceContext.Lim;
                        var span = new Span(i1, i2);
                        reason.Span = span;

                        // Pattern match to see if predicate is delegable.
                        //  Lookup(Table, Id=Guid) 
                        if (binOp.Op == BinaryOpKind.EqGuid)
                        {
                            // Matches (Id=Guid) or (Guid=Id)
                            if (TryMatchPrimaryId(binOp.Left, binOp.Right, out var primaryIdField, out var guidValue, tableArg))
                            {
                                var retVal2 = guidValue.Accept(this, context);
                                var right = Materialize(retVal2);

                                var findThisRecord = ThisRecordIRVisitor.FindThisRecordUsage(node, right);
                                if (findThisRecord != null)
                                {
                                    reason = new ExpressionError
                                    {
                                        MessageKey = "WrnDelagationRefersThisRecord",
                                        MessageArgs = new object[] { func },
                                        Span = findThisRecord.Span,
                                        Severity = ErrorSeverity.Warning
                                    };
                                }
                                else
                                {
                                    var findBehaviorFunc = BehaviorIRVisitor.Find(right);
                                    if (findBehaviorFunc != null)
                                    {
                                        reason = new ExpressionError
                                        {
                                            MessageKey = "WrnDelagationBehaviorFunction",
                                            MessageArgs = new object[] { func, findBehaviorFunc.Name },
                                            Span = findBehaviorFunc.Span,
                                            Severity = ErrorSeverity.Warning
                                        };
                                    }
                                    else
                                    {
                                        // We can successfully delegate this call. 
                                        // __lookup(table, guid);
                                        var newNode = _hooks.MakeRetrieveCall(tableArg, right);
                                        return Ret(newNode);
                                    }
                                }
                            }
                        }
                    }

                    // Failed to delegate. Add the warning and continue. 
                    this.AddError(reason);
                    return this.Ret(node);
                }
            }
            else if (func == BuiltinFunctionsCore.FirstN.Name)
            {
                if (node.Args.Count == 2)
                {
                    var newNode = _hooks.MakeTopCall(tableArg, node.Args[1]);
                    return Ret(newNode);
                }
            }

            // Other delegating functions, continue to compose...
            // - First, 
            // - Filter
            // - Sort   
            return base.Visit(node, context, tableArg);
        }
    }
}