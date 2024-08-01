// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

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
}
