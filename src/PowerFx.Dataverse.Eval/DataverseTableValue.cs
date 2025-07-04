﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Interpreter;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Wrap a live Dataverse Table.
    /// </summary>
    internal class DataverseTableValue : TableValue, IRefreshable, IDelegatableTableValue
    {
        private readonly IConnectionValueContext _connection;

        private readonly RecordType _recordType;

        internal IConnectionValueContext Connection => _connection;

        private Lazy<Task<List<DValue<RecordValue>>>> _lazyTaskRows;

        private Lazy<Task<List<DValue<RecordValue>>>> NewLazyTaskRowsInstance => new Lazy<Task<List<DValue<RecordValue>>>>(() => GetRowsAsync());

        public bool HasCachedRows => _lazyTaskRows.IsValueCreated;

        public override sealed IEnumerable<DValue<RecordValue>> Rows => _lazyTaskRows.Value.ConfigureAwait(false).GetAwaiter().GetResult();

        public DelegationParameterFeatures SupportedFeatures => DelegationParameterFeatures.Filter | DelegationParameterFeatures.Top | DelegationParameterFeatures.Columns | DelegationParameterFeatures.ApplyGroupBy | DelegationParameterFeatures.ApplyJoin | DelegationParameterFeatures.Sort | DelegationParameterFeatures.Count | DelegationParameterFeatures.ApplyTopLevelAggregation;

        public readonly EntityMetadata _entityMetadata;

        internal DataverseTableValue(RecordType recordType, IConnectionValueContext connection, EntityMetadata metadata)
            : base(recordType.ToTable())
        {
            _recordType = recordType;
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _entityMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _lazyTaskRows = NewLazyTaskRowsInstance;
        }

        public override object ToObject()
        {
            throw new NotImplementedException("DataverseTableValue.ToObject() isn't implemented yet.");
        }

        public void Refresh()
        {
            _lazyTaskRows = NewLazyTaskRowsInstance;
            var services = _connection.Services.DataverseServices;
            if (services is IDataverseRefresh serviceRefresh)
            {
                serviceRefresh.Refresh(_entityMetadata.LogicalName);
            }
        }

        protected virtual async Task<List<DValue<RecordValue>>> GetRowsAsync()
        {
            List<DValue<RecordValue>> list = new ();
            DataverseResponse<EntityCollection> entities = await _connection.Services.QueryAsync(_entityMetadata.LogicalName, _connection.MaxRows).ConfigureAwait(false);

            if (entities.HasError)
            {
                return new List<DValue<RecordValue>> { entities.DValueError(nameof(QueryExtensions.QueryAsync)) };
            }

            var result = EntityCollectionToRecordValues(entities.Response);
            return result;
        }

        public virtual async Task<DValue<RecordValue>> RetrieveAsync(Guid id, FxColumnMap columnMap, CancellationToken cancellationToken = default)
        {
            var columns = columnMap?.RealColumnNames;
            var result = await _connection.Services.RetrieveAsync(_entityMetadata.LogicalName, id, columns, cancellationToken).ConfigureAwait(false);

            if (result.HasError)
            {
                return result.DValueError("Retrieve");
            }

            Entity entity = result.Response;
            var row = new DataverseRecordValue(entity, _entityMetadata, Type.ToRecord(), _connection);

            return DValue<RecordValue>.Of(row);
        }

        public virtual async Task<DValue<RecordValue>> RetrieveAsync(Guid id, string partitionId, FxColumnMap columnMap, CancellationToken cancellationToken = default)
        {
            var entityReference = new EntityReference(this._entityMetadata.LogicalName, id);

            var request = new RetrieveRequest
            {
                ColumnSet = columnMap.ToXRMColumnSet(),
                Target = entityReference,
                ["partitionId"] = partitionId
            };

            var result = await _connection.Services.ExecuteAsync(request, cancellationToken);

            if (result.HasError)
            {
                return result.DValueError("Retrieve");
            }

            var entity = ((RetrieveResponse)result.Response).Entity;

            var row = new DataverseRecordValue(entity, _entityMetadata, Type.ToRecord(), _connection);

            return DValue<RecordValue>.Of(row);
        }

        public virtual async Task<IReadOnlyCollection<DValue<RecordValue>>> GetRowsAsync(IServiceProvider services, DelegationParameters parameters, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

#pragma warning disable CS0618 // Type or member is obsolete
            var delegationParameters = (DataverseDelegationParameters)parameters;
#pragma warning restore CS0618 // Type or member is obsolete

            if (this._entityMetadata.IsElasticTable())
            {
                var rows = await this.RetrieveMultipleAsync(delegationParameters, delegationParameters._partitionId, cancellationToken).ConfigureAwait(false);

                return rows;
            }
            else
            {
                var rows = await this.RetrieveMultipleAsync(delegationParameters, cancellationToken).ConfigureAwait(false);

                return rows;
            }
        }

#pragma warning disable CS0618 // Type or member is obsolete

        internal virtual async Task<IReadOnlyCollection<DValue<RecordValue>>> RetrieveMultipleAsync(DataverseDelegationParameters delegationParameters, CancellationToken cancellationToken)
#pragma warning restore CS0618 // Type or member is obsolete
        {
            cancellationToken.ThrowIfCancellationRequested();
            QueryExpression query = CreateQueryExpression(_entityMetadata.LogicalName, delegationParameters);

            DataverseResponse<EntityCollection> entityCollectionResponse = await _connection.Services.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false);

            if (entityCollectionResponse.HasError)
            {
                return new List<DValue<RecordValue>>() { entityCollectionResponse.DValueError("RetrieveMultiple") };
            }

            List<DValue<RecordValue>> result = await EntityCollectionToRecordValuesAsync(entityCollectionResponse.Response, delegationParameters, cancellationToken).ConfigureAwait(false);

            return result;
        }

        public async Task<FormulaValue> ExecuteQueryAsync(IServiceProvider services, DelegationParameters parameters, CancellationToken cancellationToken)
        {
            // if condition expression is empty, we can call separate function to get count of the table.
            cancellationToken.ThrowIfCancellationRequested();
            var delegationParameters = (DataverseDelegationParameters)parameters;
            if (delegationParameters.ReturnTotalRowCount == true)
            {
                var count = await GetCountAsync(services, delegationParameters, cancellationToken).ConfigureAwait(false);
                var countFV = ToFxNumericFormulaValue(count, delegationParameters.ExpectedReturnType);
                return countFV;
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                QueryExpression query = CreateQueryExpression(_entityMetadata.LogicalName, delegationParameters);

                DataverseResponse<EntityCollection> entityCollectionResponse = await _connection.Services.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false);

                if (entityCollectionResponse.HasError)
                {
                    throw new CustomFunctionErrorException($"Error while executing query in ExecuteQueryAsync: {entityCollectionResponse.Error}");
                }

                if (entityCollectionResponse.Response.Entities.Count == 0)
                {
                    return FormulaValue.NewBlank(delegationParameters.ExpectedReturnType);
                }

                if (entityCollectionResponse.Response.Entities.Count > 1)
                {
                    throw new InvalidOperationException($"Expected less or equal one entity, found {entityCollectionResponse.Response.Entities.Count} entities.");
                }

                var aggregationValue = entityCollectionResponse.Response.Entities.First().Attributes.First().Value;

                if (aggregationValue is AliasedValue aliasedValue)
                {
                    aggregationValue = aliasedValue.Value;
                }

                var aggregationFV = ToFxNumericFormulaValue(aggregationValue, delegationParameters.ExpectedReturnType);
                return aggregationFV;
            }
        }

        private static FormulaValue ToFxNumericFormulaValue(object value, FormulaType expectedFT)
        {
            if (expectedFT != FormulaType.Decimal && expectedFT != FormulaType.Number)
            {
                throw new InvalidOperationException($"Expected type is number or decimal, found {expectedFT}");
            }

            if (!DataverseRecordValue.TryParseDataverseValueNoNetworkRequest(expectedFT, value, CancellationToken.None, out var fxValue))
            {
                throw new InvalidOperationException($"Could not conver {value} to {expectedFT} in {nameof(DataverseRecordValue.TryParseDataverseValueNoNetworkRequest)}");
            }

            return fxValue;
        }

        private async Task<long> GetCountAsync(IServiceProvider services, DataverseDelegationParameters parameters, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // If it is counting entire table, we can use RetrieveTotalRecordCountRequest to get the count for entire table. IE select count(*) from table.
            if (parameters.IsCountingEntireTable())
            {
                // https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/reference/retrievetotalrecordcount?view=dataverse-latest
                var response = await _connection.Services.ExecuteAsync(new RetrieveTotalRecordCountRequest() { EntityNames = new string[] { _entityMetadata.LogicalName } });
                var response2 = (RetrieveTotalRecordCountResponse)response.Response;
                if (response2.EntityRecordCountCollection.TryGetValue(_entityMetadata.LogicalName, out var totalCount))
                {
                    return totalCount;
                }

                throw new InvalidOperationException($"Response incorrect executing query in {nameof(GetCountAsync)}");
            }
            else
            {
                // If it is counting based on filter, etc., we can use QueryExpression to get the count. IE select count(*) from table where filter.
                var delegationParameters = (DataverseDelegationParameters)parameters;
                var query = CreateQueryExpression(_entityMetadata.LogicalName, delegationParameters);

                // https://learn.microsoft.com/en-us/dotnet/api/microsoft.xrm.sdk.entitycollection.totalrecordcount?view=dataverse-sdk-latest#microsoft-xrm-sdk-entitycollection-totalrecordcount
                query.PageInfo.ReturnTotalRecordCount = true;
                DataverseResponse<EntityCollection> entityCollectionResponse = await _connection.Services.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false);

                if (entityCollectionResponse.HasError)
                {
                    throw new CustomFunctionErrorException($"Error while executing query in {nameof(GetCountAsync)}");
                }
                else if (entityCollectionResponse.Response.TotalRecordCountLimitExceeded)
                {
                    // https://learn.microsoft.com/en-us/dotnet/api/microsoft.xrm.sdk.entitycollection.totalrecordcountlimitexceeded?view=dataverse-sdk-latest#microsoft-xrm-sdk-entitycollection-totalrecordcountlimitexceeded
                    throw new CustomFunctionErrorException($"Total record count limit exceeded in {nameof(GetCountAsync)}");
                }

                return entityCollectionResponse.Response.TotalRecordCount;
            }
        }

