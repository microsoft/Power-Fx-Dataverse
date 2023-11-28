using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.PowerFx.Dataverse
{
    public interface IEntityAttributeMetadataProvider
    {
        bool TryGetSecondaryEntityMetadata(string logicalName, out SecondaryEntityMetadata entity);

        bool TryGetSecondaryAttributeMetadata(string entityLogicalName, string attributeLogicalName, out SecondaryAttributeMetadata attribute);
    }
}
