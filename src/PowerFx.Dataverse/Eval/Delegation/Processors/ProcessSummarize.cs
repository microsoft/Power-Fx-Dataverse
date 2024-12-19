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

            // Initialize GroupBy and Aggregate collections
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
                else if (arg is LazyEvalNode lazyEvalNode
                    && lazyEvalNode.Child is RecordNode scope
                    && TryProcessAggregateExpression(node, scope, context, tableArg, aggregateExpressions, capabilities))
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

            // Return a new RetVal with updated transformations, it should include all the previous transformations.
            var result = new RetVal(
                _hooks,
                node,
                tableArg._sourceTableIRNode,
                tableArg.TableType,
                filter: tableArg.Filter,
                orderBy: tableArg.OrderBy,
                count: topCount,
                (int)tableArg.MaxRows.LiteralValue,
                tableArg.ColumnMap,
                groupByNode);

            return result;
        }

        private static bool TryProcessAggregateExpression(
            CallNode node,
            RecordNode scope,
            Context context,
            RetVal sourceTable,
            IList<FxAggregateExpression> aggregateExpressions,
            TableDelegationInfo capbilities)
        {
            var aliasName = scope.Fields.First().Key.Value;

            if (scope.Fields.First().Value is CallNode callNode)
            {
                if (IsSumFunction(callNode, node))
                {
                    return TryAddSumAggregateExpression(callNode, context, sourceTable, aliasName, aggregateExpressions, capbilities, sourceTable);
                }
                else if (IsCountIfFunction(callNode, node))
                {
                    return TryAddCountIfAggregateExpression(callNode, context, sourceTable, aliasName, aggregateExpressions, capbilities, sourceTable);
                }
            }

            return false;
        }

        private static bool IsSumFunction(CallNode aggregateExpressionNode, CallNode groupByNode)
        {
            return aggregateExpressionNode.Function.Name == BuiltinFunctionsCore.SumT.Name
                   && aggregateExpressionNode.Args.Count == 2
                   && aggregateExpressionNode.Args[0] is ScopeAccessNode scopeNode
                   && scopeNode.Value is ScopeAccessSymbol scopeSymbol
                   && scopeSymbol.Name == FunctionThisGroupScopeInfo.ThisGroup.Value
                   && scopeSymbol.Parent.Id == groupByNode.Scope.Id;
        }

        private static bool IsCountIfFunction(CallNode aggregateExpressionNode, CallNode groupByNode)
        {
            return aggregateExpressionNode.Function.Name == BuiltinFunctionsCore.CountIf.Name
                   && aggregateExpressionNode.Args.Count == 2
                   && aggregateExpressionNode.Args[0] is ScopeAccessNode scopeNode
                   && scopeNode.Value is ScopeAccessSymbol scopeSymbol
                   && scopeSymbol.Name == FunctionThisGroupScopeInfo.ThisGroup.Value
                   && scopeSymbol.Parent.Id == groupByNode.Scope.Id;
        }

        private static bool TryAddSumAggregateExpression(
            CallNode callNode,
            Context context,
            RetVal sourceTable,
            string aliasName,
            IList<FxAggregateExpression> aggregateExpressions,
            TableDelegationInfo capbilities,
            RetVal tableArg)
        {
            var sumArg = callNode.Args[1] as LazyEvalNode;
            var predicateContext = context.GetContextForPredicateEval(callNode, sourceTable);

            if (DelegationIRVisitor.TryGetFieldNameFromScopeNode(predicateContext, sumArg.Child, out var fieldName) &&
                capbilities.CanDelegateSummarize(fieldName, SummarizeMethod.Sum, tableArg.IsDataverseDelegation))
            {
                aggregateExpressions.Add(new FxAggregateExpression(fieldName, SummarizeMethod.Sum, aliasName));
                return true;
            }

            return false;
        }

        // CountIf(ThisGroup, Not(IsBlank(fieldName)))
        private static bool TryAddCountIfAggregateExpression(
            CallNode callNode,
            Context context,
            RetVal sourceTable,
            string aliasName,
            IList<FxAggregateExpression> aggregateExpressions,
            TableDelegationInfo capbilities,
            RetVal tableArg)
        {
            var maybeNotCall = callNode.Args[1] as LazyEvalNode;

            if (maybeNotCall?.Child is CallNode maybeNotCallNode && maybeNotCallNode.Function.Name == BuiltinFunctionsCore.Not.Name)
            {
                if (maybeNotCallNode.Args[0] is CallNode maybeIsBlankCallNode && maybeIsBlankCallNode.Function.Name == BuiltinFunctionsCore.IsBlank.Name)
                {
                    var countIfArg = maybeIsBlankCallNode.Args[0] as LazyEvalNode;
                    var predicateContext = context.GetContextForPredicateEval(callNode, sourceTable);
                    if (DelegationIRVisitor.TryGetFieldNameFromScopeNode(predicateContext, maybeIsBlankCallNode.Args[0], out var fieldName) &&
                        capbilities.CanDelegateSummarize(fieldName, SummarizeMethod.Count, tableArg.IsDataverseDelegation))
                    {
                        aggregateExpressions.Add(new FxAggregateExpression(fieldName, SummarizeMethod.Count, aliasName));
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
