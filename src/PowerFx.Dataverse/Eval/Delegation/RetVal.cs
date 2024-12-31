// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Xml.Linq;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.DelegatedFunctions;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;

namespace Microsoft.PowerFx.Dataverse
{
    // Rewrite the tree to inject delegation.
    // If we encounter a dataverse table (something that should be delegated) during the walk, we either:
    // - successfully delegate, which means rewriting to a call an efficient DelegatedFunction,
    // - leave IR unchanged (don't delegate), but issue a warning.
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        // Return Value passed through at each phase of the walk.
        public class RetVal
        {
            // supports $select with column renames and distinct
            public readonly ColumnMap ColumnMap;

            public readonly DelegationHooks Hooks;

            // Original IR node for non-delegating path.
            // This should always be set. Even if we are attempting to delegate, we may need to use this if we can't support the delegation.
            public readonly IntermediateNode OriginalNode;

            // IR node that will resolve to the TableValue at runtime.
            // From here, we can downcast at get the services. Ideally would be either Scope node or ResolvedObjectNode
            public readonly DelegableIntermediateNode _sourceTableIRNode;

            // Table type  and original metadata for table that we're delegating to.
            public readonly TableType TableType;

            public readonly IDelegationMetadata DelegationMetadata;

            private readonly IntermediateNode _filter;

            private readonly IntermediateNode _orderBy;

            private readonly IntermediateNode _topCount;

            private readonly NumberLiteralNode _maxRows;

            internal NumberLiteralNode MaxRows => _maxRows;

            // Null if not dataverse
            private readonly EntityMetadata _metadata;

            /// <summary>
            /// Will be null for non-dataverse tables.
            /// </summary>
            public EntityMetadata Metadata => _metadata ?? throw new ArgumentNullException(nameof(Metadata));

            public bool IsDataverseDelegation => _metadata != null;

            internal readonly FxGroupByNode _groupByNode;

            internal bool HasGroupByNode => _groupByNode != null;

            internal IntermediateNode GroupByNode => GenerateGroupByIR(_groupByNode, TableType);

            private RetVal(DelegationHooks hooks, IntermediateNode originalNode, IntermediateNode sourceTableIRNode, TableType tableType, IntermediateNode filter, IntermediateNode orderBy, IntermediateNode count, int maxRows, ColumnMap columnMap, FxGroupByNode groupByNode)
            {
                this._maxRows = new NumberLiteralNode(IRContext.NotInSource(FormulaType.Number), maxRows);
                this._sourceTableIRNode = new DelegableIntermediateNode(sourceTableIRNode ?? throw new ArgumentNullException(nameof(sourceTableIRNode)));
                this.TableType = tableType ?? throw new ArgumentNullException(nameof(tableType));
                this.OriginalNode = originalNode ?? throw new ArgumentNullException(nameof(originalNode));
                this.Hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
                this.DelegationMetadata = tableType._type.AssociatedDataSources.FirstOrDefault().DelegationMetadata;

                // topCount and filter are optional.
                this._topCount = count;
                this._filter = filter;
                this._orderBy = orderBy;
                this._groupByNode = groupByNode;
                this.ColumnMap = columnMap;
                this.IsDelegating = true;

                _ = DelegationUtility.TryGetEntityMetadata(tableType, out this._metadata);                
            }

            // Non-delegating path
            public RetVal(IntermediateNode node, bool isDelegating = false)
            {
                OriginalNode = node;
                IsDelegating = isDelegating;
            }

            public bool HasFilter => _filter != null;

            public bool HasOrderBy => _orderBy != null;

            public bool HasTopCount => _topCount != null;

            public bool HasColumnMap => ColumnMap != null;

            public IntermediateNode Filter => _filter ?? MakeBlankCall(Hooks);

            public IntermediateNode OrderBy => _orderBy ?? MakeBlankCall(Hooks);

            /// <summary>
            /// Only use it in final query generation.
            /// </summary>
            public IntermediateNode TopCountOrDefault => _topCount ?? _maxRows;

            // If set, we're attempting to delegate the current expression specifeid by _node.
            public bool IsDelegating { get; init; }

            public bool IsElasticTable => _metadata?.IsElasticTable() ?? false;

