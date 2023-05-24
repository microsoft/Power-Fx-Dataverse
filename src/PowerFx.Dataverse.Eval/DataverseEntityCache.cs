//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse
{
    public class DataverseEntityCache : IDataverseServices, IDataverseEntityCache
    {
        public int MaxEntries { get; } // 0 = no cache

        public TimeSpan LifeTime { get; }

        public TimeSpan DefaultLifeTime => new(0, 5, 0); // 5 minutes

        public int CacheSize { get { lock (_lock) { return _cache.Count; } } }

        // Stores all entities, sorted by Id (key) with their timestamp (value)
        private readonly Dictionary<Guid, DataverseCachedEntity> _cache = new();

        // Stores the list of cached entry Ids in order they are cached
        private readonly List<Guid> _cacheList = new();

        // Lock used for cache dictionary and list
        private readonly object _lock = new();

        private readonly IDataverseServices _innerService;

        public DataverseEntityCache(IOrganizationService orgService, int maxEntries = 4096, TimeSpan cacheLifeTime = default)
            : this(new DataverseService(orgService), maxEntries, cacheLifeTime)
        {
        }

        public DataverseEntityCache(IDataverseServices innerService, int maxEntries = 4096, TimeSpan cacheLifeTime = default)
        {
            MaxEntries = maxEntries < 0 ? 4096 : maxEntries;
            LifeTime = cacheLifeTime.TotalMilliseconds <= 0 ? DefaultLifeTime : cacheLifeTime;
            _innerService = innerService;
        }

        public void AddCacheEntry(Entity entity)
        {
            if (entity == null || MaxEntries == 0)
            {
                return;
            }

            Guid id = entity.Id;

            lock (_lock)
            {
                DataverseCachedEntity dce = new(entity);

                if (_cache.ContainsKey(id))
                {
                    _cache[id] = dce;
                    _cacheList.Remove(id);
                }
                else
                {
                    _cache.Add(id, dce);
                }

                _cacheList.Add(id);

                if (_cacheList.Count > MaxEntries)
                {
                    Guid idToRemove = _cacheList.First();

                    _cacheList.RemoveAt(0);
                    _cache.Remove(idToRemove);
                }
            }
        }

        public void RemoveCacheEntry(Guid id)
        {
            lock (_lock)
            {
                _cacheList.Remove(id);
                _cache.Remove(id);
            }
        }

        public void ClearCache(string logicalTableName = null)
        {
            lock (_lock)
            {
                if (string.IsNullOrEmpty(logicalTableName))
                {
                    _cache.Clear();
                    _cacheList.Clear();
                    return;
                }

                foreach (var entityKvp in _cache.Where(kvp => kvp.Value.Entity.LogicalName.Equals(logicalTableName, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    RemoveCacheEntry(entityKvp.Key);
                }
            }
        }

        public Entity GetEntityFromCache(Guid id)
        {
            lock (_lock)
            {
                if (!_cache.TryGetValue(id, out DataverseCachedEntity dce))
                {
                    // Unknown Id (not in cache)
                    return null;
                }

                // Is entry still valid?
                if (dce.TimeStamp > DateTime.UtcNow.Add(-LifeTime))
                {
                    return dce.Entity;
                }

                // Entry expired
                RemoveCacheEntry(id);
                return null;
            }
        }

        public async Task<DataverseResponse<Guid>> CreateAsync(Entity entity, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            DataverseResponse<Guid> result = await _innerService.CreateAsync(entity, cancellationToken).ConfigureAwait(false);

            if (!result.HasError)
            {
                // Should never happen, but make sure this Id isn't in the cache
                RemoveCacheEntry(result.Response);
            }

            return result;
        }

        public Task<DataverseResponse> DeleteAsync(string entityName, Guid id, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Remove this Id from cache, even if Delete would fail
            RemoveCacheEntry(id);
            return _innerService.DeleteAsync(entityName, id, cancellationToken);
        }

        public async Task<DataverseResponse<Entity>> RetrieveAsync(string entityName, Guid id, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_lock)
            {
                Entity e = GetEntityFromCache(id);

                if (e != null)
                {
                    return new DataverseResponse<Entity>(e);
                }
            }

            DataverseResponse<Entity> result = await _innerService.RetrieveAsync(entityName, id, cancellationToken).ConfigureAwait(false);

            if (!result.HasError)
            {
                AddCacheEntry(result.Response);
            }

            return result;
        }

        public async Task<DataverseResponse<EntityCollection>> RetrieveMultipleAsync(QueryBase query, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Verify if we have any entry in the cache for that table/entity name
            // If any, we'll use the cached entries
            lock (_lock)
            {                
                List<Entity> list = new();

                foreach (KeyValuePair<Guid, DataverseCachedEntity> cacheEntry in _cache)
                {
                    Entity e = cacheEntry.Value.Entity;

                    if (e.LogicalName == (query as QueryExpression).EntityName)
                    {
                        list.Add(e);
                    }
                }

                if (list.Any())
                {
                    return new DataverseResponse<EntityCollection>(new EntityCollection(list));
                }
            }

            DataverseResponse<EntityCollection> result = await _innerService.RetrieveMultipleAsync(query, cancellationToken);

            if (!result.HasError)
            {
                foreach (Entity e in result.Response.Entities)
                {
                    AddCacheEntry(e);
                }
            }

            return result;
        }

        public Task<DataverseResponse> UpdateAsync(Entity entity, CancellationToken cancellationToken = default(CancellationToken))
        {
            RemoveCacheEntry(entity.Id);
            return _innerService.UpdateAsync(entity, cancellationToken);
        }

        public void Refresh(string logicalTableName)
        {
            if (MaxEntries != 0)
            {
                lock (_lock)
                {
                    foreach (KeyValuePair<Guid, DataverseCachedEntity> entityKvp in _cache)
                    {
                        if (entityKvp.Value.Entity.LogicalName.Equals(logicalTableName, StringComparison.Ordinal))
                        {
                            RemoveCacheEntry(entityKvp.Key);
                        }
                    }
                }
            }
        }
    }
}
