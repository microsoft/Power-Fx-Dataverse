// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;
using RecordNode = Microsoft.PowerFx.Core.IR.Nodes.RecordNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        private RetVal ProcessSummarize(CallNode node, RetVal tableArg, Context context)
        {
            var groupByProperties = new List<FxColumnInfo>();
            var aggregateExpressions = new List<FxColumnInfo>();
            var isReturningTotalCount = false;
            var delegationInfo = tableArg.TableType.ToRecord().TryGetCapabilities(out var capabilities);

            // Process arguments for group by or aggregate logic
            foreach (var arg in node.Args.Skip(1))
            {
                if (arg is TextLiteralNode columnName)
                {
                    var columnInfo = GetRealFieldName(tableArg, columnName);

                    if (capabilities.CanDelegateSummarize(columnInfo.RealColumnName, SummarizeMethod.None, tableArg.IsDataverseDelegation))
                    {
                        groupByProperties.Add(columnInfo);
                    }
                    else
                    {
                        return ProcessOtherCall(node, tableArg, context);
                    }
                }
                else if (arg is LazyEvalNode lazyEvalNode && lazyEvalNode.Child is RecordNode scope && TryProcessAggregateExpression(node, scope, context, tableArg, aggregateExpressions, capabilities))
                {
                    continue;
                }
                else
                {
                    return ProcessOtherCall(node, tableArg, context);
                }
            }

            if (tableArg.TryAddGroupBy(groupByProperties, aggregateExpressions, node, out RetVal result))
            {
                return result;
            }

            return ProcessOtherCall(node, tableArg, context);
        }

        private static bool TryProcessAggregateExpression(CallNode node, RecordNode scope, Context context, RetVal sourceTable, IList<FxColumnInfo> aggregateExpressions, TableDelegationInfo capabilities)
        {
            var aliasName = scope.Fields.First().Key.Value;
            if (scope.Fields.First().Value is CallNode aggregationCallNode)
            {
                // Try single-field aggregations (Sum, Min, Max, Avg)
                if (TryAddSingleFieldAggregateExpression(aggregationCallNode, node, context, sourceTable, aliasName, aggregateExpressions, capabilities, SummarizeMethod.Sum, BuiltinFunctionsCore.SumT.Name) ||
                    TryAddSingleFieldAggregateExpression(aggregationCallNode, node, context, sourceTable, aliasName, aggregateExpressions, capabilities, SummarizeMethod.Min, BuiltinFunctionsCore.MinT.Name) ||
                    TryAddSingleFieldAggregateExpression(aggregationCallNode, node, context, sourceTable, aliasName, aggregateExpressions, capabilities, SummarizeMethod.Max, BuiltinFunctionsCore.MaxT.Name) ||
                    TryAddSingleFieldAggregateExpression(aggregationCallNode, node, context, sourceTable, aliasName, aggregateExpressions, capabilities, SummarizeMethod.Average, BuiltinFunctionsCore.AverageT.Name))
                {
                    return true;
                }
                else if (TryAddCountIfAggregateExpression(aggregationCallNode, node, context, sourceTable, aliasName, aggregateExpressions, capabilities, sourceTable))
                {
                    // CountIf() is a special case, as it requires a predicate with IsBlank() and Not() functions
                    return true;
                }
                else if (TryAddCountRowsAggregation(aggregationCallNode, node, context, sourceTable, aliasName, aggregateExpressions, capabilities))
                {
                    // CountRows() is a special case, as it doesn't require a field name, just alias name e.g. CountRows(ThisGroup) as TCount.
                    return true;
                }
            }

            return false;
        }

        private static bool IsAggregateFunction(CallNode aggregateExpressionNode, CallNode groupByNode, string functionName, int expectedArgCount)
        {
            return aggregateExpressionNode.Function.Name == functionName &&
                   aggregateExpressionNode.Args.Count == expectedArgCount &&
                   aggregateExpressionNode.Args[0] is ScopeAccessNode scopeNode &&
                   scopeNode.Value is ScopeAccessSymbol scopeSymbol &&
                   scopeSymbol.Parent.Id == groupByNode.Scope.Id;
        }

        private static bool TryAddSingleFieldAggregateExpression(CallNode maybeNode, CallNode summarizeNode, Context context, RetVal sourceTable, string aliasName, IList<FxColumnInfo> aggregateExpressions, TableDelegationInfo capabilities, SummarizeMethod method, string functionName)
        {
            if (!IsAggregateFunction(maybeNode, summarizeNode, functionName, 2))
            {
                return false;
            }

            var fieldArg = maybeNode.Args[1] as LazyEvalNode;
            var predicateContext = context.GetContextForPredicateEval(maybeNode, sourceTable);

            if (DelegationIRVisitor.TryGetRealFieldNameFromScopeNode(predicateContext, fieldArg.Child, out var fieldInfo) &&
                capabilities.CanDelegateSummarize(fieldInfo.RealColumnName, method, sourceTable.IsDataverseDelegation))
            {
                aggregateExpressions.Add(new FxColumnInfo(fieldInfo.RealColumnName, aliasName, isDistinct: false, aggregateOperation: method));
                return true;
            }

            return false;
        }

        // CountIf(ThisGroup, Not(IsBlank(fieldInfo)))
        private static bool TryAddCountIfAggregateExpression(CallNode maybeCountIfNode, CallNode node, Context context, RetVal sourceTable, string aliasName, IList<FxColumnInfo> aggregateExpressions, TableDelegationInfo capabilities, RetVal tableArg)
        {
            if (!IsAggregateFunction(maybeCountIfNode, node, BuiltinFunctionsCore.CountIf.Name, 2))
            {
                return false;
            }

            var maybeNotCall = maybeCountIfNode.Args[1] as LazyEvalNode;

            if (maybeNotCall?.Child is CallNode maybeNotCallNode && maybeNotCallNode.Function.Name == BuiltinFunctionsCore.Not.Name)
            {
                if (maybeNotCallNode.Args[0] is CallNode maybeIsBlankCallNode && maybeIsBlankCallNode.Function.Name == BuiltinFunctionsCore.IsBlank.Name)
                {
                    var predicateContext = context.GetContextForPredicateEval(maybeCountIfNode, sourceTable);
                    if (DelegationIRVisitor.TryGetRealFieldNameFromScopeNode(predicateContext, maybeIsBlankCallNode.Args[0], out var fieldInfo) &&
                        capabilities.CanDelegateSummarize(fieldInfo.RealColumnName, SummarizeMethod.Count, tableArg.IsDataverseDelegation))
                    {
                        aggregateExpressions.Add(new FxColumnInfo(fieldInfo.RealColumnName, aliasName, isDistinct: false, aggregateOperation: SummarizeMethod.Count));
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryAddCountRowsAggregation(CallNode maybeCountRowsNode, CallNode node, Context context, RetVal sourceTable, string aliasName, IList<FxColumnInfo> aggregateExpressions, TableDelegationInfo capabilities)
        {
            if (IsAggregateFunction(maybeCountRowsNode, node, BuiltinFunctionsCore.CountRows.Name, 1) &&
                capabilities.CanDelegateSummarize(null, SummarizeMethod.CountRows, sourceTable.IsDataverseDelegation))
            {
                aggregateExpressions.Add(new FxColumnInfo(null, aliasName, isDistinct: false, aggregateOperation: SummarizeMethod.CountRows));
                return true;
            }

            return false;
        }
    }
}
