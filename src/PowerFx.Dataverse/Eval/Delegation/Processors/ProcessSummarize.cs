// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Core.Types.Enums;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;
using RecordNode = Microsoft.PowerFx.Core.IR.Nodes.RecordNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        private RetVal ProcessSummarize(CallNode node, RetVal tableArg, Context context)
        {
            // nested summarize is not supported and renames are not supported.
            if (tableArg.HasGroupByNode || tableArg.HasColumnMap)
            {
                return CreateNotSupportedErrorAndReturn(node, tableArg);
            }

            var groupByProperties = new List<string>();
            var aggregateExpressions = new List<FxAggregateExpression>();

            var delegationInfo = tableArg.TableType.ToRecord().TryGetCapabilities(out var capabilities);

            // Process arguments for group by or aggregate logic
            foreach (var arg in node.Args.Skip(1))
            {
                if (arg is TextLiteralNode columnName)
                {
                    var columnNameString = columnName.LiteralValue;
                    if (capabilities.CanDelegateSummarize(columnNameString, tableArg.IsDataverseDelegation))
                    {
                        groupByProperties.Add(columnNameString);
                    }
                    else
                    {
                        return CreateNotSupportedErrorAndReturn(node, tableArg);
                    }
                }
                else if (arg is LazyEvalNode lazyEvalNode && lazyEvalNode.Child is RecordNode scope && TryProcessAggregateExpression(node, scope, context, tableArg, aggregateExpressions, capabilities))
                {
                    continue;
                }
                else
                {
                    return CreateNotSupportedErrorAndReturn(node, tableArg);
                }
            }

            var groupByNode = new FxGroupByNode(groupByProperties, aggregateExpressions);
            var topCount = tableArg.HasTopCount ? tableArg.TopCountOrDefault : null;

            return new RetVal(_hooks, node, tableArg._sourceTableIRNode, tableArg.TableType, filter: tableArg.Filter, orderBy: tableArg.OrderBy, count: topCount, (int)tableArg.MaxRows.LiteralValue, tableArg.ColumnMap, groupByNode);
        }

        private static bool TryProcessAggregateExpression(CallNode node, RecordNode scope, Context context, RetVal sourceTable, IList<FxAggregateExpression> aggregateExpressions, TableDelegationInfo capabilities)
        {
            var aliasName = scope.Fields.First().Key.Value;

            if (scope.Fields.First().Value is CallNode callNode)
            {
                // Try single-field aggregations (Sum, Min, Max, Avg)
                if (TryAddSingleFieldAggregateExpression(callNode, node, context, sourceTable, aliasName, aggregateExpressions, capabilities, SummarizeMethod.Sum, BuiltinFunctionsCore.SumT.Name) ||
                    TryAddSingleFieldAggregateExpression(callNode, node, context, sourceTable, aliasName, aggregateExpressions, capabilities, SummarizeMethod.Min, BuiltinFunctionsCore.MinT.Name) ||
                    TryAddSingleFieldAggregateExpression(callNode, node, context, sourceTable, aliasName, aggregateExpressions, capabilities, SummarizeMethod.Max, BuiltinFunctionsCore.MaxT.Name) ||
                    TryAddSingleFieldAggregateExpression(callNode, node, context, sourceTable, aliasName, aggregateExpressions, capabilities, SummarizeMethod.Average, BuiltinFunctionsCore.AverageT.Name))
                {
                    return true;
                }

                // Try CountIf
                if (TryAddCountIfAggregateExpression(callNode, node, context, sourceTable, aliasName, aggregateExpressions, capabilities, sourceTable))
                {
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

        private static bool TryAddSingleFieldAggregateExpression(CallNode maybeNode, CallNode summarizeNode, Context context, RetVal sourceTable, string aliasName, IList<FxAggregateExpression> aggregateExpressions, TableDelegationInfo capabilities, SummarizeMethod method, string functionName)
        {
            if (!IsAggregateFunction(maybeNode, summarizeNode, functionName, 2))
            {
                return false;
            }

            var fieldArg = maybeNode.Args[1] as LazyEvalNode;
            var predicateContext = context.GetContextForPredicateEval(maybeNode, sourceTable);

            if (DelegationIRVisitor.TryGetFieldNameFromScopeNode(predicateContext, fieldArg.Child, out var fieldName) &&
                capabilities.CanDelegateSummarize(fieldName, method, sourceTable.IsDataverseDelegation))
            {
                aggregateExpressions.Add(new FxAggregateExpression(fieldName, method, aliasName));
                return true;
            }

            return false;
        }

        // CountIf(ThisGroup, Not(IsBlank(fieldName)))
        private static bool TryAddCountIfAggregateExpression(CallNode maybeCountIfNode, CallNode node, Context context, RetVal sourceTable, string aliasName, IList<FxAggregateExpression> aggregateExpressions, TableDelegationInfo capabilities, RetVal tableArg)
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
                    if (DelegationIRVisitor.TryGetFieldNameFromScopeNode(predicateContext, maybeIsBlankCallNode.Args[0], out var fieldName) &&
                        capabilities.CanDelegateSummarize(fieldName, SummarizeMethod.Count, tableArg.IsDataverseDelegation))
                    {
                        aggregateExpressions.Add(new FxAggregateExpression(fieldName, SummarizeMethod.Count, aliasName));
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryAddCountRowsAggregateExpression(CallNode maybeCountRowsNode, CallNode node, Context context, RetVal sourceTable, string aliasName, IList<FxAggregateExpression> aggregateExpressions, TableDelegationInfo capabilities)
        {
            if (!IsAggregateFunction(maybeCountRowsNode, node, BuiltinFunctionsCore.CountRows.Name, 1))
            {
                return false;
            }

            if (capabilities.CanDelegateSummarize(SummarizeMethod.CountRows, sourceTable.IsDataverseDelegation))
            {
                aggregateExpressions.Add(new FxAggregateExpression(null, SummarizeMethod.CountRows, aliasName));
                return true;
            }

            return false;
        }
    }
}
