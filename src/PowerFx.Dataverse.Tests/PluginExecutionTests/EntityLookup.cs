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

        public EntityMetadata LookupMetadata(string logicalName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_rawProvider.TryGetEntityMetadata(logicalName, out var metadata))
            {
                throw new InvalidOperationException($"Metadata {logicalName} not found.");
            }
            return metadata;
        }

        // get a RecordValue for the first entity in the table.
        public Entity GetFirstEntity(string logicalName, DataverseConnection dataverseConnection, CancellationToken cancellationToken)
        {
            if (dataverseConnection == null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                dataverseConnection = new DataverseConnection(this, _provider);
                dataverseConnection.AddTable(logicalName, logicalName);
            }

            foreach (var entity in _list)
            {
                if (entity.LogicalName == logicalName)
                {
                    return Clone(entity, cancellationToken);
                }
            }

            throw new InvalidOperationException($"No entity of type {logicalName}.");
        }

        public RecordValue ConvertEntityToRecordValue(string logicalName, DataverseConnection dataverseConnection, CancellationToken cancellationToken)
        {
            if (dataverseConnection == null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                dataverseConnection = new DataverseConnection(this, _provider);
                dataverseConnection.AddTable(logicalName, logicalName);
            }

            var entity = GetFirstEntity(logicalName, dataverseConnection, cancellationToken);
            return dataverseConnection.Marshal(entity);            
        }

        // Entities should conform to the metadata passed to the ctor. 
        public void Add(CancellationToken cancellationToken, params Entity[] entities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var entity in entities)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Assert the entities we provide match the metadata we have. 
                var metadata = LookupMetadata(entity.LogicalName, cancellationToken); // will throw if missing. 

                foreach (var attr in entity.Attributes)
                {
                    // Fails for EntityReference due to ReferencingEntityNavigationPropertyName. 
                    if (!(attr.Value is EntityReference))
                    {
                        metadata.Attributes.First(x => x.LogicalName == attr.Key); // throw if missing. 
                    }
                }

                _list.Add(Clone(entity, cancellationToken));
            }
        }

        // Chance to hook for error injection. Can throw. 
        public Action<EntityReference> _onLookupRef;

        // Throws if column value is out of range
        public Func<string, object, string> _checkColumnRange;

        // When used, it forces a mutation function to return a DataverseResponse error.
        public Func<string> _getCustomErrorMessage;

        // When set, returns the column name that's allowed to be updated. Attempting to update any other column name will result in an error.
        public Func<string> _getTargetedColumnName;

        // Gets a copy of the entity. 
        // modifying the storage still requires a call to Update. 
        public Entity LookupRef(EntityReference entityRef, CancellationToken cancellationToken)
        {
            return Clone(LookupRefCore(entityRef), cancellationToken);
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

        public virtual async Task<DataverseResponse<Guid>> CreateAsync(Entity entity, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Add(cancellationToken, entity);

            return new DataverseResponse<Guid>(entity.Id);
        }

        public async Task<DataverseResponse<Entity>> LookupReferenceAsync(EntityReference reference, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await DataverseResponse<Entity>.RunAsync(() => Task.FromResult(LookupRef(reference, cancellationToken)), "Entity lookup");
        }

        public virtual Task<DataverseResponse> UpdateAsync(Entity entity, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // gets the raw storage and mutate it. 
            var existing = LookupRefCore(entity.ToEntityReference()); 
            
            foreach (var attr in entity.Attributes)
            {
                if (_getTargetedColumnName != null && _getTargetedColumnName() != attr.Key)
                {
                    return Task.FromResult(DataverseResponse.NewError($"Invalid attempt to update {attr.Key} column."));
                }

                if (_getTargetedColumnName != null && _getTargetedColumnName() != attr.Key)
                {
                    return Task.FromResult(DataverseResponse.NewError($"Invalid attempt to update {attr.Key} column."));
                }

                if (_checkColumnRange != null)
                {
                    var errorMessage = _checkColumnRange(attr.Key, attr.Value);

                    if (errorMessage != null)
                    {
                        return Task.FromResult(DataverseResponse.NewError(errorMessage));
                    }                    
                }

                existing.Attributes[attr.Key] = attr.Value;
            }
            
            return Task.FromResult(new DataverseResponse());
        }

        public virtual Task<DataverseResponse<Entity>> RetrieveAsync(string entityName, Guid id, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return LookupReferenceAsync(new EntityReference(entityName, id));
        }

        public virtual async Task<DataverseResponse<EntityCollection>> RetrieveMultipleAsync(QueryBase query, CancellationToken cancellationToken = default(CancellationToken))
        {
            List<Entity> entityList = new List<Entity>();
            IEnumerable<Entity> data = _list;

            cancellationToken.ThrowIfCancellationRequested();
            var qe = query as QueryExpression;

            int take = qe.TopCount.GetValueOrDefault();
            if (take == 0)
            {
                take = int.MaxValue;
            }

            foreach (var entity in data)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entity.LogicalName == qe.EntityName)
                {
                    entityList.Add(Clone(entity, cancellationToken));
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

        public virtual Task<DataverseResponse> DeleteAsync(string entityName, Guid id, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_getCustomErrorMessage != null)
            {
                return Task.FromResult(DataverseResponse.NewError(_getCustomErrorMessage()));
            }

            foreach (var entity in _list)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entity.LogicalName == entityName&& entity.Id == id)
                {
                    _list.Remove(entity);
                    break;
                }
            }

            return Task.FromResult(new DataverseResponse());
        }

        // Create clones to simulate that local copies of an Entity are separate than what's in the database.
        private Entity Clone(Entity entity, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var newEntity = new Entity(entity.LogicalName, entity.Id);
            foreach (var attr in entity.Attributes)
            {
                newEntity.Attributes[attr.Key] = attr.Value;
            }
            return newEntity;
        }
    }
}