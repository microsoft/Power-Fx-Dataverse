// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using Microsoft.AppMagic.Authoring;
using Microsoft.PowerFx.Core.App;

namespace Microsoft.PowerFx.Dataverse
{
    internal class DataverseDocumentProperties : IExternalDocumentProperties
    {
        public IExternalEnabledFeatures EnabledFeatures => new DefaultEnabledFeatures();

        public IExternalUserFlags UserFlags => new DefaultUserFlags();
    }
}
