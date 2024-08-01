// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using Microsoft.Xrm.Sdk;

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
