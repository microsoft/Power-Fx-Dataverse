// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Dataverse.Eval.Core;
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

        /// <summary>
        /// Converts FxColumnMap to ColumnSet.
        /// </summary>
        /// <param name="columnMap"></param>
        /// <param name="fxGroupByNode"></param>
        internal static ColumnSet ToXRMColumnSet(this FxColumnMap columnMap, FxGroupByNode fxGroupByNode = null)
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
                var hasGroupBy = fxGroupByNode?.Contains(columnInfo.RealColumnName) == true;
                var hasAliasing = !string.IsNullOrEmpty(columnInfo.AliasColumnName);
                var hasAggregate = columnInfo.AggregateMethod != SummarizeMethod.None;
                if (hasAliasing)
                {
                    columnSet.AttributeExpressions.Add(new XrmAttributeExpression()
                    {
                        AttributeName = columnInfo.RealColumnName,
                        Alias = columnInfo.AliasColumnName,
                        HasGroupBy = fxGroupByNode?.Contains(columnInfo.RealColumnName) == true,
                        AggregateType = FxToXRMAggregateType(columnInfo.AggregateMethod)
                    });
                }
                else if (hasGroupBy || hasAggregate)
                {
                    columnSet.AttributeExpressions.Add(new XrmAttributeExpression()
                    {
                        AttributeName = columnInfo.RealColumnName,
                        Alias = columnInfo.RealColumnName, // DV doesn't allow this to be null, so we have to set it.
                        HasGroupBy = fxGroupByNode?.Contains(columnInfo.RealColumnName) == true,
                        AggregateType = FxToXRMAggregateType(columnInfo.AggregateMethod)
                    });
                }
                else
                {
                    columnSet.AddColumns(columnInfo.RealColumnName);
                }
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

        internal static DependencyInfo ApplyDependencyInfoScan(this CheckResult checkResult, CdsEntityMetadataProvider metadataProvider)
        {
            var info = checkResult.ApplyDependencyInfoScan();
            var newInfo = new DependencyInfo();

            foreach (var kvp in info.Dependencies)
            {
                newInfo.Dependencies[kvp.Key] = new HashSet<string>();

                foreach (var fieldLogicalName in kvp.Value)
                {
                    if (metadataProvider.TryGetXrmEntityMetadata(kvp.Key, out var entityMetadata))
                    {
                        // Normal case.
                        if (entityMetadata.TryGetAttribute(fieldLogicalName, out _))
                        {
                            newInfo.Dependencies[kvp.Key].Add(fieldLogicalName);
                        }

                        // Relationship
                        else if (entityMetadata.TryGetRelationship(fieldLogicalName, out var realName))
                        {
                            newInfo.Dependencies[kvp.Key].Add(realName);
                        }

                        // It can be Navigation property in case of dot walking.
                        else
                        {
                            var navigationRelation = entityMetadata.ManyToOneRelationships.FirstOrDefault(r => r.ReferencedEntityNavigationPropertyName == fieldLogicalName);

                            if (navigationRelation != null)
                            {
                                newInfo.Dependencies[kvp.Key].Add(navigationRelation.ReferencingAttribute);
                            }
                        }
                    }
                }
            }

            return newInfo;
        }

        internal static LinkEntity ToXRMLinkEntity(this FxJoinNode fxJoinNode)
        {
            JoinOperator joinOperator = ToJoinOperator(fxJoinNode.JoinType);

            // Joins between source & foreign table, using equality comparison between 'from' & 'to' attributes, with specified JOIN operator
            // EntityAlias is used in OData $apply=join(foreignTable as <entityAlias>) and DV Entity attribute names will be prefixed with this alias
            // hence the need to rename columns with a columnMap afterwards
            LinkEntity linkEntity = new LinkEntity(fxJoinNode.SourceTable, fxJoinNode.ForeignTable, fxJoinNode.FromAttribute, fxJoinNode.ToAttribute, joinOperator);
            linkEntity.EntityAlias = fxJoinNode.ForeignTableAlias;

            ColumnSet columnSet;
            if (fxJoinNode.RightTablColumnMap.IsEmpty)
            {
                // if column is empty, we are joining in expression but ShowColumns/similar eliminated the right columns.
                columnSet = new ColumnSet(fxJoinNode.ToAttribute);
            }
            else
            {
                columnSet = fxJoinNode.RightTablColumnMap.ToXRMColumnSet();
            }

            linkEntity.Columns = columnSet;
            return linkEntity;
        }

        private static JoinOperator ToJoinOperator(FxJoinType joinType)
        {
            return joinType switch
            {
                FxJoinType.Inner => JoinOperator.Inner,
                FxJoinType.Left => JoinOperator.LeftOuter,
                FxJoinType.Right => JoinOperator.In,
                FxJoinType.Full => JoinOperator.All,
                _ => throw new InvalidOperationException($"Unknown JoinType {joinType}")
            };
        }
    }
}
