// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
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

        // Systems can get the filter expression directrly and translate.
        internal FxFilterExpression FxFilter { get; init; }

        public FilterExpression Filter => FxFilter.GetDataverseFilterExpression();

        public IList<OrderExpression> OrderBy { get; init; }

        internal ISet<LinkEntity> Relation { get; init; }

        public ColumnMap ColumnMap { get; init; }

        public LinkEntity Join { get; init; }

        public IEnumerable<NamedValue> JoinColumns { get; init; }

        // Use for dataverse elastic tables.
        internal string _partitionId;

        public DataverseDelegationParameters()
        {
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

                return features;
            }
        }

        public override string GetOdataFilter() => ToOdataFilter(FxFilter);

        public IReadOnlyDictionary<string, string> ODataElements
        {
            get
            {
                Dictionary<string, string> ode = new Dictionary<string, string>();

                string apply = GetApply();
                string filter = GetOdataFilter();
                int top = Top ?? 0;
                IReadOnlyCollection<string> select = GetColumns();
                IReadOnlyCollection<(string col, bool asc)> orderBy = GetOrderBy();

                if (!string.IsNullOrEmpty(apply))
                {
                    ode.Add(Odata_Apply, apply);
                }

                if (!string.IsNullOrEmpty(filter))
                {
                    ode.Add(Odata_Filter, filter);
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

                return ode;
            }
        }

        public override IReadOnlyCollection<string> GetColumns() => ColumnMap?.Columns;

        // $$$ -  https://github.com/microsoft/Power-Fx-Dataverse/issues/488
        private static string ToOdataFilter(FxFilterExpression filter)
        {
            if (filter.Filters?.Count > 0)
            {
                var op = filter.FilterOperator switch
                {
                    FxFilterOperator.And => "and",
                    FxFilterOperator.Or => "or",
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
                        return $"contains({fieldName},{EscapeOdata(value)})";
                    }
                    else if (condition.Operator == FxConditionOperator.Null)
                    {
                        return $"({fieldName} eq null)";
                    }
                    else if (condition.Operator == FxConditionOperator.NotNull)
                    {
                        return $"({fieldName} ne null)";
                    }
                    else if (condition.Operator == FxConditionOperator.BeginsWith)
                    {
                        return $"startswith({fieldName},{EscapeOdata(value)})";
                    }
                    else if (condition.Operator == FxConditionOperator.EndsWith)
                    {
                        return $"endswith({fieldName},{EscapeOdata(value)})";
                    }
                    
                    string op = condition.Operator switch
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
                    string odFilter = $"({fieldName} {op} {odValue})";

                    return odFilter;
                }
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

        private string GetApply()
        {
            if (Join == null)
            {
                return null;
            }

            return $"join({Join.LinkToEntityName} as {Join.EntityAlias})";
        }

        public override IReadOnlyCollection<(string, bool)> GetOrderBy()
        {
            return OrderBy?.Select(oe => (oe.AttributeName, oe.OrderType == OrderType.Ascending)).ToList();
        }        
    }
}
