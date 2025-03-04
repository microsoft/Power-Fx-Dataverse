// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Linq;
using System.Text.Json;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.Functions.Delegation.DelegationMetadata;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using IRCallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation
{
    // See PowerApps-Client\src\Cloud\DocumentServer.Core\Document\DataToControls\MetadataUtils.cs
    internal class DelegationUtility
    {
        public static RelationMetadata DeserializeRelatioMetadata(string serializedMetadata)
        {
            var relationshipObj = JsonSerializer.Deserialize<RelationMetadata>(serializedMetadata, DelegationIRVisitor._jsonSerializerDefaultOptions);
            return relationshipObj;
        }

        public static string SerializeRelationMetadata(RelationMetadata metadata)
        {
            var serializedMetadata = JsonSerializer.Serialize(metadata, DelegationIRVisitor._jsonSerializerDefaultOptions);
            return serializedMetadata;
        }

        public static bool TryGetEntityMetadata(TableType tableType, out EntityMetadata entityMetadata)
        {
            return TryGetEntityMetadata(tableType._type, out entityMetadata);
        }

        public static bool TryGetEntityMetadata(RecordType recordType, out EntityMetadata entityMetadata)
        {
            return TryGetEntityMetadata(recordType._type, out entityMetadata);
        }

        private static bool TryGetEntityMetadata(DType type, out EntityMetadata entityMetadata)
        {
            var tableDS = type.AssociatedDataSources.FirstOrDefault();
            if (tableDS != null)
            {
                var tableLogicalName = tableDS.TableMetadata.Name; // logical name
                if (tableDS.DataEntityMetadataProvider is CdsEntityMetadataProvider m2)
                {
                    if (m2.TryGetXrmEntityMetadata(tableLogicalName, out var metadata))
                    {
                        entityMetadata = metadata;
                        return true;
                    }
                }
            }

            entityMetadata = null;
            return false;
        }

        public static bool IsElasticTable(TableType tableType)
        {
            if (TryGetEntityMetadata(tableType, out var entityMetadata))
            {
                return entityMetadata.IsElasticTable();
            }

            return false;
        }

        public static bool CanDelegateDistinct(string fieldName, FilterOpMetadata filterCapabilities)
        {
            return filterCapabilities?.IsDelegationSupportedByColumn(DPath.Root.Append(new DName(fieldName)), DelegationCapability.Distinct) == true;
        }

        public static bool CanDelegateBinaryOp(string fieldName, BinaryOpKind op, FilterOpMetadata filterCapabilities, FxColumnMap columnMap)
        {
            if (columnMap?.TryGetColumnInfo(fieldName, out var columnInfo) == true)
            {
                fieldName = columnInfo.RealColumnName;
            }

            bool b = op == BinaryOpKind.Invalid /* Starts/EndsWith */ || (filterCapabilities?.IsBinaryOpInDelegationSupportedByColumn(ToBinaryOp(op), DPath.Root.Append(new DName(fieldName))) != false);

            return b;
        }

        public static bool CanDelegateSort(FxColumnInfo fieldInfo, bool isAscending, SortOpMetadata sortCapabilities)
        {
            // $$$ Can't delegate sort if the field is being aggregated https://github.com/microsoft/Power-Fx-Dataverse/issues/593
            if (fieldInfo.AggregateMethod != SummarizeMethod.None)
            {
                return false;
            }

            bool? canSortCapability = sortCapabilities?.IsDelegationSupportedByColumn(DPath.Root.Append(new DName(fieldInfo.RealColumnName)), DelegationCapability.Sort);
            bool? canSortAscendingOnlyCapability = sortCapabilities?.IsDelegationSupportedByColumn(DPath.Root.Append(new DName(fieldInfo.RealColumnName)), DelegationCapability.SortAscendingOnly);

            // if we can't get capabilities, we can't delegate
            if (canSortCapability == null || canSortAscendingOnlyCapability == null)
            {
                return false;
            }
            else if (isAscending)
            {
                // if ascending order, either Sort or SortAscendingOnly are OK
                if (canSortCapability == false && canSortAscendingOnlyCapability == false)
                {
                    return false;
                }
            }
            else if (canSortAscendingOnlyCapability == true)
            {
                // if descending order with SortAscendingOnly, we can't delegate 
                return false;
            }

            return true;
        }

        public static bool CanDelegateJoin(FxJoinType joinType, IDelegationMetadata delegationCapability)
        {
            DelegationCapability joinTypeCapability = joinType switch
            {
                FxJoinType.Inner => DelegationCapability.JoinInner,
                FxJoinType.Left => DelegationCapability.JoinLeft,
                FxJoinType.Right => DelegationCapability.JoinRight,
                FxJoinType.Full => DelegationCapability.JoinFull,
                _ => throw new InvalidOperationException($"Invalid joinType {joinType}")
            };

            return delegationCapability.TableCapabilities.HasCapability(joinTypeCapability.Capabilities);
        }

        public static bool CanDelegateFirst(IDelegationMetadata delegationMetadata)
        {
            // [MS-ODATA]
            // https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-odata/505b6322-c57f-4c37-94ef-daf8b6e2abd3
            // If the data service URI contains a $top query option, but does not contain an $orderby option,
            // then the entities in the set MUST first be fully ordered by the data service.
            // Such a full order SHOULD be obtained by sorting the entities based on their EntityKey values.
            // While no ordering semantics are mandated, a data service MUST always use the same semantics to obtain
            // a full ordering across requests.
            return delegationMetadata?.SortDelegationMetadata != null;
        }

        /// <summary>
        /// Creates a Datetime call node.
        /// </summary>
        /// <param name="yearNode">Year IR node.</param>
        /// <param name="month">Month value.</param>
        /// <param name="day">Day value.</param>
        /// <param name="hour">Hour value.</param>
        /// <param name="minute">Minute value.</param>
        /// <param name="second">Second value.</param>
        /// <param name="millisecond">Millisencond value.</param>
        /// <returns></returns>
        private static IRCallNode CreateDateTimeCallNode(IntermediateNode yearNode, int month, int day, int hour, int minute, int second, int millisecond)
        {
            var zeroLitNode = new NumberLiteralNode(IRContext.NotInSource(FormulaType.Number), 0d);

            return new IRCallNode(
                    IRContext.NotInSource(FormulaType.DateTime),
                    BuiltinFunctionsCore.DateTime,
                    new IRCallNode(IRContext.NotInSource(FormulaType.Number), BuiltinFunctionsCore.Coalesce, new IRCallNode(IRContext.NotInSource(FormulaType.Number), BuiltinFunctionsCore.Float, yearNode), zeroLitNode),
                    new IRCallNode(IRContext.NotInSource(FormulaType.Number), BuiltinFunctionsCore.Coalesce, new NumberLiteralNode(IRContext.NotInSource(FormulaType.Number), month), zeroLitNode),
                    new IRCallNode(IRContext.NotInSource(FormulaType.Number), BuiltinFunctionsCore.Coalesce, new NumberLiteralNode(IRContext.NotInSource(FormulaType.Number), day), zeroLitNode),
                    new IRCallNode(IRContext.NotInSource(FormulaType.Number), BuiltinFunctionsCore.Coalesce, new NumberLiteralNode(IRContext.NotInSource(FormulaType.Number), hour), zeroLitNode),
                    new IRCallNode(IRContext.NotInSource(FormulaType.Number), BuiltinFunctionsCore.Coalesce, new NumberLiteralNode(IRContext.NotInSource(FormulaType.Number), minute), zeroLitNode),
                    new IRCallNode(IRContext.NotInSource(FormulaType.Number), BuiltinFunctionsCore.Coalesce, new NumberLiteralNode(IRContext.NotInSource(FormulaType.Number), second), zeroLitNode),
                    new IRCallNode(IRContext.NotInSource(FormulaType.Number), BuiltinFunctionsCore.Coalesce, new NumberLiteralNode(IRContext.NotInSource(FormulaType.Number), millisecond), zeroLitNode));
        }

        /// <summary>
        /// Creates the earliest possible date  and time of a year.
        /// </summary>
        /// <param name="yearNode">Year value.</param>
        /// <returns></returns>
        public static IRCallNode CreateEarliestDateTime(IntermediateNode yearNode)
        {
            return CreateDateTimeCallNode(yearNode, 1, 1, 0, 0, 0, 0);
        }

        /// <summary>
        /// Creates the latest possible date and time of a year.
        /// </summary>
        /// <param name="yearNode">Year value.</param>
        /// <returns></returns>
        public static IRCallNode CreateLatestDateTime(IntermediateNode yearNode)
        {
            return CreateDateTimeCallNode(yearNode, 12, 31, 23, 59, 59, 999);
        }

        internal static ConditionOperator ConvertToXRMConditionOperator(FxConditionOperator fxOperator)
        {
            return fxOperator switch
            {
                FxConditionOperator.Equal => ConditionOperator.Equal,
                FxConditionOperator.NotEqual => ConditionOperator.NotEqual,
                FxConditionOperator.GreaterThan => ConditionOperator.GreaterThan,
                FxConditionOperator.LessThan => ConditionOperator.LessThan,
                FxConditionOperator.GreaterEqual => ConditionOperator.GreaterEqual,
                FxConditionOperator.LessEqual => ConditionOperator.LessEqual,
                FxConditionOperator.Like => ConditionOperator.Like,
                FxConditionOperator.NotLike => ConditionOperator.NotLike,
                FxConditionOperator.In => ConditionOperator.In,
                FxConditionOperator.NotIn => ConditionOperator.NotIn,
                FxConditionOperator.Between => ConditionOperator.Between,
                FxConditionOperator.NotBetween => ConditionOperator.NotBetween,
                FxConditionOperator.Null => ConditionOperator.Null,
                FxConditionOperator.NotNull => ConditionOperator.NotNull,
                FxConditionOperator.Contains => ConditionOperator.Contains,
                FxConditionOperator.DoesNotContain => ConditionOperator.DoesNotContain,
                FxConditionOperator.BeginsWith => ConditionOperator.BeginsWith,
                FxConditionOperator.DoesNotBeginWith => ConditionOperator.DoesNotBeginWith,
                FxConditionOperator.EndsWith => ConditionOperator.EndsWith,
                FxConditionOperator.DoesNotEndWith => ConditionOperator.DoesNotEndWith,
                _ => throw new ArgumentOutOfRangeException(nameof(fxOperator), fxOperator, null)
            };
        }

        internal static BinaryOp ToBinaryOp(BinaryOpKind opKind) =>
            opKind switch
            {
                BinaryOpKind.AddDateAndDay => BinaryOp.Add,
                BinaryOpKind.AddDateAndTime => BinaryOp.Add,
                BinaryOpKind.AddDateTimeAndDay => BinaryOp.Add,
                BinaryOpKind.AddDayAndDate => BinaryOp.Add,
                BinaryOpKind.AddDayAndDateTime => BinaryOp.Add,
                BinaryOpKind.AddDecimals => BinaryOp.Add,
                BinaryOpKind.AddNumberAndTime => BinaryOp.Add,
                BinaryOpKind.AddNumbers => BinaryOp.Add,
                BinaryOpKind.AddTimeAndDate => BinaryOp.Add,
                BinaryOpKind.AddTimeAndNumber => BinaryOp.Add,
                BinaryOpKind.AddTimeAndTime => BinaryOp.Add,
                BinaryOpKind.And => BinaryOp.And,
                BinaryOpKind.Concatenate => BinaryOp.Concat,
                BinaryOpKind.DateDifference => BinaryOp.And,
                BinaryOpKind.DivDecimals => BinaryOp.Div,
                BinaryOpKind.DivNumbers => BinaryOp.Div,
                BinaryOpKind.DynamicGetField => BinaryOp.Error,
                BinaryOpKind.EqBlob => BinaryOp.Equal,
                BinaryOpKind.EqBoolean => BinaryOp.Equal,
                BinaryOpKind.EqColor => BinaryOp.Equal,
                BinaryOpKind.EqCurrency => BinaryOp.Equal,
                BinaryOpKind.EqDate => BinaryOp.Equal,
                BinaryOpKind.EqDateTime => BinaryOp.Equal,
                BinaryOpKind.EqDecimals => BinaryOp.Equal,
                BinaryOpKind.EqGuid => BinaryOp.Equal,
                BinaryOpKind.EqHyperlink => BinaryOp.Equal,
                BinaryOpKind.EqImage => BinaryOp.Equal,
                BinaryOpKind.EqMedia => BinaryOp.Equal,
                BinaryOpKind.EqNamedValue => BinaryOp.Equal,
                BinaryOpKind.EqNull => BinaryOp.Equal,
                BinaryOpKind.EqNullUntyped => BinaryOp.Equal,
                BinaryOpKind.EqNumbers => BinaryOp.Equal,
                BinaryOpKind.EqOptionSetValue => BinaryOp.Equal,
                BinaryOpKind.EqPolymorphic => BinaryOp.Equal,
                BinaryOpKind.EqText => BinaryOp.Equal,
                BinaryOpKind.EqTime => BinaryOp.Equal,
                BinaryOpKind.EqViewValue => BinaryOp.Equal,
                BinaryOpKind.ExactInScalarTable => BinaryOp.Exactin,
                BinaryOpKind.ExactInText => BinaryOp.Exactin,
                BinaryOpKind.GeqDate => BinaryOp.GreaterEqual,
                BinaryOpKind.GeqDateTime => BinaryOp.GreaterEqual,
                BinaryOpKind.GeqDecimals => BinaryOp.GreaterEqual,
                BinaryOpKind.GeqNull => BinaryOp.GreaterEqual,
                BinaryOpKind.GeqNumbers => BinaryOp.GreaterEqual,
                BinaryOpKind.GeqTime => BinaryOp.GreaterEqual,
                BinaryOpKind.GtDate => BinaryOp.Greater,
                BinaryOpKind.GtDateTime => BinaryOp.Greater,
                BinaryOpKind.GtDecimals => BinaryOp.Greater,
                BinaryOpKind.GtNull => BinaryOp.Greater,
                BinaryOpKind.GtNumbers => BinaryOp.Greater,
                BinaryOpKind.GtTime => BinaryOp.Greater,
                BinaryOpKind.InRecordTable => BinaryOp.In,
                BinaryOpKind.InScalarTable => BinaryOp.In,
                BinaryOpKind.InText => BinaryOp.In,
                BinaryOpKind.Invalid => BinaryOp.Error,
                BinaryOpKind.LeqDate => BinaryOp.LessEqual,
                BinaryOpKind.LeqDateTime => BinaryOp.LessEqual,
                BinaryOpKind.LeqDecimals => BinaryOp.LessEqual,
                BinaryOpKind.LeqNull => BinaryOp.LessEqual,
                BinaryOpKind.LeqNumbers => BinaryOp.LessEqual,
                BinaryOpKind.LeqTime => BinaryOp.LessEqual,
                BinaryOpKind.LtDate => BinaryOp.Less,
                BinaryOpKind.LtDateTime => BinaryOp.Less,
                BinaryOpKind.LtDecimals => BinaryOp.Less,
                BinaryOpKind.LtNull => BinaryOp.Less,
                BinaryOpKind.LtNumbers => BinaryOp.Less,
                BinaryOpKind.LtTime => BinaryOp.Less,
                BinaryOpKind.MulDecimals => BinaryOp.Mul,
                BinaryOpKind.MulNumbers => BinaryOp.Mul,
                BinaryOpKind.NeqBlob => BinaryOp.NotEqual,
                BinaryOpKind.NeqBoolean => BinaryOp.NotEqual,
                BinaryOpKind.NeqColor => BinaryOp.NotEqual,
                BinaryOpKind.NeqCurrency => BinaryOp.NotEqual,
                BinaryOpKind.NeqDate => BinaryOp.NotEqual,
                BinaryOpKind.NeqDateTime => BinaryOp.NotEqual,
                BinaryOpKind.NeqDecimals => BinaryOp.NotEqual,
                BinaryOpKind.NeqGuid => BinaryOp.NotEqual,
                BinaryOpKind.NeqHyperlink => BinaryOp.NotEqual,
                BinaryOpKind.NeqImage => BinaryOp.NotEqual,
                BinaryOpKind.NeqMedia => BinaryOp.NotEqual,
                BinaryOpKind.NeqNamedValue => BinaryOp.NotEqual,
                BinaryOpKind.NeqNull => BinaryOp.NotEqual,
                BinaryOpKind.NeqNullUntyped => BinaryOp.NotEqual,
                BinaryOpKind.NeqNumbers => BinaryOp.NotEqual,
                BinaryOpKind.NeqOptionSetValue => BinaryOp.NotEqual,
                BinaryOpKind.NeqPolymorphic => BinaryOp.NotEqual,
                BinaryOpKind.NeqText => BinaryOp.NotEqual,
                BinaryOpKind.NeqTime => BinaryOp.NotEqual,
                BinaryOpKind.NeqViewValue => BinaryOp.NotEqual,
                BinaryOpKind.Or => BinaryOp.Or,
                BinaryOpKind.Power => BinaryOp.Power,
                BinaryOpKind.SubtractDateAndTime => BinaryOp.Add,
                BinaryOpKind.SubtractNumberAndDate => BinaryOp.Add,
                BinaryOpKind.SubtractNumberAndDateTime => BinaryOp.Add,
                BinaryOpKind.SubtractNumberAndTime => BinaryOp.Add,
                BinaryOpKind.TimeDifference => BinaryOp.Add,
                _ => BinaryOp.Error
            };
    }
}
