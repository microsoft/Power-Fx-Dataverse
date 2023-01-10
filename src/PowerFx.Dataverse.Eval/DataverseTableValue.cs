//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Connectors;
using Microsoft.PowerFx.Interpreter;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
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
    internal class DataverseTableValue : ODataQueryableTableValue
    {
        private readonly IConnectionValueContext _connection;
        private ODataParameters _oDataParameters;
        private RecordType _recordType;

        public readonly EntityMetadata _entityMetadata;

        internal DataverseTableValue(RecordType recordType, IConnectionValueContext connection, EntityMetadata metadata, ODataParameters oDataParameters = default)
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
            DataverseResponse<EntityCollection> entities = await _connection.Services.QueryAsync(_entityMetadata.LogicalName, _oDataParameters);

            if (entities.HasError)
                return new List<DValue<RecordValue>> { entities.DValueError(nameof(QueryExtensions.QueryAsync)) };

            foreach (Entity entity in entities.Response.Entities)
            {
                var row = new DataverseRecordValue(entity, _entityMetadata, Type.ToRecord(), _connection);
                list.Add(DValue<RecordValue>.Of(row));
            }

            return list;
        }

        public override async Task<DValue<RecordValue>> AppendAsync(RecordValue record, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            cancellationToken.ThrowIfCancellationRequested();

            Entity entity = record.ToEntity(_entityMetadata);
            DataverseResponse<Guid> response = await _connection.Services.CreateAsync(entity, cancellationToken);

            if (response.HasError)
                return response.DValueError(nameof(IDataverseCreator.CreateAsync));

            cancellationToken.ThrowIfCancellationRequested();

            // Once inserted, let's get the newly created Entity with all its attributes
            DataverseResponse<Entity> newEntity = await _connection.Services.RetrieveAsync(_entityMetadata.LogicalName, response.Response, cancellationToken);

            if (newEntity.HasError)
                return newEntity.DValueError(nameof(IDataverseReader.RetrieveAsync));

            return DValue<RecordValue>.Of(new DataverseRecordValue(newEntity.Response, _entityMetadata, Type.ToRecord(), _connection));
        }

        protected override async Task<DValue<RecordValue>> PatchCoreAsync(RecordValue baseRecord, RecordValue record, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (baseRecord == null)
                throw new ArgumentNullException(nameof(baseRecord));
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            // Retrieve the primary key of the entity (should alwyas be present and a Guid)
            FormulaValue fv = baseRecord.GetField(_entityMetadata.PrimaryIdAttribute);

            cancellationToken.ThrowIfCancellationRequested();

            if (fv.Type == FormulaType.Blank)
            {
                return DataverseExtensions.DataverseError<RecordValue>($"record doesn't contain primary Id", nameof(PatchCoreAsync));
            }

            if (fv is not GuidValue id)
                return DataverseExtensions.DataverseError<RecordValue>($"primary Id isn't a Guid", nameof(PatchCoreAsync));

            DataverseResponse<Entity> entityResponse = await _connection.Services.RetrieveAsync(_entityMetadata.LogicalName, id.Value, cancellationToken);

            if (entityResponse.HasError)
                return entityResponse.DValueError(nameof(IDataverseReader.RetrieveAsync));

            var item = new DataverseRecordValue(entityResponse.Response, _entityMetadata, Type.ToRecord(), _connection);
            return await item.UpdateFieldsAsync(record, cancellationToken);
        }

        public async override Task<DValue<BooleanValue>> RemoveAsync(IEnumerable<FormulaValue> recordsToRemove, bool all, CancellationToken cancellationToken = default(CancellationToken))
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
                    DataverseResponse response = await _connection.Services.DeleteAsync(_entityMetadata.LogicalName, id.Value, cancellationToken);

                    if (response.HasError)
                        return DataverseExtensions.DataverseError<BooleanValue>(response.Error, nameof(RemoveAsync));
                }
            }

            return DValue<BooleanValue>.Of(BooleanValue.New(true));
        }

        public override void ToExpression(StringBuilder sb, FormulaValueSerializerSettings settings)
        {
            // Serialize table as the table name.
            // Explicitly avoid enumerating all rows. 
            var name = this._connection.GetSerializationName(_entityMetadata.LogicalName);
            sb.Append(IdentToken.MakeValidIdentifier(name));
        }
    }
}
