using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        // $$$ Make this a member of the visitor, not the context.
        private readonly ICollection<ExpressionError> _errors;

        public DelegationIRVisitor(DelegationHooks hooks, ICollection<ExpressionError> errors)
        {
            _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
            _errors = errors ?? throw new ArgumentNullException(nameof(errors));   
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

        protected override IntermediateNode Materialize(RetVal ret)
        {
            if (ret.IsDelegating)
            {
                // Failed to delegate. 
                var reason = new ExpressionError
                {
                    Message = $"Delegating this operation on table '{ret._metadata.LogicalName}' is not supported.",
                    Span = ret._sourceTableIRNode.IRContext.SourceContext,
                    Severity = ErrorSeverity.Warning
                };
                this.AddError(reason);

                /*
                var q = ret._query;
                if (q.TopCount.HasValue || q.Criteria.Conditions.Count > 0)
                {
                    // We have an actionable filter. 
                    // Return custom node to execute that filter. 
                    // $$$$
                    
                }
                */
                // Error! Attempting to access a table, but not delegatable. 
                // $$$
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
                var type = aggType._type;

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

            // Just a regular variable, don't bother delegating. 
            return Ret(node);
        }

        public override RetVal Visit(CallNode node, Context context)
        {
            var func = node.Function.Name;

            // Some functions don't require delegation.
            if (func == "IsBlank" || func == "IsError" || func == "Patch" || func == "Collect")
            {
                var context2 = new Context { _ignoreDelegation = true };    
                return base.Visit(node, context2);
            }

            // Pattern match
            // - Lookup(Table, Id=Guid)  --> Retrieve
            // - Filter(Table, Key=Value1  && Key=Value2)  -->FetchXml

            if (node.Args.Count == 0)
            {
                // Delegated functions require arg0 is the table. 
                // So a 0-length args can't delegate.
                return base.Visit(node, context);
            }

            RetVal arg0b = node.Args[0].Accept(this, context);
            
            if (func == "LookUp")
            {            
                if (arg0b.IsDelegating && node.Args.Count == 2)
                {
                    var arg1 = node.Args[1];

                    ExpressionError reason = new ExpressionError
                    {
                        Message = $"Can't delegate LookUp: only support delegation for lookup on primary key field '{arg0b._metadata.PrimaryIdAttribute}'",
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
                        // - Lookup(Table, Id=Guid) 
                        // - Lookup(Table, Guid=Id) 
                        // - Lookup(Table, Id=  If(ThisRecord.Test > Rand(), G1, G2)) ) // NO!!!!
                        if (binOp.Op == BinaryOpKind.EqGuid)
                        {
                            var left = binOp.Left;
                            var right = binOp.Right;

                            // $$$ Normalize order?

                            if (left is ScopeAccessNode left1)
                            {
                                if (left1.Value is ScopeAccessSymbol s)
                                {
                                    var fieldName = s.Name;
                                    if (fieldName == arg0b._metadata.PrimaryIdAttribute)
                                    {

                                        // $$$ Verify 2nd arg is a guid, does not depend on ThisRecord. 
                                        // This also means loop-invariant code motion.... 
                                        // So once we have LICM, this check will be easy 

                                        // Left = PrimaryKey on metadata? 
                                        // Right = Guid? We can evaluate this. 

                                        // Call 

                                        // We may have nested delegation. 
                                        // Although LICM would also have hoisted this. 
                                        // Also catch any delegation errors in nested. 
                                        var retVal2 = right.Accept(this, context);

                                        right = Materialize(retVal2);

                                        var x = ThisRecordIRVisitor.FindThisRecordUsage(node, right);
                                        if (x == null)
                                        {


                                            // $$$ May need to fallback if we can't delegate. 
                                            // __lookup(table, guid) ?? node;

                                            // __lookup(table, guid);
                                            var newNode = _hooks.MakeRetrieveCall(arg0b, right);
                                            return Ret(newNode);
                                        }

                                        reason = new ExpressionError
                                        {
                                            Message = "Can't delegate LookUp: Id expression refers to ThisRecord",
                                            Span = x.Span,
                                            Severity = ErrorSeverity.Warning
                                        };
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
            // Other delegating functions, continue to compose...
            // - First, FirstN, 
            // - Filter
            // - Sort            

            return base.Visit(node, context, arg0b);

            // Other non-delegating supported function....
            



            // Table - 

            // First(Filter(...)) 
            // FirstN(Filter(...), N) 
            // First(Table) 
            //  qe.TopCount


            // FirstN(FirstN(...))  - silly, but compose?
            // Filter(Filter(Table)) - compose?

            // Sort(...)
            //    qe.Orders

            // Distint() 

            // First(Sort(Table))
            // Sort(First(Table))  ???? which one takes precedence. 

            // ColumnSets? - what is retrieved.

            //  Relationships???

            // Paging?

            // Capabilities
            //  CountRows(Table); // not supported

            // Warnings on things we can't delegate?
            //  Last(Table) 
            //  Filter(Table, F(ThisRecord.Field) > 2);

        }

        // First(Table).Field + 3
        // XXX().Field + 3
    }
}