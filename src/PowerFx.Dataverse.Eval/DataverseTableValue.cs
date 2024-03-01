//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Interpreter;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Wrap a live Dataverse Table. 
    /// </summary>
    internal class DataverseTableValue : TableValue, IRefreshable
    {
        private readonly IConnectionValueContext _connection;
        private readonly RecordType _recordType;

        internal IConnectionValueContext Connection => _connection;

        private Lazy<Task<List<DValue<RecordValue>>>> _lazyTaskRows;

        private Lazy<Task<List<DValue<RecordValue>>>> NewLazyTaskRowsInstance => new Lazy<Task<List<DValue<RecordValue>>>>(() => GetRowsAsync());

        public bool HasCachedRows => _lazyTaskRows.IsValueCreated;

        public sealed override IEnumerable<DValue<RecordValue>> Rows => _lazyTaskRows.Value.ConfigureAwait(false).GetAwaiter().GetResult();
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
            var services = _connection.Services.dataverseServices;
            if (services is IDataverseRefresh serviceRefresh)
            {
                serviceRefresh.Refresh(_entityMetadata.LogicalName);
            }
        }

        protected async Task<List<DValue<RecordValue>>> GetRowsAsync()
        {
            List<DValue<RecordValue>> list = new ();
            DataverseResponse<EntityCollection> entities = await _connection.Services.QueryAsync(_entityMetadata.LogicalName, _connection.MaxRows).ConfigureAwait(false);

            if (entities.HasError)
                return new List<DValue<RecordValue>> { entities.DValueError(nameof(QueryExtensions.QueryAsync)) };

            var result = EntityCollectionToRecordValues(entities);
            return result;
        }

        public async Task<DValue<RecordValue>> RetrieveAsync(Guid id, IEnumerable<string> columns, CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = await _connection.Services.RetrieveAsync(_entityMetadata.LogicalName, id, columns, cancellationToken).ConfigureAwait(false);

            if (result.HasError)
            {
                return result.DValueError("Retrieve");
        }

            Entity entity = result.Response;
            var row = new DataverseRecordValue(entity, _entityMetadata, Type.ToRecord(), _connection);

            return DValue<RecordValue>.Of(row);
        }

        public async Task<DValue<RecordValue>> RetrieveAsync(Guid id, string partitionId, IEnumerable<string> columns, CancellationToken cancellationToken = default(CancellationToken))
        {
            var entityReference = new EntityReference(this._entityMetadata.LogicalName, id);

            var request = new RetrieveRequest
            {
                ColumnSet = columns == null ? new ColumnSet(true) : new ColumnSet(columns.ToArray()),
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

        internal async Task<IEnumerable<DValue<RecordValue>>> RetrieveMultipleAsync(FilterExpression filter, ISet<LinkEntity> relation, int? count, IEnumerable<string> columnSet, bool isDistinct, CancellationToken cancel)
        {
            var query = CreateQueryExpression(_entityMetadata.LogicalName, filter, relation, count, columnSet, isDistinct);

            var entities = await _connection.Services.RetrieveMultipleAsync(query, cancel).ConfigureAwait(false);

            if (entities.HasError)
            {
                return new List<DValue<RecordValue>>() { entities.DValueError("RetrieveMultiple") };
            }

            var result = EntityCollectionToRecordValues(entities);

            return result;
        }

        internal async Task<IEnumerable<DValue<RecordValue>>> RetrieveMultipleAsync(FilterExpression filter, ISet<LinkEntity> relation, string partitionId, int? count, IEnumerable<string> columnSet, bool isDistinct, CancellationToken cancel)
        {
            var query = CreateQueryExpression(_entityMetadata.LogicalName, filter, relation, count, columnSet, isDistinct);

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

            var entities = ((RetrieveMultipleResponse)response.Response).EntityCollection;
            return EntityCollectionToRecordValues(entities);
        }

        internal static QueryExpression CreateQueryExpression(string entityName, FilterExpression filter, ISet<LinkEntity> relation, int? count, IEnumerable<string> columnSet, bool isDistinct)
        {
            var columns = columnSet != null ? new ColumnSet(columnSet.ToArray()) : new ColumnSet(true);

            var query = new QueryExpression(entityName)
            {
                ColumnSet = columns,
                Criteria = filter ?? new FilterExpression(),
                Distinct = isDistinct
            };

            if (count != null)
            {
                query.TopCount = count;
            }

            if (relation != null && relation.Count > 0)
            {
                query.LinkEntities.AddRange(relation);
            }

            return query;
        }


        public override async Task<DValue<RecordValue>> AppendAsync(RecordValue record, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            cancellationToken.ThrowIfCancellationRequested();
            Entity entity = record.ConvertRecordToEntity(_entityMetadata, out DValue<RecordValue> error);

            if (error != null)
            {
                return error;
            }

            DataverseResponse<Guid> response = await _connection.Services.CreateAsync(entity, cancellationToken).ConfigureAwait(false);

            if (response.HasError)
                return response.DValueError(nameof(IDataverseCreator.CreateAsync));

            cancellationToken.ThrowIfCancellationRequested();

            // Once inserted, let's get the newly created Entity with all its attributes
            DataverseResponse<Entity> newEntity = await _connection.Services.RetrieveAsync(_entityMetadata.LogicalName, response.Response, columns:null, cancellationToken).ConfigureAwait(false);

            if (newEntity.HasError)
                return newEntity.DValueError(nameof(IDataverseReader.RetrieveAsync));

            // After mutation, lazily refresh Rows from server.
            Refresh();

            return DValue<RecordValue>.Of(new DataverseRecordValue(newEntity.Response, _entityMetadata, Type.ToRecord(), _connection));
        }

        protected override async Task<DValue<RecordValue>> PatchCoreAsync(RecordValue baseRecord, RecordValue record, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (baseRecord == null)
                throw new ArgumentNullException(nameof(baseRecord));
            if (record == null)
                throw new ArgumentNullException(nameof(record));

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

        public override async Task<DValue<BooleanValue>> RemoveAsync(IEnumerable<FormulaValue> recordsToRemove, bool all, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (recordsToRemove == null)
                throw new ArgumentNullException(nameof(recordsToRemove));
            if (!recordsToRemove.All(rtr => rtr is RecordValue))
                throw new ArgumentException($"All elements to be deleted must be of type RecordValue");

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
                        return DataverseExtensions.DataverseError<BooleanValue>(response.Error, nameof(RemoveAsync));
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

        private List<DValue<RecordValue>> EntityCollectionToRecordValues(DataverseResponse<EntityCollection> entityCollection)
        {
            if (entityCollection == null)
            {
                throw new ArgumentNullException(nameof(entityCollection));
            }

            List<DValue<RecordValue>> list = new ();

            foreach (Entity entity in entityCollection.Response.Entities)
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

        private List<DValue<RecordValue>> EntityCollectionToRecordValues(EntityCollection entityCollection)
        {
            if (entityCollection == null)
            {
                throw new ArgumentNullException(nameof(entityCollection));
            }

            List<DValue<RecordValue>> list = new();

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
