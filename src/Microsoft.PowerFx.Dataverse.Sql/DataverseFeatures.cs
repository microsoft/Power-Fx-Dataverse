using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.PowerFx.Dataverse
{
    public sealed class DataverseFeatures
    {
        // This FCB is to enable/disable Option Set Feature.
        // When this flag is enabled, Formula Field of type Options Set are supported.
        internal bool IsOptionSetEnabled { get; set; }
    }
}
