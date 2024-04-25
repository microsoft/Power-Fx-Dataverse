//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    // Simulate dataverse entities. 
    // Handles both metadata and the instances of Entity objects for testing. 
    internal class EntityLookup : IDataverseServices, IDataverseRefresh
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
                        metadata.Attributes.First(x => x.LogicalName == attr.Key || x.DisplayName.UserLocalizedLabel.Label == attr.Key); // throw if missing. 
                    }
                }

                _list.Add(Clone(entity, cancellationToken));
            }
        }

        // Chance to hook for error injection. Can throw. 
        public Action<EntityReference> _onLookupRef;

        // Return error message if numeric column is our of range (string: field name, object: number value).
        public Func<string, object, string> _checkColumnRange;

        // When used, returns a DataverseResponse error.
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
            return await DataverseResponse<Entity>.RunAsync(() => Task.FromResult(LookupRef(reference, cancellationToken)), "Entity lookup").ConfigureAwait(false);
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

        public virtual Task<DataverseResponse<Entity>> RetrieveAsync(string entityName, Guid id, IEnumerable<string> columns, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_rawProvider.TryGetEntityMetadata(entityName, out var md))
            {
                throw new InvalidOperationException($"Entity metadata for : {entityName} not found.");
            }

            if (md.IsElasticTable())
            {
                throw new InvalidOperationException("Elastic tables not supported. It should use Retreive Multiple API");
            }

            if (_getCustomErrorMessage != null)
            {
                return Task.FromResult(DataverseResponse<Entity>.NewError(_getCustomErrorMessage()));
            }

            return LookupReferenceAsync(new EntityReference(entityName, id));
        }

        public virtual async Task<DataverseResponse<EntityCollection>> RetrieveMultipleAsync(QueryBase query, CancellationToken cancellationToken = default(CancellationToken))
        {
            IEnumerable<Entity> data = _list;

            cancellationToken.ThrowIfCancellationRequested();
            var qe = query as QueryExpression;

            int take = qe.TopCount.GetValueOrDefault();
            if (take == 0)
            {
                take = int.MaxValue;
            }

            var entityList = ProcessEntity(data, qe, take, cancellationToken);

            if(qe.Distinct)
            {
                entityList = entityList.Distinct(new EntityComparer(qe.ColumnSet)).ToList();
            }

            return new DataverseResponse<EntityCollection>(new EntityCollection(entityList));
        }

        private IList<Entity> ProcessEntity(IEnumerable<Entity> data, QueryExpression qe, int take, CancellationToken cancellationToken)
        {
            var entityList = new List<Entity>();

            foreach (var entity in data)
            {
                var metadata = LookupMetadata(entity.LogicalName, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (entity.LogicalName == qe.EntityName &&
                    IsCriteriaMatching(entity, qe.Criteria, qe.LinkEntities, metadata))
                {
                    
                    entityList.Add(Clone(entity, qe.ColumnSet, cancellationToken));
                    take--;
                    if (take == 0)
                    {
                        break;
                    }
                }
            }
            return entityList;
        }

        private bool IsCriteriaMatching(Entity entity, FilterExpression criteria, DataCollection<LinkEntity> linkEntities, EntityMetadata metadata)
        {
            if (linkEntities != null && linkEntities.Count > 0)
            {
                entity = AttachRelationship(entity, linkEntities);
                linkEntities = null;
            }

            switch (criteria.FilterOperator)
            {
                case LogicalOperator.Or:
                    foreach (var filter in criteria.Filters)
                    {
                        if (IsCriteriaMatching(entity, filter, linkEntities, metadata))
                        {
                            return true;
                        }
                    }

                    foreach(var condition in criteria.Conditions)
                    {
                        if (isSatisfyingCondition(condition, entity, metadata))
                        {
                            return true;
                        }
                    }

                    return false;

                case LogicalOperator.And:
                    foreach (var filter in criteria.Filters)
                    {
                        if (!IsCriteriaMatching(entity, filter, linkEntities, metadata))
                        {
                            return false;
                        }
                    }

                    foreach (var condition in criteria.Conditions)
                    {
                        if (!isSatisfyingCondition(condition, entity, metadata))
                        {
                            return false;
                        }
                    }

                    return true;

                default:
                    throw new NotImplementedException();
            }
        }

        private Entity AttachRelationship(Entity currentEntity, DataCollection<LinkEntity> linkEntities)
        {
            if(linkEntities == null)
            {
                return currentEntity;
            }

            if(linkEntities.Count > 1)
            {
                throw new NotImplementedException("Multiple LinkEntities not supported");
            }

            var linkEntity = linkEntities[0];
            var linkEntityMetadata = LookupMetadata(linkEntity.LinkToEntityName, CancellationToken.None);

            foreach(var attribute in linkEntityMetadata.Attributes)
            {
                currentEntity.Attributes["_" + linkEntity.LinkToEntityName + "_" + attribute.LogicalName] = null;
            }

            var fromAttribute = linkEntity.LinkFromAttributeName;
            currentEntity.Attributes.TryGetValue(fromAttribute, out var fromValue);
            
            if(fromValue == null)
            {
                currentEntity.Attributes["_" + linkEntity.LinkToEntityName + "_" + fromAttribute] = null;
                return currentEntity;
            }

            var foreignEntity = LookupRef((EntityReference)fromValue, CancellationToken.None);

            foreach(var attribute in foreignEntity.Attributes)
            {
                currentEntity.Attributes["_" + foreignEntity.LogicalName + "_" + attribute.Key] = attribute.Value;
            }

            return currentEntity;

        }

        public bool isSatisfyingCondition(ConditionExpression condition, Entity entity, EntityMetadata metadata)
        {
            // this means condition was on relationship.
            if(condition.EntityName != null && condition.EntityName != entity.LogicalName)
            {
                _rawProvider.TryGetEntityMetadata(condition.EntityName.Substring(0, condition.EntityName.LastIndexOf("_")), out metadata);
            }

            metadata.TryGetAttribute(condition.AttributeName, out var amd);
            var comparer = new AttributeComparer(amd);

            var fieldName = condition.EntityName != null ? "_" + condition.EntityName.Substring(0, condition.EntityName.LastIndexOf("_")) + "_" + condition.AttributeName : condition.AttributeName;
            if (!TryGetAttributeOrPrimaryId(entity, metadata, fieldName, out var value))
            {
                return false;
            }

            switch (condition.Operator)
            {
                case ConditionOperator.Equal:
                    if (comparer.Compare(condition.Values[0], value) == 0)
                    {
                        return true;
                    }
                    break;
                case ConditionOperator.NotEqual:
                    if (comparer.Compare(condition.Values[0], value) != 0)
                    {
                        return true;
                    }
                    break;
                case ConditionOperator.Null:
                    if (value == null)
                    {
                        return true;
                    }
                    break;
                case ConditionOperator.NotNull:
                    if (value != null)
                    {
                        return true;
                    }
                    break;
                case ConditionOperator.GreaterThan:
                    if (comparer.Compare(condition.Values[0], value) < 0)
                    {
                        return true;
                    }
                    break;
                case ConditionOperator.GreaterEqual:
                    if (comparer.Compare(condition.Values[0], value) <= 0)
                    {
                        return true;
                    }
                    break;
                case ConditionOperator.LessThan:
                    if (comparer.Compare(condition.Values[0], value) > 0)
                    {
                        return true;
                    }
                    break;
                case ConditionOperator.LessEqual:
                    if (comparer.Compare(condition.Values[0], value) >= 0)
                    {
                        return true;
                    }
                    break;
                case ConditionOperator.In:
                    foreach (var v in condition.Values)
                    {
                        if (comparer.Compare(v, value) == 0)
                        {
                            return true;
                        }
                    }
                    break;
                default:
                    throw new NotImplementedException($"Operator not supported: {condition.Operator.ToString()}");
            }

            return false;
        }

        private static bool TryGetAttributeOrPrimaryId(Entity entity, EntityMetadata metadata, string attributeName, out object value)
        {
            if(entity.Attributes.TryGetValue(attributeName, out value))
            {
                return true;
            }
            if(attributeName == metadata.PrimaryIdAttribute)
            {
                value = entity.Id;
                return true;
            }

            return false;
        }

        internal class AttributeComparer : IComparer<object>
        {
            private readonly AttributeMetadata _amd;

            public AttributeComparer(AttributeMetadata amd)
            {
                _amd = amd;
            }

            public int Compare(object x,object y)
            {
                switch (_amd.AttributeType.Value)
                {
                    case AttributeTypeCode.Boolean:
                        return (x == null ? default : (bool)x).CompareTo(y == null ? default : (bool)y);

                    case AttributeTypeCode.DateTime:
                        return (x == null ? default : (DateTime)x).CompareTo(y == null ? default : (DateTime)y);

                    case AttributeTypeCode.Money:
                    case AttributeTypeCode.Decimal:
                        if (x is Xrm.Sdk.Money mx)
                        {
                            x = mx.Value;
                        }
                        if (y is Xrm.Sdk.Money my)
                        {
                            y = my.Value;
                        }
                        return (x == null ? default : (decimal)x).CompareTo(y == null ? default : (decimal)y);

                    case AttributeTypeCode.Double:
                        return (x == null ? default : (double)x).CompareTo(y == null ? default : (double)y);

                    case AttributeTypeCode.Picklist:
                    case AttributeTypeCode.Status:
                    case AttributeTypeCode.Integer:
                    case AttributeTypeCode.State:
                        if (x is Xrm.Sdk.OptionSetValue osx)
                        {
                            x = osx.Value;
                        }
                        if (y is Xrm.Sdk.OptionSetValue osy)
                        {
                            y = osy.Value;
                        }

                        return (x == null ? default : (int)x).CompareTo(y == null ? default : (int)y);

                    case AttributeTypeCode.Memo:
                    case AttributeTypeCode.String:
                        return (x == null ? default : (string)x).CompareTo(y == null ? default : (string)y);

                    case AttributeTypeCode.Uniqueidentifier:
                        return (x == null ? default : (Guid)x).CompareTo(y == null ? default : (Guid)y);

                    case AttributeTypeCode.Lookup:
                        return (x == null ? default : (x is Guid gx ? gx : ((EntityReference)x).Id)).CompareTo(y == null ? default : (y is Guid gy ? gy : ((EntityReference)y).Id));
                    case AttributeTypeCode.BigInt:
                    case AttributeTypeCode.CalendarRules:
                    case AttributeTypeCode.Customer:
                    case AttributeTypeCode.EntityName:
                    case AttributeTypeCode.Virtual:
                    case AttributeTypeCode.ManagedProperty:
                    case AttributeTypeCode.PartyList:
                    default:
                        throw new NotImplementedException($"FieldType {_amd.AttributeType.Value} not supported");
                }
            }
        }

        internal class EntityComparer : IEqualityComparer<Entity>
        {
            private readonly string _column;

            public EntityComparer(ColumnSet columnSet)
            {
                if(columnSet.Columns.Count == 1)
                {
                    _column = columnSet.Columns[0];
                    return;
                }

                throw new NotImplementedException();
            }

            public bool Equals(Entity x, Entity y)
            {
                if(x.Attributes.TryGetValue(_column, out var xValue) && y.Attributes.TryGetValue(_column, out var yValue))
                {
                    return xValue.Equals(yValue);
                }

                return false;
            }

            public int GetHashCode([DisallowNull] Entity obj)
            {
                if(obj.Attributes.TryGetValue(_column, out var value))
                {
                    return value.GetHashCode();
                }

                throw new NotImplementedException();
            }
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

        private Entity Clone(Entity entity, ColumnSet columnSet, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var columnFilter = columnSet.Columns.ToHashSet();

            var newEntity = new Entity(entity.LogicalName, entity.Id);
            foreach (var attr in entity.Attributes)
            {
                if(columnSet.AllColumns || columnFilter.Contains(attr.Key) || attr.Key == "partitionid")
                {
                    newEntity.Attributes[attr.Key] = attr.Value;
                }
            }
            return newEntity;
        }

        public virtual void Refresh(string logicalTableName)
        {            
        }

        public async Task<DataverseResponse<OrganizationResponse>> ExecuteAsync(OrganizationRequest request, CancellationToken cancellationToken = default)
        {
            if (!request.Parameters.TryGetValue("partitionId", out var partitionId))
            {
                throw new InvalidOperationException("PartitionId not found in the request.");
            }

            if(request is RetrieveMultipleRequest rmr)
            {
                var result = await RetrieveMultipleAsync(rmr.Query, cancellationToken);
                var paramCollection = new ParameterCollection
                {
                    ["EntityCollection"] = new EntityCollection(result.Response.Entities.Select(e => e.Attributes.TryGetValue("partitionid", out var value) && (partitionId == null || value.Equals(partitionId)) ? e : null).Where(e => e != null).ToList())
                };

                return new DataverseResponse<OrganizationResponse>(new RetrieveMultipleResponse () { Results = paramCollection });
            }
            else if(request is RetrieveRequest rr)
            {
                this._rawProvider.TryGetEntityMetadata(rr.Target.LogicalName, out var metadata);
                var filter = new FilterExpression(LogicalOperator.And);
                filter.AddCondition(metadata.PrimaryIdAttribute, ConditionOperator.Equal, rr.Target.Id);

                if (partitionId != null)
                {
                    filter.AddCondition("partitionid", ConditionOperator.Equal, partitionId);
                }
                var query = new QueryExpression(rr.Target.LogicalName)
                {
                    ColumnSet = new ColumnSet(true),
                    Criteria = filter
                };

                var result = await RetrieveMultipleAsync(query, cancellationToken);
                var entity = result.Response.Entities.FirstOrDefault();

                if(entity == null)
                {
                    return new DataverseResponse<OrganizationResponse>(new RetrieveResponse() { Results = new ParameterCollection() });
                }

                return new DataverseResponse<OrganizationResponse>(new RetrieveResponse() { Results = new ParameterCollection { ["Entity"] = entity } });
                
            }

            throw new NotImplementedException();
        }
    }
}
