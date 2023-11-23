using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.PowerFx.Dataverse
{
    public interface IEntityAndAttributeMetadataProvider
    {
        bool TryGetAdditionalEntityMetadata(string logicalName, out Dictionary<string, object> entity);

        bool TryGetAdditionalAttributeMetadata(string entityLogicalName, string attributeLogicalName, out Dictionary<string, object> attribute);
    }
}
