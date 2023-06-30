//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Core.Localization;
using System.Resources;

[assembly: NeutralResourcesLanguage("en-US", UltimateResourceFallbackLocation.Satellite)]

namespace Microsoft.PowerFx.Dataverse.Localization
{
    internal static class DataverseStringResources
    {
        internal static readonly IResourceStringManager LocalStringResources = new PowerFxStringResources("Microsoft.PowerFx.Dataverse.strings.DataverseResources", typeof(DataverseStringResources).Assembly);
    }    
}