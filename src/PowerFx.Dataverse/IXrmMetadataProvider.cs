//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------


using System.Collections.Generic;
using Microsoft.Xrm.Sdk.Metadata;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Callback interface implemented by host to provide metadata
    /// </summary>
    public interface IXrmMetadataProvider
    {
        bool TryGetEntityMetadata(string logicalOrDisplayName, out EntityMetadata entity);
    }

    public interface IEntityAndAttributeMetadataProvider : IXrmMetadataProvider
    {
        bool TryGetAdditionalEntityMetadata(string logicalName, out Dictionary<string, object> entity);

        bool TryGetAdditionalAttributeMetadata(string entityLogicalName, string attributeLogicalName, out Dictionary<string, object> attribute);
    }
}