#pragma warning disable CS0618 // Type or member is obsolete

        internal virtual async Task<IReadOnlyCollection<DValue<RecordValue>>> RetrieveMultipleAsync(DataverseDelegationParameters delegationParameters, string partitionId, CancellationToken cancellationToken)
#pragma warning restore CS0618 // Type or member is obsolete
        {
            cancellationToken.ThrowIfCancellationRequested();

            var query = CreateQueryExpression(_entityMetadata.LogicalName, delegationParameters);

            var request = new RetrieveMultipleRequest
            {
                Query = query,

                // Important that below is camel cased.
                ["partitionId"] = partitionId
            };

            var response = await _connection.Services.ExecuteAsync(request);

            if (response.HasError)
            {
                return new List<DValue<RecordValue>>() { response.DValueError("RetrieveMultiple") };
            }

            EntityCollection entities = ((RetrieveMultipleResponse)response.Response).EntityCollection;
            return await EntityCollectionToRecordValuesAsync(entities, delegationParameters, cancellationToken).ConfigureAwait(false);
        }

#pragma warning disable CS0618 // Type or member is obsolete

        internal static QueryExpression CreateQueryExpression(string entityName, DataverseDelegationParameters delegationParameters)
#pragma warning restore CS0618 // Type or member is obsolete
        {
            bool hasDistinct = delegationParameters.HasDistinct();

            var query = new QueryExpression(entityName)
            {
                ColumnSet = delegationParameters.ColumnMap.ToXRMColumnSet(delegationParameters.GroupBy),
                Criteria = delegationParameters.FxFilter?.GetDataverseFilterExpression() ?? new FilterExpression(),
                Distinct = hasDistinct
            };

            if (!delegationParameters.Joins.IsNullOrEmpty())
            {
                /* right table renames in LinkEntity */
                query.LinkEntities.AddRange(delegationParameters.Joins.Select(join => join.ToXRMLinkEntity()));
            }

            if (delegationParameters.Top != null)
            {
                query.TopCount = delegationParameters.Top;
            }

            if (delegationParameters.OrderBy != null && delegationParameters.OrderBy.Any())
            {
                if (delegationParameters.ColumnMap != null)
                {
                    IReadOnlyDictionary<string, FxColumnInfo> map = delegationParameters.ColumnMap.ColumnInfoMap.Values.ToDictionary(cInfo => cInfo.RealColumnName);

                    foreach (OrderExpression oe in delegationParameters.OrderBy)
                    {
                        if (map.TryGetValue(oe.AttributeName, out FxColumnInfo columnInfo))
                        {
                            var attrinuteName = columnInfo.RealColumnName;
                            var attributeAlias = columnInfo.AliasColumnName;
                            query.Orders.Add(new OrderExpression(attrinuteName, oe.OrderType, attributeAlias, oe.EntityName));
                        }
                        else
                        {
                            query.Orders.Add(oe);
                        }
                    }
                }
                else
                {
                    query.Orders.AddRange(delegationParameters.OrderBy);
                }
            }

            return query;
        }

        public override async Task<DValue<RecordValue>> AppendAsync(RecordValue record, CancellationToken cancellationToken = default)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            cancellationToken.ThrowIfCancellationRequested();
            Entity entity = record.ConvertRecordToEntity(_entityMetadata, out DValue<RecordValue> error);

            if (error != null)
            {
                return error;
            }

            DataverseResponse<Guid> response = await _connection.Services.CreateAsync(entity, cancellationToken).ConfigureAwait(false);

            if (response.HasError)
            {
                return response.DValueError(nameof(IDataverseCreator.CreateAsync));
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Once inserted, let's get the newly created Entity with all its attributes
            DataverseResponse<Entity> newEntity = await _connection.Services.RetrieveAsync(_entityMetadata.LogicalName, response.Response, columns: null, cancellationToken).ConfigureAwait(false);

            if (newEntity.HasError)
            {
                return newEntity.DValueError(nameof(IDataverseReader.RetrieveAsync));
            }

            // After mutation, lazily refresh Rows from server.
            Refresh();

            return DValue<RecordValue>.Of(new DataverseRecordValue(newEntity.Response, _entityMetadata, Type.ToRecord(), _connection));
        }

        protected override async Task<DValue<RecordValue>> PatchCoreAsync(RecordValue baseRecord, RecordValue record, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (baseRecord == null)
            {
                throw new ArgumentNullException(nameof(baseRecord));
            }

            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            // Retrieve the primary key of the entity (should always be present and a Guid)
            FormulaValue fieldFormulaValue = baseRecord.GetField(_entityMetadata.PrimaryIdAttribute);

            cancellationToken.ThrowIfCancellationRequested();

            if (fieldFormulaValue.Type == FormulaType.Blank)
            {
                return DataverseExtensions.DataverseError<RecordValue>($"record doesn't contain primary Id", nameof(PatchCoreAsync));
            }

            if (fieldFormulaValue is not GuidValue id)
            {
                return DataverseExtensions.DataverseError<RecordValue>($"primary Id isn't a Guid", nameof(PatchCoreAsync));
            }

            var ret = await DataverseRecordValue.UpdateEntityAsync(id.Value, record, _entityMetadata, _recordType, _connection, cancellationToken).ConfigureAwait(false);

            // After mutation, lazely refresh Rows from server.
            Refresh();

            return ret;
        }

        public override async Task<DValue<BooleanValue>> RemoveAsync(IEnumerable<FormulaValue> recordsToRemove, bool all, CancellationToken cancellationToken = default)
        {
            if (recordsToRemove == null)
            {
                throw new ArgumentNullException(nameof(recordsToRemove));
            }

            if (!recordsToRemove.All(rtr => rtr is RecordValue))
            {
                throw new ArgumentException($"All elements to be deleted must be of type RecordValue");
            }

            foreach (var record in recordsToRemove.OfType<RecordValue>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                FormulaValue fv = record.GetField(_entityMetadata.PrimaryIdAttribute);

                if (fv.Type == FormulaType.Blank || fv is not GuidValue id)
                {
                    return DataverseExtensions.DataverseError<BooleanValue>("Dataverse record doesn't contain primary Id, of Guid type", nameof(RemoveAsync));
                }
                else
                {
                    DataverseResponse response = await _connection.Services.DeleteAsync(_entityMetadata.LogicalName, id.Value, cancellationToken).ConfigureAwait(false);

                    if (response.HasError)
                    {
                        return DataverseExtensions.DataverseError<BooleanValue>(response.Error, nameof(RemoveAsync));
                    }
                }
            }

            // After mutation, lazely refresh Rows from server.
            Refresh();

            return DValue<BooleanValue>.Of(BooleanValue.New(true));
        }

        public override void ToExpression(StringBuilder sb, FormulaValueSerializerSettings settings)
        {
            // Serialize table as the table name.
            // Explicitly avoid enumerating all rows.
            var name = this._connection.GetSerializationName(_entityMetadata.LogicalName);
            sb.Append(IdentToken.MakeValidIdentifier(name));
        }

        /// <summary>
        /// API to clear previous stored rows.
        /// </summary>
        public void RefreshCache()
        {
            Refresh();

            if (_connection.Services is IDataverseEntityCacheCleaner decc)
            {
                decc.ClearCache(_entityMetadata.LogicalName);
            }
        }

        public override DValue<RecordValue> CastRecord(RecordValue record, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record is not DataverseRecordValue)
            {
                throw new CustomFunctionErrorException($"Given record was not of dataverse type");
            }

            var dvRecord = (DataverseRecordValue)record;
            if (dvRecord.Entity.LogicalName != _entityMetadata.LogicalName)
            {
                ExpressionError error = DataverseHelpers.GetInvalidCastError(new string[] { dvRecord.Entity.LogicalName, _entityMetadata.LogicalName });
                throw new CustomFunctionErrorException(error);
            }

            var row = new DataverseRecordValue(dvRecord.Entity, dvRecord.Metadata, Type.ToRecord(), _connection);

            return DValue<RecordValue>.Of(row);
        }

        private async Task<List<DValue<RecordValue>>> EntityCollectionToRecordValuesAsync(EntityCollection entityCollection, DataverseDelegationParameters delegationParameters, CancellationToken cancellationToken)
        {
            if (entityCollection == null)
            {
                throw new ArgumentNullException(nameof(entityCollection));
            }

            cancellationToken.ThrowIfCancellationRequested();
            List<DValue<RecordValue>> list = new ();
            RecordType recordType = (RecordType)delegationParameters.ExpectedReturnType;

            bool returnsDVRecordValue = delegationParameters.GroupBy == null && delegationParameters.Join == null;

            foreach (Entity entity in entityCollection.Entities)
            {
                RecordValue recordValue;

                if (returnsDVRecordValue)
                {
                    recordValue = new DataverseRecordValue(entity, _entityMetadata, recordType, _connection);
                }
                else
                {
                    List<NamedValue> namedValues = new List<NamedValue>();

                    foreach (NamedFormulaType nft in recordType.GetFieldTypes())
                    {
                        var fieldName = nft.Name.Value;
                        var fieldType = nft.Type;
                        FormulaValue fieldValue;

                        if (DataverseRecordValue.TryGetAttributeOrRelationship(_entityMetadata, entity, fieldName, out var val))
                        {
                            // If entity Marshalling needs network request we assign blank().
                            if (!DataverseRecordValue.TryParseDataverseValueNoNetworkRequest(fieldType, val, cancellationToken, out fieldValue))
                            {
                                fieldValue = FormulaValue.NewBlank(fieldType);
                            }
                        }
                        else if (delegationParameters.Join != null && 
                            delegationParameters.Join.JoinTableRecordType != null &&
                            DelegationUtility.TryGetEntityMetadata(delegationParameters.Join.JoinTableRecordType, out var rightTableMetadata) &&
                            DataverseRecordValue.TryGetAttributeOrRelationship(rightTableMetadata, entity, fieldName, out var rightTableVal))
                        {
                            if (!DataverseRecordValue.TryParseDataverseValueNoNetworkRequest(fieldType, rightTableVal, cancellationToken, out fieldValue))
                            {
                                fieldValue = FormulaValue.NewBlank(fieldType);
                            }
                        }
                        else if (fieldType is AggregateType)
                        {
                            // one to many relationship case.
                            fieldValue = FormulaValue.NewBlank(fieldType);
                        }
                        else
                        {
                            throw new InvalidOperationException($"Field {fieldName} not found in {entity.LogicalName} or in right Table of the join.");
                        }

                        namedValues.Add(new NamedValue(fieldName, fieldValue));
                    }

                    recordValue = FormulaValue.NewRecordFromFields(namedValues.ToArray());
                }

                list.Add(DValue<RecordValue>.Of(recordValue));
            }

            if (_connection.MaxRows > 0 && list.Count > _connection.MaxRows)
            {
                list.Remove(list.Last());
                list.Add(DataverseExtensions.DataverseError<RecordValue>($"Too many entities in table {_entityMetadata.LogicalName}, more than {_connection.MaxRows} rows", nameof(GetRowsAsync)));
            }

            return list;
        }

        private List<DValue<RecordValue>> EntityCollectionToRecordValues(EntityCollection entityCollection)
        {
            if (entityCollection == null)
            {
                throw new ArgumentNullException(nameof(entityCollection));
            }

            List<DValue<RecordValue>> list = new ();

            foreach (Entity entity in entityCollection.Entities)
            {
                var row = new DataverseRecordValue(entity, _entityMetadata, Type.ToRecord(), _connection);
                list.Add(DValue<RecordValue>.Of(row));
            }

            if (_connection.MaxRows > 0 && list.Count > _connection.MaxRows)
            {
                list.Remove(list.Last());
                list.Add(DataverseExtensions.DataverseError<RecordValue>($"Too many entities in table {_entityMetadata.LogicalName}, more than {_connection.MaxRows} rows", nameof(GetRowsAsync)));
            }

            return list;
        }
    }
}
