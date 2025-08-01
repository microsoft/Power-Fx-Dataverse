﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using Microsoft.Xrm.Sdk;

namespace Microsoft.PowerFx.Dataverse
{
    public interface IDataverseEntityCache : IDataverseEntityCacheCleaner
    {
        // Maximum number of entries in cache
        int MaxEntries { get; }

        // Cached entry lifetime
        TimeSpan LifeTime { get; }

        // Current size of the cache
        int CacheSize { get; }

        // Add entity. If an entity exists with the same Id, it is replaced.
        void AddCacheEntry(Entity entity);

        // Remove an entry from cache
        void RemoveCacheEntry(Guid id);

        // Get an entity from cache. Returns null it not present.
        Entity GetEntityFromCache(Guid id);
    }

    public interface IDataverseEntityCacheCleaner
    {
        // Remove all cached entried from cache
        // When logicalTableName is specified, all Entities from that table are removed from the cache
        void ClearCache(string logicalTableName = null);
    }
}
