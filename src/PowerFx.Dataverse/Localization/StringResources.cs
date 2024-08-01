// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Resources;
using Microsoft.PowerFx.Core.Localization;

[assembly: NeutralResourcesLanguage("en-US", UltimateResourceFallbackLocation.Satellite)]

namespace Microsoft.PowerFx.Dataverse.Localization
{
    internal static class DataverseStringResources
    {
        internal static readonly IExternalStringResources LocalStringResources = new PowerFxStringResources("Microsoft.PowerFx.Dataverse.strings.DataverseResources", typeof(DataverseStringResources).Assembly);

        internal static ErrorResourceKey New(string key) => new (key, LocalStringResources);
    }
}
