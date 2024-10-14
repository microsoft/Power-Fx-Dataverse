// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Linq;
using System.Text.Json;
using Microsoft.PowerFx.Core.Functions.Delegation.DelegationMetadata;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;

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

        public static bool CanDelegateFilter(string fieldName, BinaryOpKind op, FilterOpMetadata filterCapabilities)
        {
            return op == BinaryOpKind.Invalid /* Starts/EndsWith */ || (filterCapabilities?.IsBinaryOpInDelegationSupportedByColumn(ToBinaryOp(op), DPath.Root.Append(new DName(fieldName))) != false);
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
