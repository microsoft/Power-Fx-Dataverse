//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.AppMagic.Authoring;
using Microsoft.PowerFx.Core.App;
using System.Collections.Generic;

namespace Microsoft.PowerFx.Dataverse
{
    internal class DataverseDocumentProperties : IExternalDocumentProperties
    {
        public IExternalEnabledFeatures EnabledFeatures => new DefaultEnabledFeatures();
        public IExternalUserFlags UserFlags => new DefaultUserFlags();
    }
}
