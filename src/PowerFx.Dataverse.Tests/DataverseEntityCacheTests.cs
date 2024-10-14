// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class DataverseEntityCacheTests
    {
        [Fact]
        public void EntityCache_TestCacheLimits()
        {
            Entity[] entities = Enumerable.Range(0, 5).Select((i) => new Entity("entity", Guid.NewGuid())).ToArray();
            DataverseEntityCache cache = new DataverseEntityCache(new TestOrganizationService(), 3, new TimeSpan(0, 0, 0, 0, 500));

            cache.AddCacheEntry(entities[0]);
            Assert.Equal(1, cache.CacheSize); // cache: 0

            cache.AddCacheEntry(entities[1]);
            Assert.Equal(2, cache.CacheSize); // cache: 0, 1

            cache.AddCacheEntry(entities[2]);
            Assert.Equal(3, cache.CacheSize); // cache: 0, 1, 2

            cache.AddCacheEntry(entities[3]);
            cache.AddCacheEntry(entities[3]);
            Assert.Equal(3, cache.CacheSize); // cache: 1, 2, 3

            cache.AddCacheEntry(entities[4]);
            Assert.Equal(3, cache.CacheSize); // cache: 2, 3, 4

            Assert.Null(cache.GetEntityFromCache(entities[0].Id));
            Assert.Null(cache.GetEntityFromCache(entities[1].Id));
            Assert.NotNull(cache.GetEntityFromCache(entities[2].Id));
            Assert.NotNull(cache.GetEntityFromCache(entities[3].Id));
            Assert.NotNull(cache.GetEntityFromCache(entities[4].Id));

            // cache life time is 500ms, so all entries should be invalid after this point
            Thread.Sleep(700);

            Assert.Null(cache.GetEntityFromCache(entities[2].Id));
            Assert.Null(cache.GetEntityFromCache(entities[3].Id));
            Assert.Null(cache.GetEntityFromCache(entities[4].Id));

            Assert.Equal(0, cache.CacheSize);

            cache.AddCacheEntry(entities[0]);
            Assert.Equal(1, cache.CacheSize); // cache: 0

            cache.AddCacheEntry(entities[1]);
            Assert.Equal(2, cache.CacheSize); // cache: 0, 1

            cache.AddCacheEntry(entities[2]);
            Assert.Equal(3, cache.CacheSize); // cache: 0, 1, 2

            // Here, we update entity 1
            cache.AddCacheEntry(entities[1]);
            Assert.Equal(3, cache.CacheSize); // cache: 0, 2, 1

            cache.AddCacheEntry(entities[4]);
            Assert.Equal(3, cache.CacheSize); // cache: 2, 1, 4

            Assert.Null(cache.GetEntityFromCache(entities[0].Id));
            Assert.NotNull(cache.GetEntityFromCache(entities[1].Id));
            Assert.NotNull(cache.GetEntityFromCache(entities[2].Id));
            Assert.Null(cache.GetEntityFromCache(entities[3].Id));
            Assert.NotNull(cache.GetEntityFromCache(entities[4].Id));

            cache.ClearCache();

            Assert.Equal(0, cache.CacheSize);

            cache.AddCacheEntry(entities[0]);
            cache.AddCacheEntry(new Entity("entity2", Guid.NewGuid()));

            cache.ClearCache("entity2");

            Assert.Equal(1, cache.CacheSize);
            Assert.Equal(entities[0].Id, cache.GetEntityFromCache(entities[0].Id).Id);

            cache.RemoveCacheEntry(entities[0].Id);
            cache.RemoveCacheEntry(entities[0].Id);
        }

        [Fact]
        public async Task EntityCache_CacheAPIs()
        {
            Entity[] entities = Enumerable.Range(0, 5).Select((i) => new Entity("entity", Guid.NewGuid())).ToArray();
            TestOrganizationService orgService = new TestOrganizationService();
            DataverseEntityCache cache = new DataverseEntityCache(orgService);

            DataverseResponse<Guid> r1 = await cache.CreateAsync(entities[0]);
            Assert.NotNull(r1);
            Assert.False(r1.HasError);

            Assert.Equal(0, cache.CacheSize);

            orgService.SetNextRetrieveResult(entities[0]);
            DataverseResponse<Entity> r2 = await cache.RetrieveAsync(entities[0].LogicalName, entities[0].Id, columns: null);
            Assert.NotNull(r2);
            Assert.False(r2.HasError);

            Assert.Equal(1, cache.CacheSize);
            Assert.Equal(entities[0].Id, r2.Response.Id);

            // Do not call SetNextRetrieveResult here
            DataverseResponse<Entity> r3 = await cache.RetrieveAsync(entities[0].LogicalName, entities[0].Id, columns: null);
            Assert.NotNull(r3);
            Assert.False(r3.HasError);

            Assert.Equal(1, cache.CacheSize);
            Assert.Equal(entities[0].Id, r3.Response.Id);
        }
    }

    public class TestOrganizationService : IOrganizationService
    {
        public void SetNextRetrieveResult(Entity entity)
        {
            _nextRetrieveResult = entity;
        }

        private Entity _nextRetrieveResult = null;

        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            throw new NotImplementedException();
        }

        public Guid Create(Entity entity)
        {
            return entity.Id;
        }

        public void Delete(string entityName, Guid id)
        {
            throw new NotImplementedException();
        }

        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            throw new NotImplementedException();
        }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            throw new NotImplementedException();
        }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            Entity e = _nextRetrieveResult;

            _nextRetrieveResult = null;

            return e;
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            throw new NotImplementedException();
        }

        public void Update(Entity entity)
        {
            throw new NotImplementedException();
        }
    }
}
