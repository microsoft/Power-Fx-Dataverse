using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xrm.Sdk.Metadata;

namespace Microsoft.PowerFx.Dataverse
{
    internal static class Extensions
    {
        public static bool IsElasticTable(this EntityMetadata entityMetadata)
        {
            return entityMetadata.DataProviderId == System.Guid.Parse("1d9bde74-9ebd-4da9-8ff5-aa74945b9f74");
        }
    }
}
