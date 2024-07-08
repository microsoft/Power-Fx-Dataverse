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

        public override DelegationParameterFeatures Features =>
            DelegationParameterFeatures.Columns |
            DelegationParameterFeatures.Filter |
            DelegationParameterFeatures.OrderBy |
            DelegationParameterFeatures.Top;

        public override string GetOdataFilter()
        {            
            var odata = ToOdataFilter(Filter);
            return odata;
        }

        public override string GetOrderBy()
        {
            // $$$ To be implemented
            return null;
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
                        ConditionOperator.GreaterEqual => "gt",
                        ConditionOperator.GreaterThan => "ge",
                        ConditionOperator.LessEqual => "lt",
                        ConditionOperator.LessThan => "le",
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
            if (obj is string str)
            {
                return EscapeOdata(str);
            }
            else if (obj is DateTime dt)
            {
                return EscapeOdata(dt);
            }
            else if (obj is float f)
            {
                return f.ToString();
            }
            else if (obj is decimal d)
            {
                return d.ToString();
            }
            else if (obj is double d2)
            {
                return d2.ToString();
            }
            else if (obj is int i)
            {
                return i.ToString();
            }

            throw new NotImplementedException($"OData type: {obj.GetType()}");
        }

        private static string EscapeOdata(string str)
        {
            // https://docs.oasis-open.org/odata/odata/v4.01/cs01/part2-url-conventions/odata-v4.01-cs01-part2-url-conventions.html#sec_URLComponents
            // ecaped single quote as 2 single quotes.
            return "'" + str.Replace("'", "''") + "'";
        }

        private static string EscapeOdata(DateTime dt)
        {
            return dt.ToLongDateString();
        }
    }
}
