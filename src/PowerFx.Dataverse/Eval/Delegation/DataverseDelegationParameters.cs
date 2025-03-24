// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse
{
    // DelegationParameters implemented using Xrm filter classes.    
    [Obsolete("Preview")]
    public class DataverseDelegationParameters : DelegationParameters
    {
        public const string Odata_Filter = "$filter";

        public const string Odata_OrderBy = "$orderby";

        public const string Odata_Select = "$select";

        public const string Odata_Top = "$top";

        public const string Odata_Apply = "$apply";

        public const string Odata_Count = "$count";

        // Systems can get the filter expression directrly and translate.
        public FxFilterExpression FxFilter { get; init; }

        public FilterExpression Filter => FxFilter.GetDataverseFilterExpression();

        public IList<OrderExpression> OrderBy { get; init; }

        internal ISet<LinkEntity> Relation { get; init; }

        public FxColumnMap ColumnMap { get; init; }

        public FxGroupByNode GroupBy { get; init; }

        public ISet<FxJoinNode> Joins { get; init; }

        /// <summary>
        /// Currently we only support one join, so this is a helper.
        /// </summary>
        internal FxJoinNode Join => Joins?.FirstOrDefault();

        // Use for dataverse elastic tables.
        internal string _partitionId;

        private readonly FormulaType _expectedReturnType;

        /// <summary>
        /// This is the expected RecordType Host needs to return after it performed delegation.
        /// </summary>
        public FormulaType ExpectedReturnType => _expectedReturnType;

        /// <summary>
        /// If true, Delegation will return total row count. I.e. SQL Count(*).
        /// </summary>
        public bool ReturnTotalRowCount => ColumnMap?.ReturnTotalRowCount ?? false;

        private bool HasNoFilter => FxFilter == null || FxFilter.Conditions.IsNullOrEmpty();

        private bool HasNoJoin => Joins == null || Joins.IsNullOrEmpty();

        private bool HasNoGroupBy => GroupBy == null;

        public DataverseDelegationParameters(FormulaType expectedReturnType)
        {
            _expectedReturnType = expectedReturnType;
        }

        public override DelegationParameterFeatures Features
        {
            get
            {
                DelegationParameterFeatures features = 0;

                if (FxFilter != null)
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

                if (!Joins.IsNullOrEmpty())
                {
                    features |= DelegationParameterFeatures.ApplyJoin;
                }

                if (GroupBy != null)
                {
                    features |= DelegationParameterFeatures.ApplyGroupBy;
                }
                else if (ColumnMap != null && ColumnMap.Any(column => column.AggregateMethod != SummarizeMethod.None))
                {
                    // top level aggregation without grouping.
                    features |= DelegationParameterFeatures.ApplyTopLevelAggregation;
                }

                if (ReturnTotalRowCount)
                {
                    features |= DelegationParameterFeatures.Count;
                }

                return features;
            }
        }

        public override string GetOdataFilter() => ToOdataFilter(FxFilter);

        public string GetODataGroupBy()
        {
            // Filter out columns that do NOT produce a real aggregator expression
            var aggregatorExpressions = ColumnMap?
                .Where(c => c.AggregateMethod != SummarizeMethod.None &&
                            c.AggregateMethod != SummarizeMethod.CountRows)
                .ToList();

            bool hasAggregations = !aggregatorExpressions.IsNullOrEmpty();

            var groupByProperties = GroupBy?.GroupingProperties ?? Enumerable.Empty<string>();
            bool hasGroupByProperties = !groupByProperties.IsNullOrEmpty();

            // If we have neither grouping nor aggregations, there's nothing to produce
            if (!hasGroupByProperties && !hasAggregations)
            {
                return string.Empty;
            }

            // Precompute the aggregator portion (if any).
            // e.g. "aggregate(Sales with sum as TotalSales,Quantity with max as MaxQty)"
            var aggregateClause = hasAggregations
                ? $"aggregate({string.Join(",", aggregatorExpressions.Select(TranslateNode))})"
                : string.Empty;

            // If no group-by properties but we do have aggregations,
            // return only the "aggregate(...)" portion
            if (!hasGroupByProperties)
            {
                return aggregateClause;
            }

            // Otherwise, we do have group-by properties
            // Build the "groupby((...))" portion
            var sb = new StringBuilder();
            sb.Append("groupby((");
            sb.Append(string.Join(",", groupByProperties));
            sb.Append(")");

            // If we also have aggregations, append aggregate portion
            if (hasAggregations)
            {
                sb.Append($",{aggregateClause}");
            }

            sb.Append(")");
            return sb.ToString();
        }

        private static string TranslateNode(FxColumnInfo aggExpression)
        {
            var method = aggExpression.AggregateMethod; // e.g., "sum", "min", etc.
            var propertyName = aggExpression.RealColumnName;
            var alias = aggExpression.AliasColumnName;

            // OData requires aliasing, so generate it in case of top level aggregation I.e. Sum(table, field).
            if (alias == null)
            {
                alias = $"{propertyName}_{method.ToString().ToLowerInvariant()}";
            }

            if (method == SummarizeMethod.Count)
            {
                throw new NotSupportedException("Count(Column) is not supported in DataverseDelegationParameters");
            }
            else if (method == SummarizeMethod.CountRows)
            {
                return $"$count as {alias}";
            }

            return $"{propertyName} with {method.ToString().ToLowerInvariant()} as {alias}";
        }

        public IReadOnlyDictionary<string, string> ODataElements
        {
            get
            {
                Dictionary<string, string> ode = new Dictionary<string, string>();

                string joinApply = GetOdataJoinApply();
                string filter = GetOdataFilter();
                int top = Top ?? 0;
                IEnumerable<string> select = GetColumns();
                IReadOnlyCollection<(string col, bool asc)> orderBy = GetOrderBy();
                string groupBy = GetODataGroupBy();

                if (!string.IsNullOrEmpty(joinApply))
                {
                    ode.Add(Odata_Apply, joinApply);
                }

                if (!string.IsNullOrEmpty(filter))
                {
                    ode.Add(Odata_Filter, filter);
                }

                if (!string.IsNullOrEmpty(groupBy))
                {
                    ode.Add(Odata_Apply, groupBy);
                }

                if (orderBy != null && orderBy.Any())
                {
                    ode.Add(Odata_OrderBy, string.Join(",", orderBy.Select(x => x.col + (x.asc ? string.Empty : " desc"))));
                }

                if (select != null && select.Any())
                {
                    ode.Add(Odata_Select, string.Join(",", select));
                }

                if (top > 0)
                {
                    ode.Add(Odata_Top, top.ToString());
                }

                if (ReturnTotalRowCount)
                {
                    ode.Add(Odata_Count, "true");
                }

                return ode;
            }
        }

        public override IReadOnlyCollection<string> GetColumns() => ColumnMap?.Where(ci => ci.AggregateMethod == SummarizeMethod.None).Select(ci => ci.RealColumnName).ToArray();

        // $$$ -  https://github.com/microsoft/Power-Fx-Dataverse/issues/488
        private static string ToOdataFilter(FxFilterExpression filter)
        {
            if (filter == null)
            {
                return null;
            }

            var op = filter.FilterOperator switch
            {
                FxFilterOperator.And => "and",
                FxFilterOperator.Or => "or",
                _ => throw new NotSupportedException($"Unsupported filter operator: {filter.FilterOperator}"),
            };

            if (filter.Filters?.Count > 0)
            {                
                StringBuilder sb = new StringBuilder();
                int count = 0;

                if (filter.Filters.Count > 1)
                {
                    sb.Append('(');
                }

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

                if (filter.Filters.Count > 1)
                {
                    sb.Append(')');
                }

                return sb.ToString();
            }

            if (filter.Conditions?.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                int count = 0;

                if (filter.Conditions.Count > 1)
                {
                    sb.Append('(');
                }

                foreach (var condition in filter.Conditions)
                {
                    if (count > 0)
                    {
                        sb.Append($" {op} ");
                    }

                    var fieldName = condition.AttributeName;

                    if (!condition.FieldFunctions.IsNullOrEmpty())
                    {
                        if (condition.FieldFunctions.Count() > 1)
                        {
                            throw new NotSupportedException($"Multiple field functions are not supported in DataverseDelegationParameters: {condition.FieldFunctions.Count()}");
                        }

                        var fieldFunction = condition.FieldFunctions.First();

                        if (fieldFunction != FieldFunction.None)
                        {
                            fieldName = $"{condition.FieldFunctions.First().ToString().ToLower()}({fieldName})";
                        }
                    }

                    var value = condition.Values.FirstOrDefault();

                    // OData spec: https://docs.oasis-open.org/odata/odata/v4.0/errata03/os/complete/part2-url-conventions/odata-v4.0-errata03-os-part2-url-conventions-complete.html (chapters 5.1.1 and next)
                    // Azure tables: https://learn.microsoft.com/en-us/rest/api/storageservices/querying-tables-and-entities#supported-comparison-operators

                    // OData spec, chapter 5.1.1.5.2
                    if (condition.Operator == FxConditionOperator.Contains)
                    {
                        // not supported on Azure tables but we don't support capabilities for now
                        sb.Append($"contains({fieldName},{EscapeOdata(value)})");
                    }
                    else if (condition.Operator == FxConditionOperator.Null)
                    {
                        sb.Append($"({fieldName} eq null)");
                    }
                    else if (condition.Operator == FxConditionOperator.NotNull)
                    {
                        sb.Append($"({fieldName} ne null)");
                    }
                    else if (condition.Operator == FxConditionOperator.BeginsWith)
                    {
                        sb.Append($"startswith({fieldName},{EscapeOdata(value)})");
                    }
                    else if (condition.Operator == FxConditionOperator.EndsWith)
                    {
                        sb.Append($"endswith({fieldName},{EscapeOdata(value)})");
                    }
                    else
                    {
                        string cop = condition.Operator switch
                        {
                            FxConditionOperator.GreaterEqual => "ge",
                            FxConditionOperator.GreaterThan => "gt",
                            FxConditionOperator.LessEqual => "le",
                            FxConditionOperator.LessThan => "lt",
                            FxConditionOperator.Equal => "eq",
                            FxConditionOperator.NotEqual => "ne",
                            _ => throw new NotImplementedException($"DataverseDelegationParameters don't support: {condition.Operator} operator"),
                        };

                        string odValue = EscapeOdata(value);
                        string odFilter = $"({fieldName} {cop} {odValue})";

                        sb.Append(odFilter);
                    }

                    count++;
                }

                if (filter.Conditions.Count > 1)
                {
                    sb.Append(')');
                }

                return sb.ToString();
            }

            return null;
        }

        private static string EscapeOdata(object obj)
        {
            if (obj == null)
            {
                return "null";
            }

            return obj switch
            {
                string str => EscapeOdata(str),
                bool b => b.ToString().ToLowerInvariant(),
                DateTime dt => (dt.Kind == DateTimeKind.Utc || dt.Kind == DateTimeKind.Unspecified ? dt : dt.ToUniversalTime()).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                float f => f.ToString(),
                decimal d => d.ToString(),
                double d2 => d2.ToString(),
                int i => i.ToString(),

                // OData spec, chapter 5.1.1.11.1
                Guid guid => guid.ToString("D"),
                _ => throw new NotImplementedException($"EscapeOdata type: {obj.GetType().Name} not supported")
            };
        }

        private static string EscapeOdata(string str)
        {
            // https://docs.oasis-open.org/odata/odata/v4.01/cs01/part2-url-conventions/odata-v4.01-cs01-part2-url-conventions.html#sec_URLComponents
            // escaped single quote as 2 single quotes.
            return "'" + str.Replace("'", "''") + "'";
        }

        private string GetOdataJoinApply()
        {
            if (Join == null)
            {
                return null;
            }

            return Join.JoinType switch
            {
                FxJoinType.Inner => $"join({Join.ForeignTable} as {Join.ForeignTableAlias})",
                FxJoinType.Left => $"outerjoin({Join.ForeignTable} as {Join.ForeignTableAlias})",
                FxJoinType.Right => throw new InvalidOperationException("Right join not supported yet"),

                // $$$ this operation isn't part of OData specifications
                FxJoinType.Full => $"fulljoin({Join.ForeignTable} as {Join.ForeignTableAlias})",
                _ => throw new InvalidOperationException("Invalid Joins operator")
            };
        }

        public override IReadOnlyCollection<(string, bool)> GetOrderBy()
        {
            return OrderBy?.Select(oe => (oe.AttributeName, oe.OrderType == OrderType.Ascending)).ToList();
        }

        /// <summary>
        /// Check if the query is counting the entire table without any filter, join, etc. Ie pure, select count(*) from table.
        /// </summary>
        public bool IsCountingEntireTable()
        {
            if (!ReturnTotalRowCount)
            {
                return false;
            }

            return HasNoFilter && HasNoJoin && HasNoGroupBy;
        }
    }
}
