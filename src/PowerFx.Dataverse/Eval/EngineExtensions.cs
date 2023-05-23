using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.DataSource;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Span = Microsoft.PowerFx.Syntax.Span;

namespace Microsoft.PowerFx.Dataverse
{
    public static class DelegationEngineExtensions
    {
        // Only Dataverse Eval should use this.  
        // Nested class to decrease visibility. 
        public class DelegationHooks
        {
            // Return Blank if not found. 
            public virtual async Task<DValue<RecordValue>> RetrieveAsync(TableValue table, FilterExpression filter, CancellationToken cancel)
            {
                throw new NotImplementedException();
            }

            public virtual async Task<DValue<RecordValue>> RetrieveAsync(TableValue table, Guid id, CancellationToken cancel)
            {
                throw new NotImplementedException();
            }

            public virtual async Task<IEnumerable<DValue<RecordValue>>> RetrieveMultipleAsync(TableValue table, FilterExpression filter, int? count, CancellationToken cancel)
            {
                throw new NotImplementedException();
            }

            // Are symbols from this table delegable?
            public virtual bool IsDelegableSymbolTable(ReadOnlySymbolTable symTable)
            {
                return false;
            }

            internal CallNode MakeBlankFilterCall(FormulaType tableType)
            {
                var func = new DelegatedBlankFilter(this, (TableType)tableType);
                var node = new CallNode(IRContext.NotInSource(tableType), func);
                return node;
            }

            internal CallNode MakeQueryExecutorCall(DelegationIRVisitor.RetVal query)
            {
                DelegateFunction func;
                CallNode node;

                // If original node was returning record type, execute retriveSingle. Otherwise, execute retrieveMultiple.
                if(query._node.IRContext.ResultType is RecordType)
                {
                    func = new DelegatedRetrieveSingleFunction(this, query._tableType);
                    var args = new List<IntermediateNode> { query._sourceTableIRNode, query.filter };

                    if (query._node is CallNode originalCallNode && originalCallNode.Scope != null)
                    {
                        var scopeSymbol = originalCallNode.Scope;
                        node = new CallNode(IRContext.NotInSource(query._tableType), func, scopeSymbol, args);
                    }
                    else
                    {
                        node = new CallNode(IRContext.NotInSource(query._tableType), func, args);
                    }
                }
                else
                {
                    func = new DelegatedRetrieveMultipleFunction(this, query._tableType);
                    var args = new List<IntermediateNode> { query._sourceTableIRNode, query.filter, query.topCount };
                    if (query._node is CallNode originalCallNode && originalCallNode.Scope != null)
                    {
                        var scopeSymbol= originalCallNode.Scope;
                        node = new CallNode(IRContext.NotInSource(query._tableType), func, scopeSymbol, args);
                    }
                    else
                    {
                        node = new CallNode(IRContext.NotInSource(query._tableType), func, args);
                    }
                }
                
                return node;
            }

            internal CallNode MakeCallNode(DelegateFunction func, FormulaType tableType, IList<IntermediateNode> args, ScopeSymbol scope)
            {
                CallNode result;
                if (scope == null)
                {
                    result = new CallNode(IRContext.NotInSource(tableType), func, args);
                }
                else
                {
                    result = new CallNode(IRContext.NotInSource(tableType), func, scope, args);
                }
                return result;
            }

            internal CallNode MakeCallNode(DelegateFunction func, FormulaType tableType, string fieldName, IntermediateNode value, ScopeSymbol scope)
            {
                var field = new TextLiteralNode(IRContext.NotInSource(FormulaType.String), fieldName);
                var args = new List<IntermediateNode> { field, value };
                var node = MakeCallNode(func, tableType, args, scope);
                return node;
            }

            internal CallNode MakeEqCall(FormulaType tableType, string fieldName, IntermediateNode value, ScopeSymbol callerScope)
            {
                var func = new DelegatedEq(this);
                var node = MakeCallNode(func, tableType, fieldName, value, callerScope);
                return node;
            }

            internal CallNode MakeGtCall(FormulaType tableType, string fieldName, IntermediateNode value, ScopeSymbol callerScope)
            {
                var func = new DelegatedGt(this);
                var node = MakeCallNode(func, tableType, fieldName, value, callerScope);
                return node;
            }

            internal CallNode MakeGeqCall(FormulaType tableType, string fieldName, IntermediateNode value, ScopeSymbol callerScope)
            {
                var func = new DelegatedGeq(this);
                var node = MakeCallNode(func, tableType, fieldName, value, callerScope);
                return node;
            }

            internal CallNode MakeLtCall(FormulaType tableType, string fieldName, IntermediateNode value, ScopeSymbol callerScope)
            {
                var func = new DelegatedLt(this);
                var node = MakeCallNode(func, tableType, fieldName, value, callerScope);
                return node;
            }

            internal CallNode MakeLeqCall(FormulaType tableType, string fieldName, IntermediateNode value, ScopeSymbol callerScope)
            {
                var func = new DelegatedLeq(this);
                var node = MakeCallNode(func, tableType, fieldName, value, callerScope);
                return node;
            }

            internal CallNode MakeAndCall(FormulaType tableType, IList<IntermediateNode> args, ScopeSymbol scope)
            {
                var func = new DelegatedAnd(this);
                var result = MakeCallNode(func, tableType, args, scope);
                return result;
            }

            internal CallNode MakeOrCall(FormulaType tableType, IList<IntermediateNode> args, ScopeSymbol scope)
            {
                var func = new DelegatedOr(this);
                var result = MakeCallNode(func, tableType, args, scope);
                return result;
            }

            // Generate a lookup call for: Lookup(Table, Id=Guid)  
            internal CallNode MakeRetrieveCall(DelegationIRVisitor.RetVal query, IntermediateNode argGuid)
            {
                var func = new DelegateLookupFunction(this, query._tableType);
                
                var node = new CallNode(IRContext.NotInSource(query._tableType), func, query._sourceTableIRNode, argGuid);
                return node;
            }
        }

        private class DelegationIRTransform : IRTransform
        {
            private readonly DelegationEngineExtensions.DelegationHooks _hooks;
            private readonly int _maxRows;

            public DelegationIRTransform(DelegationEngineExtensions.DelegationHooks hooks, int maxRows)
                : base("DelegationIRTransform")
            {
                _hooks = hooks; 
                _maxRows = maxRows;
            }

            public override IntermediateNode Transform(IntermediateNode node, ICollection<ExpressionError> errors)
            {
                var visitor = new DelegationIRVisitor(_hooks, errors, _maxRows);
                var context = new DelegationIRVisitor.Context();

                var ret = node.Accept(visitor,context);
                if (ret.IsDelegating)
                {
                    return visitor.Materialize(ret);
                }
                else
                {
                    return ret._node;
                }
            }
        }

        // Called by extensions in Dataverse.Eval, which will pass in retrieveSingle.
        public static void EnableDelegationCore(this Engine engine, DelegationEngineExtensions.DelegationHooks hooks, int maxRows)
        {
            IRTransform t = new DelegationIRTransform(hooks, maxRows);

            engine.IRTransformList.Add(t);
        }
    }
}