// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Core.Types.Enums;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Syntax;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;
using RecordNode = Microsoft.PowerFx.Core.IR.Nodes.RecordNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        private RetVal ProcessSummarize(CallNode node, RetVal tableArg, Context context)
        {
            // nested summarize is not supported.
            if (tableArg.GroupByNode != null)
            {
                return CreateNotSupportedErrorAndReturn(node, tableArg);
            }

            // Initialize GroupBy and Aggregate collections
            var groupByProperties = new List<string>();
            var aggregateExpressions = new List<FxAggregateExpression>();

            // Process arguments for group by or aggregate logic
            foreach (var arg in node.Args.Skip(1))
            {
                if (arg is TextLiteralNode columnName)
                {
                    // Handle GroupBy column
                    groupByProperties.Add(columnName.LiteralValue);
                }
                else if (arg is LazyEvalNode lazyEvalNode 
                    && lazyEvalNode.Child is RecordNode scope
                    && !TryProcessAggregateExpression(node, scope, context, tableArg, aggregateExpressions))
                {
                    return CreateNotSupportedErrorAndReturn(node, tableArg);
                }
            }

            var groupByNode = new FxGroupByNode(groupByProperties, aggregateExpressions);

            // Return a new RetVal with updated transformations, it should include all the previous transformations.
            var result = new RetVal(
                tableArg.Hooks,
                node,
                tableArg._sourceTableIRNode,
                tableArg.TableType,
                filter: tableArg.Filter,
                orderBy: tableArg.OrderBy,
                count: tableArg.TopCountOrDefault,
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
            IList<FxAggregateExpression> aggregateExpressions)
        {
            var aliasName = scope.Fields.First().Key.Value;

            if (scope.Fields.First().Value is CallNode callNode && IsSumFunction(callNode, node))
            {
                return TryAddSumAggregateExpression(callNode, context, sourceTable, aliasName, aggregateExpressions);
            }
                
            return false;
        }

        private static bool IsSumFunction(CallNode aggregateExpressionNode, CallNode groupByNode)
        {
            return aggregateExpressionNode.Function.Name == BuiltinFunctionsCore.SumT.Name
                   && aggregateExpressionNode.Args.Count == 2
                   && aggregateExpressionNode.Args[0] is ScopeAccessNode scopeNode
                   && aggregateExpressionNode.Args[1] is LazyEvalNode
                   && scopeNode.Value is ScopeAccessSymbol scopeSymbol
                   && scopeSymbol.Name == "ThisGroup"
                   && scopeSymbol.Parent.Id == groupByNode.Scope.Id;
        }

        private static bool TryAddSumAggregateExpression(
            CallNode callNode,
            Context context,
            RetVal sourceTable,
            string aliasName,
            IList<FxAggregateExpression> aggregateExpressions)
        {
            var sumArg = callNode.Args[1] as LazyEvalNode;
            var predicateContext = context.GetContextForPredicateEval(callNode, sourceTable);

            if (DelegationIRVisitor.TryGetFieldNameFromScopeNode(predicateContext, sumArg.Child, out var fieldName))
            {
                aggregateExpressions.Add(new FxAggregateExpression(fieldName, FxAggregateType.Sum, aliasName));
                return true;
            }

            return false;
        }
    }
}
