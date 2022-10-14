//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Connectors;
using Microsoft.PowerFx.Interpreter;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Wrap a live Dataverse Table. 
    /// </summary>
    internal class DataverseTableValue : ODataQueryableTableValue
    {
        private readonly IDataverseServices _connection;
        private readonly EntityMetadata _entityMetadata;
        private ODataParameters _oDataParameters;
        private RecordType _recordType;

        public new TableType Type => (TableType)base.Type;

        internal DataverseTableValue(RecordType recordType, IDataverseServices connection, EntityMetadata metadata, ODataParameters oDataParameters = default)
            : base(recordType.ToTable(), oDataParameters)
        {
            _recordType = recordType;
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _entityMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _oDataParameters = oDataParameters;
        }

        public override object ToObject()
        {
            throw new NotImplementedException("DataverseTableValue.ToObject() isn't implemented yet.");
        }

        protected override ODataQueryableTableValue WithParameters(ODataParameters odataParameters)
        {
            if (!odataParameters.IsSupported())
                throw new NotDelegableException();

            var oData = _oDataParameters;

            if (odataParameters.Top > 0)
                oData = oData.WithTop(odataParameters.Top);
            if (!string.IsNullOrEmpty(odataParameters.Filter))
                oData = oData.WithFilter(odataParameters.Filter);

            return new DataverseTableValue(_recordType, _connection, _entityMetadata, oData);
        }

        protected override async Task<List<DValue<RecordValue>>> GetRowsAsync()
        {
            List<DValue<RecordValue>> list = new();
            DataverseResponse<EntityCollection> entities = await _connection.QueryAsync(_entityMetadata.LogicalName, _oDataParameters);

            if (entities.HasError)
                return new List<DValue<RecordValue>> { entities.DValueError(nameof(QueryExtensions.QueryAsync))};

            foreach (Entity entity in entities.Response.Entities)
            {
                var row = new DataverseRecordValue(entity, _entityMetadata, Type.ToRecord(), _connection);
                list.Add(DValue<RecordValue>.Of(row));
            }

            return list;
        }

        public override async Task<DValue<RecordValue>> AppendAsync(RecordValue record, CancellationToken cancellationToken)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            Entity entity = record.ToEntity(_entityMetadata);
            DataverseResponse<Guid> response = await _connection.CreateAsync(entity, cancellationToken);

            if (response.HasError)
                return response.DValueError(nameof(IDataverseCreator.CreateAsync));

            // Once inserted, let's get the newly created Entity with all its attributes
            DataverseResponse<Entity> newEntity = await _connection.RetrieveAsync(_entityMetadata.LogicalName, response.Response, cancellationToken);

            if (newEntity.HasError)
                return newEntity.DValueError(nameof(IDataverseReader.RetrieveAsync));

            return DValue<RecordValue>.Of(new DataverseRecordValue(newEntity.Response, _entityMetadata, Type.ToRecord(), _connection));
        }

        protected override async Task<DValue<RecordValue>> PatchCoreAsync(RecordValue baseRecord, RecordValue record, CancellationToken cancellationToken)
        {
            if (baseRecord == null)
                throw new ArgumentNullException(nameof(baseRecord));
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            // Retrieve the primary key of the entity (should alwyas be present and a Guid)
            FormulaValue fv = baseRecord.GetField(_entityMetadata.PrimaryIdAttribute);

            if (fv.Type == FormulaType.Blank)
            {
                return DataverseExtensions.DataverseError<RecordValue>($"record doesn't contain primary Id", nameof(PatchCoreAsync));
            }

            if (fv is not GuidValue id)
                return DataverseExtensions.DataverseError<RecordValue>($"primary Id isn't a Guid", nameof(PatchCoreAsync));

            DataverseResponse<Entity> entityResponse = await _connection.RetrieveAsync(_entityMetadata.LogicalName, id.Value, cancellationToken);

            if (entityResponse.HasError)
                return entityResponse.DValueError(nameof(IDataverseReader.RetrieveAsync));

            var item = new DataverseRecordValue(entityResponse.Response, _entityMetadata, Type.ToRecord(), _connection);
            return await item.UpdateFieldsAsync(record, cancellationToken);
        }

        public async override Task<DValue<BooleanValue>> RemoveAsync(IEnumerable<FormulaValue> recordsToRemove, bool all, CancellationToken cancellationToken)
        {
            if (recordsToRemove == null)
                throw new ArgumentNullException(nameof(recordsToRemove));
            if (!recordsToRemove.All(rtr => rtr is RecordValue))
                throw new ArgumentException($"All elements to be deleted must be of type RecordValue");

            foreach (var record in recordsToRemove.OfType<RecordValue>())
            {
                FormulaValue fv = record.GetField(_entityMetadata.PrimaryIdAttribute);

                if (fv.Type == FormulaType.Blank || fv is not GuidValue id)
                {
                    return DataverseExtensions.DataverseError<BooleanValue>("Dataverse record doesn't contain primary Id, of Guid type", nameof(RemoveAsync));
                }
                else
                {
                    DataverseResponse response = await _connection.DeleteAsync(_entityMetadata.LogicalName, id.Value, cancellationToken);

                    if (response.HasError)
                        return DValue<BooleanValue>.Of(BooleanValue.New(false));
                }
            }

            return DValue<BooleanValue>.Of(BooleanValue.New(true));
        }
    }
}
