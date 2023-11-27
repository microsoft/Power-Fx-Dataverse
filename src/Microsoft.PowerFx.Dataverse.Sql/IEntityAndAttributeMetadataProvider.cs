using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.PowerFx.Dataverse
{
    public interface IEntityAndAttributeMetadataProvider
    {
        bool TryGetAdditionalEntityMetadata(string logicalName, out AddtionalEntityMetadata entity);

        bool TryGetAdditionalAttributeMetadata(string entityLogicalName, string attributeLogicalName, out AddtionalAttributeMetadata attribute);
    }
}