            public bool TryGetPrimaryIdFieldName(out string primaryId)
            {
                if (_metadata == null)
                {
                    primaryId = null;
                    return false;
                }

                primaryId = _metadata.PrimaryIdAttribute;
                return true;
            }

            public bool TryGetLogicalName(out string logicalName)
            {
                if (_metadata != null)
                {
                    logicalName = _metadata.LogicalName;
                    return true;
                }

                logicalName = null;
                return false;
            }

            internal CallNode MakeBlankCall(DelegationHooks hooks)
            {
                var func = new DelegatedBlank(hooks);
                var node = new CallNode(IRContext.NotInSource(FormulaType.Blank), func);
                return node;
            }

            private static IntermediateNode GenerateGroupByIR(FxGroupByNode groupByNode, TableType tableType)
            {
                // convert _groupByNode to IR
                var groupByFormulaValue = new GroupByObjectFormulaValue(groupByNode, tableType);
                var groupByIRNode = new ResolvedObjectNode(IRContext.NotInSource(groupByFormulaValue.Type), groupByFormulaValue);
                return groupByIRNode;
            }

            internal bool TryAddTopCount(IntermediateNode count, CallNode node, out RetVal result)
            {
                if (_topCount == null || 
                    (count is NumberLiteralNode countLiteral && countLiteral.LiteralValue == 1) || 
                    (_topCount is NumberLiteralNode numLit && count is NumberLiteralNode cLiteral && numLit.LiteralValue > cLiteral.LiteralValue))
                {
                    result = new RetVal(Hooks, node, _sourceTableIRNode, TableType, _filter, _orderBy, count, (int)_maxRows.LiteralValue, ColumnMap, _groupByNode);
                    return true;
                }

                result = null;
                return false;
            }

            internal static RetVal New(DelegationHooks hooks, ResolvedObjectNode node, int maxRows)
            {
                var tableType = (TableType)node.IRContext.ResultType;
                var result = new RetVal(hooks, node, node, tableType, filter: null, orderBy: null, count: null, maxRows, columnMap: null, groupByNode: null);
                return result;
            }

            internal bool TryAddFilter(IntermediateNode filter, CallNode node, out RetVal result)
            {
                if (HasTopCount || HasGroupByNode)
                {
                    result = null;
                    return false;
                }

                // LookUp can't nest to Filter or ForAll
                if (node.Function == BuiltinFunctionsCore.LookUp && 
                    this.OriginalNode is CallNode maybeFilter && 
                    (maybeFilter.Function == BuiltinFunctionsCore.Filter || maybeFilter.Function == BuiltinFunctionsCore.ForAll))
                {
                    result = null;
                    return false;
                }

                if (HasFilter)
                {
                    var combinedFilter = new List<IntermediateNode> { _filter, filter };
                    var combinedFilterCall = Hooks.MakeAndCall(TableType, combinedFilter, node.Scope);
                    result = new RetVal(Hooks, node, _sourceTableIRNode, TableType, combinedFilterCall, _orderBy, _topCount, (int)_maxRows.LiteralValue, ColumnMap, _groupByNode);
                    return true;
                }
                else
                {
                    result = new RetVal(Hooks, node, _sourceTableIRNode, TableType, filter, _orderBy, _topCount, (int)_maxRows.LiteralValue, ColumnMap, _groupByNode);
                    return true;
                }
            }

