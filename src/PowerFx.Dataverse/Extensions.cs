// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse
{
    internal static class Extensions
    {
        public static bool IsElasticTable(this EntityMetadata entityMetadata)
        {
            return entityMetadata.DataProviderId == System.Guid.Parse("1d9bde74-9ebd-4da9-8ff5-aa74945b9f74");
        }

        public static bool TryGetPrimaryKeyFieldName2(this RecordType type, out IEnumerable<string> primaryKeyFieldNames)
        {
            // dataverse types has embedded metadata.
            if (DelegationUtility.TryGetEntityMetadata(type, out var entityMetadata))
            {
                primaryKeyFieldNames = new List<string>() { entityMetadata.PrimaryIdAttribute };
                return true;
            }

            return type.TryGetPrimaryKeyFieldName(out primaryKeyFieldNames);
        }

        private static XrmAggregateType FxToXRMAggregateType(SummarizeMethod aggregateType)
        {
            return aggregateType switch
            {
                SummarizeMethod.None => XrmAggregateType.None,
                SummarizeMethod.Average => XrmAggregateType.Avg,
                SummarizeMethod.Count => XrmAggregateType.Count,
                SummarizeMethod.Max => XrmAggregateType.Max,
                SummarizeMethod.Min => XrmAggregateType.Min,
                SummarizeMethod.Sum => XrmAggregateType.Sum,
                _ => throw new NotSupportedException($"Unsupported aggregate type {aggregateType}"),
            };
        }

        internal static ColumnSet ToXRMColumnSet(this FxColumnMap columnMap)
        {
            if (columnMap == null)
            {
                return new ColumnSet(true);
            }

            if (columnMap.IsEmpty && !columnMap.ReturnTotalRowCount)
            {
                throw new InvalidOperationException("FxColumnMap is empty, that means it will not fetch any columns... Bug found!");
            }

            ColumnSet columnSet = new ColumnSet(false);

            foreach (var columnInfo in columnMap.ColumnInfoMap.Values)
            {
                columnSet.AttributeExpressions.Add(new XrmAttributeExpression()
                {
                    AttributeName = columnInfo.RealColumnName,
                    Alias = columnInfo.AliasColumnName ?? columnInfo.RealColumnName,
                    HasGroupBy = columnInfo.IsGroupByProperty,
                    AggregateType = FxToXRMAggregateType(columnInfo.AggregateMethod)
                });
            }

            return columnSet;
        }

        internal static ColumnSet ToXRMColumnSet(this IEnumerable<string> columns)
        {
            if (columns == null)
            {
                return new ColumnSet(true);
            }

            if (!columns.Any())
            {
                throw new InvalidOperationException("Columns is empty, that means it will not fetch any columns... Bug found!");
            }

            return new ColumnSet(columns.ToArray());
        }

        internal static bool HasDistinct(this DataverseDelegationParameters dataverseDelegationParameters)
        {
            if (dataverseDelegationParameters?.ColumnMap?.HasDistinct() == true)
            {
                return true;
            }

            if (dataverseDelegationParameters?.Join?.RightTablColumnMap?.HasDistinct() == true)
            {
                return true;
            }

            return false;
        }
    }
}
