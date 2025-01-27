// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.Functions.Delegation.DelegationMetadata;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Core.Utils;
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
            public readonly FxColumnMap LeftColumnMap;

            public readonly DelegationHooks Hooks;

            // Original IR node for non-delegating path.
            // This should always be set. Even if we are attempting to delegate, we may need to use this if we can't support the delegation.
            public readonly IntermediateNode OriginalNode;

            // IR node that will resolve to the TableValue at runtime.
            // From here, we can downcast at get the services. Ideally would be either Scope node or ResolvedObjectNode
            public readonly DelegableIntermediateNode _sourceTableIRNode;

            // Table type and original metadata for table that we're delegating to.
            public readonly TableType TableType;

            public readonly IDelegationMetadata DelegationMetadata;

            private readonly IntermediateNode _filter;

            private readonly IntermediateNode _orderBy;

            private readonly IntermediateNode _topCount;

            private FxJoinNode _join;

            private NumberLiteralNode MaxRows => new NumberLiteralNode(IRContext.NotInSource(FormulaType.Number), _maxRows);

            private readonly int _maxRows;

            // Null if not dataverse
            private readonly EntityMetadata _metadata;

            /// <summary>
            /// Will be null for non-dataverse tables.
            /// </summary>
            public EntityMetadata Metadata => _metadata ?? throw new ArgumentNullException(nameof(Metadata));

            public bool IsDataverseDelegation => _metadata != null;

            internal readonly FxGroupByNode _groupByNode;

            internal IntermediateNode GroupByNode => GenerateGroupByIR(_groupByNode, TableType);

            internal IntermediateNode JoinNode => GenerateJoinIR(_join, TableType);

            internal IntermediateNode ColumnMapNode => GenerateColumnMapIR(LeftColumnMap);

            private RetVal(
                DelegationHooks hooks,
                IntermediateNode originalNode,
                IntermediateNode sourceTableIRNode,
                TableType tableType,
                IntermediateNode filter,
                IntermediateNode orderBy,
                IntermediateNode count,
                FxJoinNode join,
                FxGroupByNode groupby,
                int maxRows,
                FxColumnMap columnMap)
            {
                this._maxRows = maxRows;
                this._sourceTableIRNode = new DelegableIntermediateNode(sourceTableIRNode ?? throw new ArgumentNullException(nameof(sourceTableIRNode)));
                this.TableType = tableType ?? throw new ArgumentNullException(nameof(tableType));
                this.OriginalNode = originalNode ?? throw new ArgumentNullException(nameof(originalNode));
                this.Hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
                this.DelegationMetadata = tableType._type.AssociatedDataSources.FirstOrDefault()?.DelegationMetadata;

                // topCount and filter are optional.
                this._topCount = count;
                this._filter = filter;
                this._orderBy = orderBy;
                this._groupByNode = groupby;
                this._join = join;
                this.LeftColumnMap = columnMap;
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

            public bool HasJoin => _join != null;

            public bool HasGroupBy => _groupByNode != null;

            public bool HasLeftColumnMap => LeftColumnMap != null;

            public IntermediateNode Filter => _filter ?? MakeBlankCall(Hooks);

            public IntermediateNode OrderBy => _orderBy ?? MakeBlankCall(Hooks);

            public FxJoinNode Join => _join;

            public IntermediateNode TopCountOrDefault => _topCount ?? MaxRows;

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

            private static IntermediateNode GenerateColumnMapIR(FxColumnMap columnMap)
            {
                var columnMapFormulaValue = new ColumnMapFormulaValue(columnMap);
                var columnMapIRNode = new ResolvedObjectNode(IRContext.NotInSource(columnMapFormulaValue.Type), columnMapFormulaValue);
                return columnMapIRNode;
            }

            private static IntermediateNode GenerateJoinIR(FxJoinNode joinNode, TableType tableType)
            {
                var joinFormulaValue = new JoinFormulaValue(joinNode, tableType);
                var joinIRNode = new ResolvedObjectNode(IRContext.NotInSource(joinFormulaValue.Type), joinFormulaValue);
                return joinIRNode;
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
                    result = new RetVal(Hooks, node, _sourceTableIRNode, TableType, _filter, _orderBy, count, _join, _groupByNode, (int)_maxRows, LeftColumnMap);
                    return true;
                }

                result = null;
                return false;
            }

            internal static RetVal New(DelegationHooks hooks, ResolvedObjectNode node, int maxRows)
            {
                var tableType = (TableType)node.IRContext.ResultType;
                var result = new RetVal(hooks, node, node, tableType, filter: null, orderBy: null, count: null, join: null, groupby: null, maxRows, columnMap: null);
                return result;
            }

            internal bool TryAddFilter(IntermediateNode filter, CallNode node, out RetVal result)
            {
                if (HasTopCount || HasGroupBy || HasJoin)
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
                    result = new RetVal(Hooks, node, _sourceTableIRNode, TableType, combinedFilterCall, _orderBy, _topCount, _join, _groupByNode, _maxRows, LeftColumnMap);
                    return true;
                }
                else
                {
                    result = new RetVal(Hooks, node, _sourceTableIRNode, TableType, filter, _orderBy, _topCount, _join, _groupByNode, (int)_maxRows, LeftColumnMap);
                    return true;
                }
            }

            internal bool TryAddDistinct(string fieldName, CallNode distinctCallNode, out RetVal result)
            {
                var canDelegate = !DelegationUtility.IsElasticTable(TableType) &&
                    _topCount == null &&
                    _groupByNode == null &&
                    IsReturnTypePrimitive(distinctCallNode.IRContext.ResultType);

                if (!canDelegate)
                {
                    result = null;
                    return false;
                }

                string realFieldName;
                if (HasLeftColumnMap || HasJoin)
                {
                    if (HasLeftColumnMap && LeftColumnMap.TryGetColumnInfo(fieldName, out var leftTableColumnInfo))
                    {
                        // If Distinct column was present on already renamed left column map, then update that columnmap with just Distinct Column and assign Empty map to Join() selection.
                        realFieldName = leftTableColumnInfo.RealColumnName;
                        if (!DelegationUtility.CanDelegateDistinct(realFieldName, DelegationMetadata?.FilterDelegationMetadata))
                        {
                            result = null;
                            return false;
                        }

                        var fxColumnInfo = new FxColumnInfo(realColumnName: realFieldName, aliasColumnName: DVSymbolTable.SingleColumnTableFieldName, isDistinct: true);
                        var newColumnMap = new FxColumnMap(TableType);
                        newColumnMap.AddColumn(fxColumnInfo);

                        FxJoinNode newJoinNode = null;
                        if (HasJoin)
                        {
                            newJoinNode = _join.WithEmptyColumnMap();
                        }

                        result = new RetVal(Hooks, distinctCallNode, _sourceTableIRNode, TableType, _filter, _orderBy, _topCount, newJoinNode, _groupByNode, _maxRows, newColumnMap);
                        return true;
                    }
                    else if (HasJoin && _join.RightTablColumnMap.TryGetColumnInfo(fieldName, out var joinColumnInfo))
                    {
                        // If Distinct column was present on Join() selection, then update that columnmap with just Distinct Column on Join's map and assign Empty map to left column map.
                        realFieldName = joinColumnInfo.RealColumnName;
                        if (!DelegationUtility.CanDelegateDistinct(realFieldName, _join.RightTableDelegationMetadata?.FilterDelegationMetadata))
                        {
                            result = null;
                            return false;
                        }

                        var fxColumnInfo = new FxColumnInfo(realColumnName: realFieldName, aliasColumnName: DVSymbolTable.SingleColumnTableFieldName, isDistinct: true);
                        var newJoinColumnMap = new FxColumnMap(_join.JoinTableRecordType);
                        newJoinColumnMap.AddColumn(fxColumnInfo);
                        var newJoinNode = _join.With(newJoinColumnMap);
                        var newLeftColumnMap = new FxColumnMap(TableType);
                        result = new RetVal(Hooks, distinctCallNode, _sourceTableIRNode, TableType, _filter, _orderBy, _topCount, newJoinNode, _groupByNode, _maxRows, newLeftColumnMap);
                        return true;
                    }
                    else
                    {
                        throw new InvalidOperationException("Column must exist on either of the maps");
                    }
                }
                else
                {
                    if (!DelegationUtility.CanDelegateDistinct(fieldName, DelegationMetadata?.FilterDelegationMetadata))
                    {
                        result = null;
                        return false;
                    }

                    // If Distinct column was present on original table, then update that columnmap with just Distinct Column.
                    var columnMap = new FxColumnMap(TableType);
                    var columnInfo = new FxColumnInfo(realColumnName: fieldName, aliasColumnName: DVSymbolTable.SingleColumnTableFieldName, isDistinct: true);
                    columnMap.AddColumn(columnInfo);
                    result = new RetVal(Hooks, distinctCallNode, _sourceTableIRNode, TableType, _filter, _orderBy, _topCount, _join, _groupByNode, _maxRows, columnMap);
                    return true;
                }
            }

            internal static RetVal NewBinaryOp(DelegationHooks hooks, DelegableIntermediateNode callerTable, IntermediateNode filterNode, int maxRows)
            {
                var tableType = (TableType)callerTable.IRContext.ResultType;
                var result = new RetVal(hooks, filterNode, callerTable, tableType, filter: filterNode, orderBy: null, count: null, join: null, groupby: null, maxRows, columnMap: null);
                return result;
            }

            internal bool TryUpdateColumnSelection(IEnumerable<string> columnsToKeep, CallNode node, out RetVal result)
            {
                if (HasGroupBy ||
                    !TableType._type.AssociatedDataSources.First().IsSelectable)
                {
                    result = null;
                    return false;
                }

                var newLeftTableMap = new FxColumnMap(TableType);
                FxColumnMap newJoinMap = null;
                if (HasJoin)
                {
                    newJoinMap = new FxColumnMap(_join.JoinTableRecordType);
                }

                foreach (var column in columnsToKeep)
                {
                    // If FxColumnMap is present on either of the maps, then must update existing maps.
                    if (HasLeftColumnMap || HasJoin)
                    {
                        if (HasLeftColumnMap && LeftColumnMap.TryGetColumnInfo(column, out var rootFxColumnInfo))
                        {
                            newLeftTableMap.AddColumn(rootFxColumnInfo);
                        }
                        else if (HasJoin && _join.RightTablColumnMap.TryGetColumnInfo(column, out var joinColumn))
                        {
                            newJoinMap.AddColumn(joinColumn);
                        }
                        else
                        {
                            throw new InvalidOperationException("Column must exist on either of the maps");
                        }
                    }
                    else
                    {
                        newLeftTableMap.AddColumn(new FxColumnInfo(column, column));
                    }
                }

                var newJoinNode = newJoinMap != null ? _join.With(newJoinMap) : _join;
                result = new RetVal(Hooks, node, _sourceTableIRNode, TableType, _filter, _orderBy, _topCount, newJoinNode, _groupByNode, _maxRows, newLeftTableMap);
                return true;
            }

            // (column/previousAlias, alias) pair
            internal bool TryAddColumnRenames(IEnumerable<(string, string)> map, CallNode node, out RetVal result)
            {
                if (HasGroupBy ||
                    !TableType._type.AssociatedDataSources.First().IsSelectable ||
                    map.IsNullOrEmpty())
                {
                    result = null;
                    return false;
                }

                var newLeftTableMap = new FxColumnMap(TableType);
                FxColumnMap newJoinMap = null;
                if (HasJoin)
                {
                    newJoinMap = new FxColumnMap(_join.JoinTableRecordType);
                }

                foreach (var column in map)
                {
                    // If FxColumnMap is present on either of the maps, then must update existing maps.
                    if (HasLeftColumnMap || HasJoin)
                    {
                        if (HasLeftColumnMap && LeftColumnMap.TryGetColumnInfo(column.Item1, out var rootFxColumnInfo))
                        {
                            var newRootFxColumnInfo = rootFxColumnInfo.CloneAndUpdateAlias(column.Item2);
                            newLeftTableMap.AddColumn(newRootFxColumnInfo);
                        }
                        else if (HasJoin && _join.RightTablColumnMap.TryGetColumnInfo(column.Item1, out var joinColumn))
                        {
                            var newJoinColumn = joinColumn.CloneAndUpdateAlias(column.Item2);
                            newJoinMap.AddColumn(newJoinColumn);
                        }
                        else
                        {
                            throw new InvalidOperationException("Column must exist on either of the maps");
                        }
                    }
                    else
                    {
                        newLeftTableMap.AddColumn(new FxColumnInfo(column.Item1, column.Item2));
                    }
                }

                var newJoinNode = newJoinMap != null ? _join.With(newJoinMap) : _join;
                result = new RetVal(Hooks, node, _sourceTableIRNode, TableType, _filter, _orderBy, _topCount, newJoinNode, _groupByNode, _maxRows, newLeftTableMap);
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
                if (HasGroupBy || HasLeftColumnMap || HasTopCount || HasOrderBy)
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

                result = new RetVal(Hooks, node, _sourceTableIRNode, TableType, _filter, orderByNode, _topCount, _join, _groupByNode, _maxRows, LeftColumnMap);
                return true;
            }

            internal bool TryAddGroupBy(FxGroupByNode groupByNode, CallNode node, out RetVal result)
            {
                if (HasGroupBy || HasJoin || HasLeftColumnMap || HasOrderBy)
                {
                    result = null;
                    return false;
                }

                result = new RetVal(Hooks, node, _sourceTableIRNode, TableType, _filter, _orderBy, _topCount, _join, groupByNode, _maxRows, LeftColumnMap);

                return true;
            }

            internal bool TryAddJoinNode(RetVal rightTable, string fromAttribute, string toAttribute, string joinType, string rightTableAlias, FxColumnMap leftMap, FxColumnMap rightMap, CallNode node, string expand, out RetVal result)
            {
                if (!IsDelegating || HasJoin || HasGroupBy || HasOrderBy || HasLeftColumnMap || HasTopCount || HasFilter ||
                    !rightTable.IsDelegating || rightTable.HasJoin || rightTable.HasGroupBy || rightTable.HasGroupBy || rightTable.HasLeftColumnMap || rightTable.HasTopCount || rightTable.HasFilter)
                {
                    result = null;
                    return false;
                }

                // capability check, $$$ we should also check right table for Capabilities as well.
                if (DelegationUtility.CanDelegateJoin(joinType, DelegationMetadata))
                {
                    var sourceTableName = TableType.TableSymbolName;
                    var rightTableName = rightTable.TableType.TableSymbolName;
                    bool useOdataExpand = DelegationMetadata.TableAttributes.HasCapability(DelegationCapability.OdataExpand) && !string.IsNullOrEmpty(expand);

                    var joinNode = new FxJoinNode(sourceTableName, rightTableName, fromAttribute, toAttribute, joinType, rightTableAlias, rightMap, useOdataExpand ? expand : null);

                    result = new RetVal(Hooks, node, _sourceTableIRNode, TableType, _filter, _orderBy, _topCount, joinNode, _groupByNode, _maxRows, leftMap);
                    return true;
                }

                result = null;
                return false;
            }
        }
    }
}
