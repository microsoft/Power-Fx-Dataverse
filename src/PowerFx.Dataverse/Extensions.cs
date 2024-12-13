// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;

namespace Microsoft.PowerFx.Dataverse
{
    internal static class Extensions
    {
        public static bool IsElasticTable(this EntityMetadata entityMetadata)
        {
            return entityMetadata.DataProviderId == System.Guid.Parse("1d9bde74-9ebd-4da9-8ff5-aa74945b9f74");
        }

        public static bool TryGetPrimaryKeyFieldName2(this RecordType type, out IEnumerable<string> primaryKeyNames)
        {
            // dataverse types has embedded metadata.
            if (DelegationUtility.TryGetEntityMetadata(type, out var entityMetadata))
            {
                primaryKeyNames = new List<string>() { entityMetadata.PrimaryIdAttribute };
                return true;
            }

            return type.TryGetPrimaryKeyFieldName(out primaryKeyNames);
        }
    }
}
