//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.Xrm.Sdk;
using System;

namespace Microsoft.PowerFx.Dataverse
{
    internal class DataverseCachedEntity
    {
        internal Entity Entity { get; }

        internal DateTime TimeStamp { get; } // UTC

        internal DataverseCachedEntity(Entity entity) 
        { 
            Entity = entity;
            TimeStamp = DateTime.UtcNow;
        }
    }
}
