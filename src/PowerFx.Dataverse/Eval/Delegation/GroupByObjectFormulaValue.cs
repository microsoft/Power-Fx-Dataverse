// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.OData.UriParser;
using Microsoft.OData.UriParser.Aggregation;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation
{
    internal class GroupByObjectFormulaValue : ValidFormulaValue
    {
        private readonly GroupByTransformationNode _groupByTransformationNode;

        internal GroupByTransformationNode GroupByTransformationNode => _groupByTransformationNode;

        public GroupByObjectFormulaValue(GroupByTransformationNode groupByTransformationNode)
            : base(IRContext.NotInSource(FormulaType.Void))
        {
            _groupByTransformationNode = groupByTransformationNode;
        }

        public override void ToExpression(StringBuilder sb, FormulaValueSerializerSettings settings)
        {
            sb.Append(DescribeGroupByTransformationNode(_groupByTransformationNode));
        }

        public override object ToObject()
        {
            return _groupByTransformationNode;
        }

        public override void Visit(IValueVisitor visitor)
        {
            throw new NotImplementedException();
        }

        private static string DescribeGroupByTransformationNode(GroupByTransformationNode groupByNode)
        {
            if (groupByNode == null)
            {
                return "__noopGroupBy()";
            }

            var groupingColumns = new List<string>();
            var aggregateExpressions = new List<string>();

            // Extract grouping columns
            foreach (var property in groupByNode.GroupingProperties)
            {
                ExtractGroupingPropertyNames(property, groupingColumns);
            }

            // Extract aggregate expressions
            if (groupByNode.ChildTransformations is AggregateTransformationNode aggregateNode)
            {
                foreach (var expr in aggregateNode.Expressions)
                {
                    if (expr is AggregateExpression aggregateExpr)
                    {
                        var method = aggregateExpr.Method; // e.g., "sum", "min", "max", etc.
                        var propertyName = GetPropertyName(aggregateExpr.Expression);
                        aggregateExpressions.Add($"{method}({propertyName}) As {aggregateExpr.Alias}");
                    }
                }
            }

            // Construct the result string
            var allExpressions = groupingColumns.Concat(aggregateExpressions);
            string result = $"__groupBy({string.Join(", ", allExpressions)})";
            return result;
        }

        private static void ExtractGroupingPropertyNames(GroupByPropertyNode groupingProperty, IList<string> names)
        {
            if (groupingProperty.Expression is SingleValuePropertyAccessNode propertyAccessNode)
            {
                names.Add(propertyAccessNode.Property.Name);
            }
            else if (groupingProperty.Expression is SingleValueOpenPropertyAccessNode openPropertyAccessNode)
            {
                names.Add(openPropertyAccessNode.Name);
            }
            else if (groupingProperty.Expression is SingleComplexNode complexNode)
            {
                // Handle nested properties if necessary
                names.Add(groupingProperty.Name);
            }
            else
            {
                names.Add(groupingProperty.Name);
            }
        }

        private static string GetPropertyName(SingleValueNode node)
        {
            if (node is SingleValuePropertyAccessNode propertyAccessNode)
            {
                return propertyAccessNode.Property.Name;
            }
            else if (node is SingleValueOpenPropertyAccessNode openPropertyAccessNode)
            {
                return openPropertyAccessNode.Name;
            }
            else if (node is SingleValueFunctionCallNode functionCallNode)
            {
                // Handle function calls if necessary
                return functionCallNode.Name;
            }
            else if (node is ConstantNode constantNode)
            {
                return constantNode.LiteralText;
            }
            else
            {
                return node.ToString();
            }
        }
    }
}
