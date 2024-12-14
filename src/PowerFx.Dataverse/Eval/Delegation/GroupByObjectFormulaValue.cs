// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation
{
    internal class GroupByObjectFormulaValue : ValidFormulaValue
    {
        private readonly FxGroupByNode _groupBy;

        internal FxGroupByNode GroupBy => _groupBy;

        public GroupByObjectFormulaValue(FxGroupByNode groupBy, TableType tableType)
            : base(IRContext.NotInSource(tableType))
        {
            _groupBy = groupBy;
        }

        public override void ToExpression(StringBuilder sb, FormulaValueSerializerSettings settings)
        {
            sb.Append(DescribeGroupByTransformationNode(_groupBy));
        }

        public override object ToObject()
        {
            return _groupBy;
        }

        public override void Visit(IValueVisitor visitor)
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            return DescribeGroupByTransformationNode(_groupBy);
        }

        private static string DescribeGroupByTransformationNode(FxGroupByNode groupByNode)
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
                groupingColumns.Add(property);
            }

            // Extract aggregate expressions
            foreach (var expr in groupByNode.FxAggregateExpressions)
            {
                var method = expr.AggregateType; // e.g., "sum", "min", "max", etc.
                var propertyName = expr.PropertyName;
                var alias = expr.Alias;
                aggregateExpressions.Add($"{method}({propertyName}) As {alias}");
            }

            // Construct the result string
            var allExpressions = groupingColumns.Concat(aggregateExpressions);
            string result = $"__groupBy({string.Join(", ", allExpressions)})";
            return result;
        }
    }
}
