using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse
{
    // DelegationParameters implemented using Xrm filter classes.
    [Obsolete("Preview")]
    public class DataverseDelegationParameters : DelegationParameters
    {
        // Systems can get the filter expression directrly and translate.
        public FilterExpression Filter { get; init; }

        public IList<OrderExpression> OrderBy { get; init; }

        internal ISet<LinkEntity> _relation { get; init; }

        // Top is count.

        internal IEnumerable<string> _columnSet { get; init; }
        internal bool _isDistinct { get; init; }

        // Use for dataverse elastic tables.
        internal string _partitionId;

        public override DelegationParameterFeatures Features
        {
            get
            {
                DelegationParameterFeatures features = 0;
                
                if (Filter != null) 
                {
                    features |= DelegationParameterFeatures.Filter | DelegationParameterFeatures.Columns;
                }
                
                if (OrderBy != null) 
                { 
                    features |= DelegationParameterFeatures.Sort;  // $$$ Should be renamed OrderBy
                }
                
                if (Top > 0) 
                { 
                    features |= DelegationParameterFeatures.Top; 
                }

                return features;

            }
        }

        public override string GetOdataFilter()
        {            
            var odata = ToOdataFilter(Filter);
            return odata;
        }

        public override IReadOnlyCollection<string> GetColumns()
        {
            if (this._columnSet == null)
            {
                return null;
            }
            var columns = this._columnSet.ToArray();
            return columns.Length == 0 ? null : columns;
        }

        // $$$ -  https://github.com/microsoft/Power-Fx-Dataverse/issues/488
        private static string ToOdataFilter(FilterExpression filter)
        {
            if (filter.Filters?.Count > 0)
            {
                var op = filter.FilterOperator switch
                {
                    LogicalOperator.And => "and",
                    LogicalOperator.Or => "or",
                    _ => throw new NotSupportedException($"Unsupported filter operator: {filter.FilterOperator}"),
                };

                StringBuilder sb = new StringBuilder();

                int count = 0;

                sb.Append("(");
                foreach (var sub in filter.Filters)
                {
                    if (count > 0)
                    {
                        sb.Append($" {op} ");
                    }

                    var odStr = ToOdataFilter(sub);
                    sb.Append(odStr);

                    count++;
                }
                sb.Append(")");

                return sb.ToString();
            }

            if (filter.Conditions?.Count > 0)
            {
                foreach (var condition in filter.Conditions)
                {
                    var fieldName = condition.AttributeName;
                    var value = condition.Values.FirstOrDefault();

                    // https://learn.microsoft.com/en-us/rest/api/storageservices/querying-tables-and-entities#supported-comparison-operators

                    var op = condition.Operator switch
                    {
                        ConditionOperator.GreaterEqual => "ge",
                        ConditionOperator.GreaterThan => "gt",
                        ConditionOperator.LessEqual => "le",
                        ConditionOperator.LessThan => "lt",
                        ConditionOperator.Equal => "eq",
                        ConditionOperator.NotEqual => "ne",
                        _ => throw new NotImplementedException($"AzureTable can't delegate: {condition.Operator}"),
                    };

                    string odValue = EscapeOdata(value);
                    string odFilter = $"({fieldName} {op} {odValue})";

                    return odFilter;
                }
            }

            return null;
        }

        private static string EscapeOdata(object obj)
        {
            return obj switch
            {
                string str => EscapeOdata(str),
                DateTime dt => (dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime()).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                float f => f.ToString(),
                decimal d => d.ToString(),
                double d2 => d2.ToString(),
                int i => i.ToString(),
                _ => throw new NotImplementedException($"OData type: {obj.GetType()}")
            };
        }

        private static string EscapeOdata(string str)
        {
            // https://docs.oasis-open.org/odata/odata/v4.01/cs01/part2-url-conventions/odata-v4.01-cs01-part2-url-conventions.html#sec_URLComponents
            // ecaped single quote as 2 single quotes.
            return "'" + str.Replace("'", "''") + "'";
        }

        public override IReadOnlyCollection<(string, bool)> GetOrderBy()
        {
            return OrderBy?.Select(oe => (oe.AttributeName, oe.OrderType == OrderType.Ascending)).ToList();
        }
    }
}