            internal bool TryAddDistinct(string fieldName, CallNode distinctCallNode, out RetVal result)
            {
                var canDelegate = !DelegationUtility.IsElasticTable(TableType) &&
                    _topCount == null &&
                    _groupByNode == null &&
                    IsReturnTypePrimitive(distinctCallNode.IRContext.ResultType);

                ColumnMap columnMap = null;

                // check if distinct was applied on renamed column.
                if (ColumnMap != null)
                {
                    if (ColumnMap.Map.TryGetValue(new DName(fieldName), out var realFieldNameNode))
                    {
                        var realFieldName = ((TextLiteralNode)realFieldNameNode).LiteralValue;

                        if (!DelegationUtility.CanDelegateDistinct(realFieldName, DelegationMetadata?.FilterDelegationMetadata))
                        {
                            result = null;
                            return false;
                        }

                        columnMap = ColumnMap.Combine(ColumnMap, new ColumnMap(fieldName));
                    }
                    else
                    {
                        result = null;
                        return false;
                    }
                }
                else
                {
                    columnMap = new ColumnMap(fieldName);
                    if (!DelegationUtility.CanDelegateDistinct(fieldName, DelegationMetadata?.FilterDelegationMetadata))
                    {
                        result = null;
                        return false;
                    }
                }

                if (canDelegate)
                {
                    result = new RetVal(
                        hooks: Hooks,
                        originalNode: distinctCallNode,
                        sourceTableIRNode: _sourceTableIRNode,
                        tableType: TableType,
                        filter: _filter,
                        orderBy: _orderBy,
                        count: _topCount,
                        maxRows: (int)_maxRows.LiteralValue,
                        columnMap: columnMap,
                        groupByNode: _groupByNode);

                    return true;
                }

                result = null;
                return false;
            }

            internal static RetVal NewBinaryOp(DelegationHooks hooks, DelegableIntermediateNode callerTable, IntermediateNode filterNode, int maxRows)
            {
                var tableType = (TableType)callerTable.IRContext.ResultType;
                var result = new RetVal(hooks, filterNode, callerTable, tableType, filter: filterNode, orderBy: null, count: null, maxRows, columnMap: null, groupByNode: null);
                return result;
            }

            internal bool TryAddColumnMap(ColumnMap map, CallNode node, out RetVal result)
            {
                if (HasGroupByNode ||
                    !TableType._type.AssociatedDataSources.First().IsSelectable)
                {
                    result = null;
                    return false;
                }

                ColumnMap combinedMap = null;
                if (HasColumnMap)
                {
                    combinedMap = ColumnMap.Combine(ColumnMap, map);
                }
                else
                {
                    combinedMap = map;
                }

                result = new RetVal(Hooks, node, _sourceTableIRNode, TableType, _filter, _orderBy, _topCount, (int)_maxRows.LiteralValue, combinedMap, _groupByNode);
                return true;
            }

            /// <summary>
            /// Try to add OrderBy to the current RetVal.
            /// </summary>
            /// <param name="sortColumns">column names, boolean pair enumerable with boolean representing isAscending boolean.</param>
            /// <param name="node"></param>
            /// <param name="result"></param>
            internal bool TryAddOrderBy(IEnumerable<(string, bool)> sortColumns, CallNode node, out RetVal result)
            {
                // If existing First[N], Sort[ByColumns], or ShowColumns we don't delegate
                if (HasGroupByNode || HasColumnMap || HasTopCount || HasOrderBy)
                {
                    result = null;
                    return false;
                }

                IList<IntermediateNode> orderByArgs = new List<IntermediateNode>();
                orderByArgs.Add(_filter ?? node.Args[0]);

                foreach (var (fieldName, isAscending) in sortColumns)
                {
                    if (!DelegationUtility.CanDelegateSort(fieldName, isAscending, DelegationMetadata?.SortDelegationMetadata))
                    {
                        result = null;
                        return false;
                    }

                    orderByArgs.Add(new TextLiteralNode(IRContext.NotInSource(FormulaType.String), fieldName));
                    orderByArgs.Add(new BooleanLiteralNode(IRContext.NotInSource(FormulaType.Boolean), isAscending));
                }

                var sortFunc = new DelegatedSort(Hooks);
                var orderByNode = new CallNode(node.IRContext, sortFunc, orderByArgs);

                result = new RetVal(Hooks, node, _sourceTableIRNode, TableType, _filter, orderByNode, _topCount, (int)_maxRows.LiteralValue, ColumnMap, _groupByNode);
                return true;
            }

            internal bool TryAddGroupBy(FxGroupByNode groupByNode, CallNode node, out RetVal result)
            {
                if (HasGroupByNode || HasColumnMap || HasOrderBy)
                {
                    result = null;
                    return false;
                }

                result = new RetVal(Hooks, node, _sourceTableIRNode, TableType, _filter, _orderBy, _topCount, (int)_maxRows.LiteralValue, ColumnMap, groupByNode);

                return true;
            }
        }
    }
}
