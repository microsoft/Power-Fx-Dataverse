// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation
{
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
    }
}
