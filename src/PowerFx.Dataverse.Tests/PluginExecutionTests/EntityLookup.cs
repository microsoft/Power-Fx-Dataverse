//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    // Simulate dataverse entities. 
    // Handles both metadata and the instances of Entity objects for testing. 
    internal class EntityLookup : IDataverseServices
    {
        internal readonly List<Entity> _list = new List<Entity>();

        public IXrmMetadataProvider _rawProvider;
        public readonly CdsEntityMetadataProvider _provider;

        public EntityLookup(IXrmMetadataProvider xrmMetadataProvider)
        {
            _rawProvider = xrmMetadataProvider;
            _provider = new CdsEntityMetadataProvider(_rawProvider);
        }

        public EntityMetadata LookupMetadata(string logicalName)
        {
            if (!_rawProvider.TryGetEntityMetadata(logicalName, out var metadata))
            {
                throw new InvalidOperationException($"Metadata {logicalName} not found.");
            }
            return metadata;
        }

        // get a RecordValue for the first entity in the table.
        public Entity GetFirstEntity(string logicalName, DataverseConnection dataverseConnection)
        {
            if (dataverseConnection == null)
            {
                dataverseConnection = new DataverseConnection(this, _provider);
                dataverseConnection.AddTable(logicalName, logicalName);
            }

            foreach (var entity in _list)
            {
                if (entity.LogicalName == logicalName)
                {
                    return Clone(entity);
                }
            }
            throw new InvalidOperationException($"No entity of type {logicalName}.");
        }

        public RecordValue ConvertEntityToRecordValue(string logicalName, DataverseConnection dataverseConnection)
        {
            if (dataverseConnection == null)
            {
                dataverseConnection = new DataverseConnection(this, _provider);
                dataverseConnection.AddTable(logicalName, logicalName);
            }

            var entity = GetFirstEntity(logicalName, dataverseConnection);
            return dataverseConnection.Marshal(entity);            
        }

        // Entities should conform to the metadata passed to the ctor. 
        public void Add(params Entity[] entities)
        {
            foreach (var entity in entities)
            {
                // Assert the entities we provide match the metadata we have. 
                var metadata = LookupMetadata(entity.LogicalName); // will throw if missing. 


                foreach (var attr in entity.Attributes)
                {
                    // Fails for EntityReference due to ReferencingEntityNavigationPropertyName. 
                    if (!(attr.Value is EntityReference))
                    {
                        metadata.Attributes.First(x => x.LogicalName == attr.Key); // throw if missing. 
                    }
                }


                _list.Add(Clone(entity));
            }
        }

        // Chance to hook for error injection. Can throw. 
        public Action<EntityReference> _onLookupRef;

        // Gets a copy of the entity. 
        // modifying the storage still requires a call to Update. 
        public Entity LookupRef(EntityReference entityRef)
        {
            return Clone(LookupRefCore(entityRef));
        }
                
        // Gets direct access to the entire storage.
        // Modifying this entity will modify the storage.
        internal Entity LookupRefCore(EntityReference entityRef)
        {
            if (_onLookupRef != null)
            {
                _onLookupRef(entityRef);
            }

            foreach (var entity in _list)
            {
                if (entity.LogicalName == entityRef.LogicalName && entity.Id == entityRef.Id)
                {
                    return entity;
                }
            }
            throw new InvalidOperationException($"Entity {entityRef.LogicalName}:{entityRef.Id} not found");
        }

        public bool Exists(EntityReference entityRef)
        {
            foreach (var entity in _list)
            {
                if (entity.LogicalName == entityRef.LogicalName && entity.Id == entityRef.Id)
                {
                    return true;
                }
            }
            return false;
        }

        public virtual async Task<DataverseResponse<Guid>> CreateAsync(Entity entity, CancellationToken ct = default(CancellationToken))
        {
            Add(entity);

            return new DataverseResponse<Guid>(entity.Id);
        }

        public async Task<DataverseResponse<Entity>> LookupReferenceAsync(EntityReference reference, CancellationToken ct = default(CancellationToken))
        {
            return await DataverseResponse<Entity>.RunAsync(() => Task.FromResult(LookupRef(reference)), "Entity lookup");
        }

        public virtual Task<DataverseResponse<Entity>> UpdateAsync(Entity entity, CancellationToken ct = default(CancellationToken))
        {
            // gets the raw storage and mutate it. 
            var existing = LookupRefCore(entity.ToEntityReference()); 
            
            foreach (var attr in entity.Attributes)
            {
                existing.Attributes[attr.Key] = attr.Value;
            }
            
            return Task.FromResult(new DataverseResponse<Entity>(entity));
        }

        public virtual Task<DataverseResponse<Entity>> RetrieveAsync(string entityName, Guid id, CancellationToken ct = default(CancellationToken))
        {
            return LookupReferenceAsync(new EntityReference(entityName, id));
        }

        public virtual async Task<DataverseResponse<EntityCollection>> RetrieveMultipleAsync(QueryBase query, CancellationToken ct = default(CancellationToken))
        {
            List<Entity> entityList = new List<Entity>();
            IEnumerable<Entity> data = _list;

            var qe = query as QueryExpression;

            int take = qe.TopCount.GetValueOrDefault();
            if (take == 0)
            {
                take = int.MaxValue;
            }

            foreach (var entity in data)
            {
                if (entity.LogicalName == qe.EntityName)
                {
                    entityList.Add(Clone(entity));
                    take--;
                    if (take == 0)
                    {
                        break;
                    }
                }
            }

            return new DataverseResponse<EntityCollection>(new EntityCollection(entityList));
        }

        public virtual HttpResponseMessage ExecuteWebRequest(HttpMethod method, string queryString, string body, Dictionary<string, List<string>> customHeaders, string contentType = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<DataverseResponse> DeleteAsync(string entityName, Guid id, CancellationToken ct = default(CancellationToken))
        {
            foreach (var entity in _list)
            {
                if (entity.LogicalName == entityName&& entity.Id == id)
                {
                    _list.Remove(entity);
                    break;
                }
            }
            return Task.FromResult(new DataverseResponse());
        }

        // Create clones to simulate that local copies of an Entity are separate than what's in the database.
        private Entity Clone(Entity entity)
        {
            var newEntity = new Entity(entity.LogicalName, entity.Id);
            foreach (var attr in entity.Attributes)
            {
                newEntity.Attributes[attr.Key] = attr.Value;
            }
            return newEntity;
        }
    }
}