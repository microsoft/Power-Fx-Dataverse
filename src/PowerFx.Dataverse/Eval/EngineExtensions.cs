﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse
{
    public static class DelegationEngineExtensions
    {
        internal static readonly DateTime _epoch = new DateTime(1899, 12, 30, 0, 0, 0, 0);

        // LinkEntities use suffixes to better identify them
        internal const string LinkEntityN1RelationSuffix = "_N1";

        // Only Dataverse Eval should use this.
        // Nested class to decrease visibility.
        internal class DelegationHooks
        {
            public virtual int DefaultMaxRows => throw new NotImplementedException();

            public virtual async Task<DValue<RecordValue>> RetrieveAsync(TableValue table, Guid id, string partitionId, FxColumnMap columnMap, CancellationToken cancel)
            {
                throw new NotImplementedException();
            }

            public virtual async Task<IEnumerable<DValue<RecordValue>>> RetrieveMultipleAsync(IServiceProvider services, IDelegatableTableValue table, DelegationParameters delegationParameters, TableDelegationInfo capabilities, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public virtual async Task<FormulaValue> ExecuteQueryAsync(IServiceProvider services, IDelegatableTableValue table, DelegationParameters delegationParameters, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            /// <summary>
            /// This converts a FormulaValue to a value that can be used in a query's Filter Expression.
            /// </summary>
            /// <param name="table">Table that the field belongs to.</param>
            /// <param name="fieldName">Field Name.</param>
            /// <param name="value">Field Formula Value.</param>
            /// <returns>converted object.</returns>
            /// <exception cref="NotImplementedException">.</exception>
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

            internal CallNode MakeQueryExecutorCall(DelegationIRVisitor.RetVal retVal)
            {
                DelegateFunction func;
                CallNode node;
                FormulaType returnType;
                List<IntermediateNode> args;

                // If original node was returning record type, execute retrieveSingle. Otherwise, execute retrieveMultiple.
                if (retVal.OriginalNode.IRContext.ResultType is RecordType recordReturnType)
                {
                    func = new DelegatedRetrieveSingleFunction(this, recordReturnType);

                    // $$$ Change args to single record, instead of list of separate args.
                    args = new List<IntermediateNode> { retVal._sourceTableIRNode, retVal.Filter, retVal.OrderBy, retVal.JoinNode, retVal.GroupByNode, retVal.ColumnMapNode };
                    returnType = recordReturnType;
                }
                else if (retVal.OriginalNode.IRContext.ResultType is TableType tableReturnType)
                {
                    func = new DelegatedRetrieveMultipleFunction(this, tableReturnType);
                    args = new List<IntermediateNode> { retVal._sourceTableIRNode, retVal.Filter, retVal.OrderBy, retVal.JoinNode, retVal.GroupByNode, retVal.TopCountOrDefault, retVal.ColumnMapNode };
                    returnType = tableReturnType;
                }
                else if ((retVal.LeftColumnMap?.ReturnTotalRowCount == true || retVal.HasLeftColumnMap)
                    && (retVal.OriginalNode.IRContext.ResultType is NumberType || retVal.OriginalNode.IRContext.ResultType is DecimalType))
                {
                    func = new DelegatedRetrieveTopLevelAggregationFunction(this, retVal.OriginalNode.IRContext.ResultType);
                    args = new List<IntermediateNode> { retVal._sourceTableIRNode, retVal.Filter, retVal.OrderBy, retVal.JoinNode, retVal.GroupByNode, retVal.TopCountOrDefault, retVal.ColumnMapNode };
                    returnType = retVal.OriginalNode.IRContext.ResultType;
                }
                else if (retVal.OriginalNode is CallNode callNode)
                {
                    return callNode;
                }
                else
                {
                    throw new InvalidOperationException($"Unexpected return type: {retVal.OriginalNode.IRContext.ResultType.GetType()}; Should have been Record or TableType");
                }

                if (retVal.OriginalNode is CallNode originalCallNode && originalCallNode.Scope != null)
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

            internal CallNode MakeCallNode(TexlFunction func, FormulaType tableType, IEnumerable<string> relations, IEnumerable<FieldFunction> fieldFunctions, string fieldName, IntermediateNode value, IntermediateNode callerSourceTable, ScopeSymbol scope)
            {
                var fieldInfoRecord = MakeFieldInfoRecord(fieldName, fieldFunctions);

                var args = new List<IntermediateNode> { callerSourceTable, fieldInfoRecord, value };
                if (relations != null)
                {
                    args.Add(MakeStringSingleColumnTable(relations));
                }

                var node = MakeCallNode(func, tableType, args, scope);
                return node;
            }

            /// <summary>
            /// It will create a RecordNode with structure: { fieldName: "fieldName", fieldFunctions: Table({ Value: 1 }, { Value: 2 }) } Where fieldFunctions is collection of enum <see cref="FieldFunction"/>.
            /// </summary>
            /// <param name="fieldName"></param>
            /// <param name="fieldFunctions"></param>
            /// <returns></returns>
            private static IntermediateNode MakeFieldInfoRecord(string fieldName, IEnumerable<FieldFunction> fieldFunctions)
            {
                // Define the type for records within the fieldFunctions table
                fieldFunctions = fieldFunctions ?? Enumerable.Empty<FieldFunction>();
                var fieldFunctionsRecordType = RecordType.Empty().Add(FieldInfoRecord.SingleColumnTableColumnName, FormulaType.Number);
                var fieldFunctionsTableType = fieldFunctionsRecordType.ToTable();

                // Build the list of record nodes for the fieldFunctions table
                var recordNodes = new List<IntermediateNode>();
                foreach (var ff in fieldFunctions)
                {
                    var numberNode = new NumberLiteralNode(IRContext.NotInSource(FormulaType.Number), (double)ff);
                    var recordNode = new RecordNode(
                        IRContext.NotInSource(fieldFunctionsRecordType),
                        new Dictionary<DName, IntermediateNode> { { FieldInfoRecord.SingleColumnTableColumnDName, numberNode } });
                    recordNodes.Add(recordNode);
                }

                // Create the table call node for fieldFunctions
                var fieldFunctionsTableNode = new CallNode(
                    IRContext.NotInSource(fieldFunctionsTableType),
                    BuiltinFunctionsCore.Table,
                    recordNodes);

                // Define the result record type
                var resultRecordType = RecordType.Empty()
                    .Add(FieldInfoRecord.FieldNameColumnName, FormulaType.String)
                    .Add(FieldInfoRecord.FieldFunctionColumnName, fieldFunctionsTableType);

                // Create the fieldName node
                var fieldNameNode = new TextLiteralNode(IRContext.NotInSource(FormulaType.String), fieldName);

                // Build the result record node
                var resultRecordNode = new RecordNode(
                    IRContext.NotInSource(resultRecordType),
                    new Dictionary<DName, IntermediateNode>
                    {
                        { new DName(FieldInfoRecord.FieldNameColumnName), fieldNameNode },
                        { new DName(FieldInfoRecord.FieldFunctionColumnName), fieldFunctionsTableNode }
                    });

                return resultRecordNode;
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

            internal CallNode MakeEqCall(IntermediateNode callerSourceTable, FormulaType tableType, IList<string> relations, IEnumerable<FieldFunction> fieldFunctions, string fieldName, BinaryOpKind operation, IntermediateNode value, ScopeSymbol callerScope)
            {
                var func = new DelegatedEq(this, operation);
                var node = MakeCallNode(func, tableType, relations, fieldFunctions, fieldName, value, callerSourceTable, callerScope);
                return node;
            }

            internal CallNode MakeNeqCall(IntermediateNode callerSourceTable, TableType tableType, IList<string> relations, IEnumerable<FieldFunction> fieldFunctions, string fieldName, BinaryOpKind operation, IntermediateNode value, ScopeSymbol callerScope)
            {
                var func = new DelegatedNeq(this, operation);
                var node = MakeCallNode(func, tableType, relations, fieldFunctions, fieldName, value, callerSourceTable, callerScope);
                return node;
            }

            internal CallNode MakeGtCall(IntermediateNode callerSourceTable, FormulaType tableType, IList<string> relations, IEnumerable<FieldFunction> fieldFunctions, string fieldName, BinaryOpKind operation, IntermediateNode value, ScopeSymbol callerScope)
            {
                var func = new DelegatedGt(this, operation);
                var node = MakeCallNode(func, tableType, relations, fieldFunctions, fieldName, value, callerSourceTable, callerScope);
                return node;
            }

            internal CallNode MakeGeqCall(IntermediateNode callerSourceTable, FormulaType tableType, IList<string> relations, IEnumerable<FieldFunction> fieldFunctions, string fieldName, BinaryOpKind operation, IntermediateNode value, ScopeSymbol callerScope)
            {
                var func = new DelegatedGeq(this, operation);
                var node = MakeCallNode(func, tableType, relations, fieldFunctions, fieldName, value, callerSourceTable, callerScope);
                return node;
            }

            internal CallNode MakeLtCall(IntermediateNode callerSourceTable, FormulaType tableType, IList<string> relations, IEnumerable<FieldFunction> fieldFunctions, string fieldName, BinaryOpKind operation, IntermediateNode value, ScopeSymbol callerScope)
            {
                var func = new DelegatedLt(this, operation);
                var node = MakeCallNode(func, tableType, relations, fieldFunctions, fieldName, value, callerSourceTable, callerScope);
                return node;
            }

            internal CallNode MakeLeqCall(IntermediateNode callerSourceTable, FormulaType tableType, IList<string> relations, IEnumerable<FieldFunction> fieldFunctions, string fieldName, BinaryOpKind operation, IntermediateNode value, ScopeSymbol callerScope)
            {
                var func = new DelegatedLeq(this, operation);
                var node = MakeCallNode(func, tableType, relations, fieldFunctions, fieldName, value, callerSourceTable, callerScope);
                return node;
            }

            internal CallNode MakeInCall(IntermediateNode callerSourceTable, FormulaType tableType, IList<string> relations, IEnumerable<FieldFunction> fieldFunctions, string fieldName, BinaryOpKind operation, IntermediateNode value, ScopeSymbol callerScope)
            {
                var func = new DelegatedIn(this, operation);
                var node = MakeCallNode(func, tableType, relations, fieldFunctions, fieldName, value, callerSourceTable, callerScope);
                return node;
            }

            internal CallNode MakeStartsEndsWithCall(IntermediateNode callerSourceTable, FormulaType tableType, IList<string> relations, string fieldName, IntermediateNode value, ScopeSymbol callerScope, bool isStartWith)
            {
                DelegateFunction func;
                if (isStartWith)
                {
                    func = new DelegatedStartsWith(this);
                }
                else
                {
                    func = new DelegatedEndsWith(this);
                }

                var node = MakeCallNode(func, tableType, relations, fieldFunctions: default, fieldName, value, callerSourceTable, callerScope);
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
                var func = new DelegatedRetrieveGUIDFunction(this, (TableType)query.OriginalNode.IRContext.ResultType);
                var blankNode = new CallNode(IRContext.NotInSource(FormulaType.String), BuiltinFunctionsCore.Blank);

                // last arg is blank, as we don't need partition id for retrieve in non elastic table.
                var args = new List<IntermediateNode> { query._sourceTableIRNode, argGuid, blankNode, query.ColumnMapNode };
                var returnType = query.OriginalNode.IRContext.ResultType;

                CallNode node;
                if (query.OriginalNode is CallNode originalCallNode && originalCallNode.Scope != null)
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

            internal CallNode MakeElasticRetrieveCall(DelegationIRVisitor.RetVal query, IntermediateNode argGuid, IntermediateNode partitionId)
            {
                var func = new DelegatedRetrieveGUIDFunction(this, (TableType)query.OriginalNode.IRContext.ResultType);
                var args = new List<IntermediateNode> { query._sourceTableIRNode, argGuid, partitionId, query.ColumnMapNode };
                var returnType = query.OriginalNode.IRContext.ResultType;

                CallNode node;
                if (query.OriginalNode is CallNode originalCallNode && originalCallNode.Scope != null)
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

            internal virtual FxJoinNode RetrieveManyToOneRelation(TableValue table, string link)
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

                var ret = node.Accept(visitor, context);
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
    }
}
