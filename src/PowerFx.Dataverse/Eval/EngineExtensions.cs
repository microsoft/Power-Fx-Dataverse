using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse
{
    public static class DelegationEngineExtensions
    {
        internal static readonly DateTime _epoch = new DateTime(1899, 12, 30, 0, 0, 0, 0);

        // Only Dataverse Eval should use this.  
        // Nested class to decrease visibility. 
        internal class DelegationHooks
        {
            public virtual int DefaultMaxRows => throw new NotImplementedException();

            public virtual async Task<DValue<RecordValue>> RetrieveAsync(TableValue table, Guid id, IEnumerable<string> columns, CancellationToken cancel)
            {
                throw new NotImplementedException();
            }

            public virtual async Task<IEnumerable<DValue<RecordValue>>> RetrieveMultipleAsync(TableValue table, ISet<LinkEntity> relation, FilterExpression filter, int? topCount, IEnumerable<string> columns, bool isDistinct, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            /// <summary>
            /// This converts a FormulaValue to a value that can be used in a query's Filter Expression.
            /// </summary>
            /// <param name="table">Table that the field belongs to.</param>
            /// <param name="fieldName">Field Name.</param>
            /// <param name="value">Field Formula Value.</param>
            /// <returns>converted object</returns>
            /// <exception cref="NotImplementedException"></exception>
            public virtual object RetrieveAttribute(TableValue table, string fieldName, FormulaValue value)
            {
                throw new NotImplementedException();
            }

            internal virtual object RetrieveRelationAttribute(TableValue table, LinkEntity relation, string field, FormulaValue value)
            {
                throw new NotImplementedException();
            }

            // Are symbols from this table delegable?
            public virtual bool IsDelegableSymbolTable(ReadOnlySymbolTable symTable)
            {
                return false;
            }

            internal CallNode MakeBlankFilterCall()
            {
                var func = new DelegatedBlankFilter(this);
                var node = new CallNode(IRContext.NotInSource(FormulaType.Blank), func);
                return node;
            }

            internal CallNode MakeQueryExecutorCall(DelegationIRVisitor.RetVal query)
            {
                DelegateFunction func;
                CallNode node;
                FormulaType returnType;
                List<IntermediateNode> args;
                // If original node was returning record type, execute retrieveSingle. Otherwise, execute retrieveMultiple.
                if(query._originalNode.IRContext.ResultType is RecordType recordReturnType)
                {
                    func = new DelegatedRetrieveSingleFunction(this, recordReturnType);
                    // $$$ Change args to single record, instead of list of separate args.
                    args = new List<IntermediateNode> { query._sourceTableIRNode, query.Filter };
                    returnType = recordReturnType;
                }
                else if(query._originalNode.IRContext.ResultType is TableType tableReturnType)
                {
                    func = new DelegatedRetrieveMultipleFunction(this, tableReturnType);
                    args = new List<IntermediateNode> { query._sourceTableIRNode, query.Filter, query.TopCountOrDefault };

                    var isDistinctArg = new BooleanLiteralNode(IRContext.NotInSource(FormulaType.Boolean), query._isDistinct);
                    args.Add(isDistinctArg);
                    returnType = tableReturnType;
                }
                else
                {
                    throw new InvalidOperationException($"Unexpected return type: {query._originalNode.IRContext.ResultType.GetType()}; Should have been Record or TableType");
                }

                if (query.hasColumnSet)
                {
                    args.AddRange(query._columnSet);
                }

                if (query._originalNode is CallNode originalCallNode && originalCallNode.Scope != null)
                {
                    var scopeSymbol = originalCallNode.Scope;
                    node = new CallNode(IRContext.NotInSource(returnType), func, scopeSymbol, args);
                }
                else
                {
                    node = new CallNode(IRContext.NotInSource(returnType), func, args);
                }


                return node;
            }

            internal CallNode MakeCallNode(TexlFunction func, FormulaType tableType, IEnumerable<string> relations, string fieldName, IntermediateNode value, IntermediateNode callerSourceTable, ScopeSymbol scope)
            {
                var field = new TextLiteralNode(IRContext.NotInSource(FormulaType.String), fieldName);

                var args = new List<IntermediateNode> { callerSourceTable, field, value };
                if (relations != null)
                {
                    args.Add(MakeStringSingleColumnTable(relations));
                }

                var node = MakeCallNode(func, tableType, args, scope);
                return node;
            }

            internal CallNode MakeCallNode(TexlFunction func, FormulaType tableType, IList<IntermediateNode> args, ScopeSymbol scope)
            {
                var result = MakeCallNode(func, IRContext.NotInSource(tableType), args, scope);
                return result;
            }

            internal CallNode MakeCallNode(TexlFunction func, IRContext iRContext, IList<IntermediateNode> args, ScopeSymbol scope)
            {
                CallNode result;
                if (scope == null)
                {
                    result = new CallNode(iRContext, func, args);
                }
                else
                {
                    result = new CallNode(iRContext, func, scope, args);
                }
                return result;
            }

            internal CallNode MakeEqCall(IntermediateNode callerSourceTable, FormulaType tableType, IList<string> relations, string fieldName, BinaryOpKind operation, IntermediateNode value, ScopeSymbol callerScope)
            {
                var func = new DelegatedEq(this, operation);
                var node = MakeCallNode(func, tableType, relations, fieldName, value, callerSourceTable, callerScope);
                return node;
            }

            internal CallNode MakeNeqCall(IntermediateNode callerSourceTable, TableType tableType, IList<string> relations, string fieldName, BinaryOpKind operation, IntermediateNode value, ScopeSymbol callerScope)
            {
                var func = new DelegatedNeq(this, operation);
                var node = MakeCallNode(func, tableType, relations, fieldName, value, callerSourceTable, callerScope);
                return node;
            }

            internal CallNode MakeGtCall(IntermediateNode callerSourceTable, FormulaType tableType, IList<string> relations, string fieldName, BinaryOpKind operation, IntermediateNode value, ScopeSymbol callerScope)
            {
                var func = new DelegatedGt(this, operation);
                var node = MakeCallNode(func, tableType, relations, fieldName, value, callerSourceTable, callerScope);
                return node;
            }

            internal CallNode MakeGeqCall(IntermediateNode callerSourceTable, FormulaType tableType, IList<string> relations, string fieldName, BinaryOpKind operation, IntermediateNode value, ScopeSymbol callerScope)
            {
                var func = new DelegatedGeq(this, operation);
                var node = MakeCallNode(func, tableType, relations, fieldName, value, callerSourceTable, callerScope);
                return node;
            }

            internal CallNode MakeLtCall(IntermediateNode callerSourceTable, FormulaType tableType, IList<string> relations, string fieldName, BinaryOpKind operation, IntermediateNode value, ScopeSymbol callerScope)
            {
                var func = new DelegatedLt(this, operation);
                var node = MakeCallNode(func, tableType, relations, fieldName, value, callerSourceTable, callerScope);
                return node;
            }

            internal CallNode MakeLeqCall(IntermediateNode callerSourceTable, FormulaType tableType, IList<string> relations, string fieldName, BinaryOpKind operation, IntermediateNode value, ScopeSymbol callerScope)
            {
                var func = new DelegatedLeq(this, operation);
                var node = MakeCallNode(func, tableType, relations, fieldName, value, callerSourceTable, callerScope);
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
                var func = new DelegatedRetrieveGUIDFunction(this, (TableType)query._originalNode.IRContext.ResultType);
                var args = new List<IntermediateNode> { query._sourceTableIRNode, argGuid };
                var returnType = query._originalNode.IRContext.ResultType;
                if (query.hasColumnSet)
                {
                    args.AddRange(query._columnSet);
                }

                CallNode node;
                if (query._originalNode is CallNode originalCallNode && originalCallNode.Scope != null)
                {
                    var scopeSymbol = originalCallNode.Scope;
                    node = new CallNode(IRContext.NotInSource(returnType), func, scopeSymbol, args);
                }
                else
                {
                    node = new CallNode(IRContext.NotInSource(returnType), func, args);
                }

                return node;
            }

            internal virtual LinkEntity RetreiveManyToOneRelation(TableValue table, IEnumerable<string> links)
            {
                throw new NotImplementedException();
            }

            private static CallNode MakeStringSingleColumnTable(IEnumerable<string> fields)
            {
                var recordNodes = new List<IntermediateNode>();
                var recordType = RecordType.Empty().Add("Value", FormulaType.String);
                foreach (var field in fields)
                {
                    var fieldNode = new TextLiteralNode(IRContext.NotInSource(FormulaType.String), field);
                    var recordNode = new RecordNode(IRContext.NotInSource(recordType), new Dictionary<DName, IntermediateNode> { { new DName("Value"), fieldNode } });
                    recordNodes.Add(recordNode);
                }

                var tableType = recordType.ToTable();
                var tableNode = new CallNode(IRContext.NotInSource(tableType), BuiltinFunctionsCore.Table, recordNodes);

                return tableNode;
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
                var result = visitor.Materialize(ret);
                return result;
            }
        }

        // Called by extensions in Dataverse.Eval, which will pass in retrieveSingle.
        internal static void EnableDelegationCore(this Engine engine, DelegationEngineExtensions.DelegationHooks hooks, int maxRows)
        {
            IRTransform t = new DelegationIRTransform(hooks, maxRows);

            engine.IRTransformList.Add(t);
        }

        public static DependencyInfo ScanDependencies(this CheckResult result, CdsEntityMetadataProvider metadataCache)
        {
            var scan = DependencyInfo.Scan(result, metadataCache);
            return scan;
        }
    }
}
