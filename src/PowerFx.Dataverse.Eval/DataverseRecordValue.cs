// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using XrmOptionSetValue = Microsoft.Xrm.Sdk.OptionSetValue;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Wrap a <see cref="Entity"/> as a <see cref="RecordValue"/> to pass to Power Fx.
    /// </summary>
    internal class DataverseRecordValue : RecordValue
    {
        // The underlying entity (= table row)
        private Entity _entity;

        private readonly EntityMetadata _metadata;

        // Used to resolve entity relationships (dot operators).
        private readonly IConnectionValueContext _connection;

        internal DataverseRecordValue(Entity entity, EntityMetadata metadata, RecordType recordType, IConnectionValueContext connection)
            : base(recordType)
        {
            _entity = entity ?? throw new ArgumentNullException(nameof(entity));
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));

            if (_entity.LogicalName != _metadata.LogicalName)
            {
                // Need to be for the same entity.
                throw new ArgumentException($"Entity {_entity.LogicalName} doesn't match Metadata {_metadata.LogicalName}.");
            }
        }

        public override bool TryShallowCopy(out FormulaValue copy)
        {
            copy = this;
            return true;
        }

        public override bool CanShallowCopy => true;

        internal Entity Entity => _entity;

        internal EntityMetadata Metadata => _metadata;

        public EntityReference EntityReference => _entity.ToEntityReference();

        public override bool TryGetSpecialFieldName(SpecialFieldKind kind, out string fieldName)
        {
            fieldName = kind switch
            {
                SpecialFieldKind.PrimaryKey => this._metadata.PrimaryIdAttribute,
                SpecialFieldKind.PrimaryName => this._metadata.PrimaryNameAttribute,
                SpecialFieldKind.PrimaryImage => this._metadata.PrimaryImageAttribute,
                _ => null
            };

            return fieldName != null;
        }

        public override bool TryGetPrimaryKey(out string key)
        {
            key = _entity.Id.ToString();
            return true;
        }

        private bool TryGetAttributeOrRelationship(string innerFieldName, out object value)
        {
            // IR should convert the fieldName from display to Logical Name.
            if (_entity.Attributes.TryGetValue(innerFieldName, out value))
            {
                return true;
            }

            if (_metadata.TryGetRelationship(innerFieldName, out var realAttributeName))
            {
                if (_entity.Attributes.TryGetValue(realAttributeName, out value))
                {
                    return true;
                }
            }
            else if (_metadata.TryGetOneToManyRelationship(innerFieldName, out var relation))
            {
                value = relation;
                return true;
            }

            value = null;
            return false;
        }

        protected override bool TryGetField(FormulaType fieldType, string fieldName, out FormulaValue result)
        {
            (bool isSuccess, FormulaValue value) = TryGetFieldAsync(fieldType, fieldName, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
            result = value;
            return isSuccess;
        }

        public override async Task<(bool Result, FormulaValue Value)> TryGetFieldAsync(FormulaType fieldType, string fieldName, CancellationToken cancellationToken)
        {
            FormulaValue result;

            // If primary key is missing from Attributes, still get it from the entity.
            if (fieldName == GetPrimaryKeyName())
            {
                result = FormulaValue.New(_entity.Id);
                return (true, result);
            }

            // IR should convert the fieldName from display to Logical Name.
            if (!TryGetAttributeOrRelationship(fieldName, out var value) || value == null)
            {
                result = null;
                return (false, result);
            }

            if (value is OneToManyRelationshipMetadata relationshipMetadata)
            {
                result = await ResolveOneToManyRelationship(relationshipMetadata, fieldType, cancellationToken).ConfigureAwait(false);
                return (true, result);
            }

            if (value is EntityReference reference)
            {
                // Blank was already handled, value would have been null.
                result = await ResolveEntityReferenceAsync(reference, fieldType, columns: null, cancellationToken).ConfigureAwait(false);
                return (true, result);
            }

            if (value is DateTime dt && dt.Kind == DateTimeKind.Utc)
            {
                // $$$ Get correct timezone from symbols
                value = TimeZoneInfo.ConvertTimeFromUtc(dt, TimeZoneInfo.Local);
            }

            // For some specific column types we need to extract the primitive value.
            if (value is Money money)
            {
                result = PrimitiveValueConversions.Marshal(money.Value, fieldType);
                return (true, result);
            }

            // Handle primitives
            if (PrimitiveValueConversions.TryMarshal(value, fieldType, out result))
            {
                return (true, result);
            }

            // Options, enums, etc?
            if (fieldType is OptionSetValueType opt)
            {
                if (TryGetValue(opt, value, out var osResult))
                {
                    result = osResult;
                    return (true, result);
                }
            }

            // Multi-select column type
            if (fieldType is TableType tableType && value is OptionSetValueCollection optionSetValueCollection)
            {
                result = ResolveMultiSelectChoice(optionSetValueCollection, tableType, cancellationToken);
                return (true, result);
            }

            _metadata.TryGetAttribute(fieldName, out var attributeMetadata);

            // Not supported FormulaType types.
            var expressionError = new ExpressionError()
            {
                Kind = ErrorKind.Unknown,
                Severity = ErrorSeverity.Critical,
                Message = string.Format("{0} column type not supported.", attributeMetadata != null ? attributeMetadata.AttributeTypeName.Value : fieldType)
            };

            result = NewError(expressionError);
            return (true, result);
        }

        private static FormulaValue ResolveMultiSelectChoice(OptionSetValueCollection optionSetValueCollection, TableType tableType, CancellationToken cancellationToken)
        {
            var records = new List<RecordValue>();

            foreach (var xrmOptionSetValue in optionSetValueCollection)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fxOptionSetValue = AttributeUtility.ConvertXrmOptionSetValueToFormulaValue(tableType, xrmOptionSetValue);
                records.Add(NewRecordFromFields(new NamedValue(tableType.FieldNames.First(), fxOptionSetValue)));
            }

            return NewTable(tableType.ToRecord(), records);
        }

        private async Task<FormulaValue> ResolveOneToManyRelationship(OneToManyRelationshipMetadata relationshipMetadata, FormulaType fieldType, CancellationToken cancellationToken)
        {
            var refernecingTable = relationshipMetadata.ReferencingEntity;
            string referencingAttribute = relationshipMetadata.ReferencingAttribute;
            FormulaValue result;
            var recordType = ((TableType)fieldType).ToRecord() ?? throw new InvalidOperationException("Field Type should be a table value");
            var query = new QueryExpression(refernecingTable)
            {
                ColumnSet = new ColumnSet(true),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(referencingAttribute, ConditionOperator.Equal, _entity.Id)
                    }
                }
            };

            var filteredEntityCollection = await _connection.Services.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false);

            if (!filteredEntityCollection.HasError)
            {
                List<RecordValue> list = new ();
                var referencingMetadata = _connection.GetMetadataOrThrow(refernecingTable);
                foreach (var entity in filteredEntityCollection.Response.Entities)
                {
                    var row = new DataverseRecordValue(entity, referencingMetadata, recordType, _connection);
                    list.Add(row);
                }

                result = FormulaValue.NewTable(recordType, list);
            }
            else
            {
                result = filteredEntityCollection.DValueError(nameof(IDataverseReader.RetrieveMultipleAsync)).ToFormulaValue();
            }

            return result;
        }

        /// <summary>
        /// Resolves EntityReference to a RecordValue.
        /// </summary>
        /// <param name="reference"></param>
        /// <param name="fieldType"></param>
        /// <param name="columnMap"> Columns to retrieve, if null fetches all columns.</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task<FormulaValue> ResolveEntityReferenceAsync(EntityReference reference, FormulaType fieldType, IEnumerable<string> columns, CancellationToken cancellationToken)
        {
            FormulaValue result;
            DataverseResponse<Entity> newEntity = await _connection.Services.RetrieveAsync(reference.LogicalName, reference.Id, columns, cancellationToken).ConfigureAwait(false);

            if (newEntity.HasError)
            {
                return newEntity.DValueError(nameof(IDataverseReader.RetrieveAsync)).ToFormulaValue();
            }

            var newMetadata = _connection.GetMetadataOrThrow(newEntity.Response.LogicalName);

            if (fieldType is not RecordType)
            {
                // Polymorphic case.
                fieldType = RecordType.Polymorphic();
            }

            result = new DataverseRecordValue(newEntity.Response, newMetadata, (RecordType)fieldType, _connection);
            return result;
        }

        // Called by DataverseRecordValue, which wont internal entity attributes.
        public static async Task<DValue<RecordValue>> UpdateEntityAsync(Guid id, RecordValue record, EntityMetadata metadata, RecordType type, IConnectionValueContext connection, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Update local copy of entity.
            var leanEntity = DataverseRecordValue.ConvertRecordToEntity(id, record, metadata, out DValue<RecordValue> error);

            if (error != null)
            {
                return error;
            }

            cancellationToken.ThrowIfCancellationRequested();
            DataverseResponse result = await connection.Services.UpdateAsync(leanEntity, cancellationToken).ConfigureAwait(false);

            if (result.HasError)
            {
                return result.DValueError(nameof(IDataverseUpdater.UpdateAsync));
            }

            // Once updated, other fields can get changed due to formula columns. Fetch a fresh copy from server.
            DataverseResponse<Entity> newEntity = await connection.Services.RetrieveAsync(leanEntity.LogicalName, leanEntity.Id, columns: null, cancellationToken).ConfigureAwait(false);

            if (newEntity.HasError)
            {
                return newEntity.DValueError(nameof(IDataverseReader.RetrieveAsync));
            }

            var refreshed = new DataverseRecordValue(newEntity.Response, metadata, type, connection);

            return DValue<RecordValue>.Of(refreshed);
        }

        public override async Task<DValue<RecordValue>> UpdateFieldsAsync(RecordValue record, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var refreshedRecord = await DataverseRecordValue.UpdateEntityAsync(_entity.Id, record, Metadata, Type, _connection, cancellationToken);

            if (refreshedRecord.IsValue)
            {
                var dataverseRecord = (DataverseRecordValue)refreshedRecord.Value;
                foreach (var attr in dataverseRecord.Entity.Attributes)
                {
                    _entity.Attributes[attr.Key] = attr.Value;
                }
            }

            return refreshedRecord;
        }

        // Record should already be using logical names.
        public static Entity ConvertRecordToEntity(Guid id, RecordValue record, EntityMetadata metadata, out DValue<RecordValue> error, [CallerMemberName] string caller = null)
        {
            var leanEntity = record.ConvertRecordToEntity(metadata, out error);

            if (error != null)
            {
                return null;
            }

            leanEntity.Id = id;
            return leanEntity;
        }

        public static bool TryGetValue(OptionSetValueType type, object value, out Types.OptionSetValue osValue)
        {
            string logicalName;

            if (value is DName dname)
            {
                logicalName = dname.Value;
            }
            else if (value is XrmOptionSetValue xrmOptionSet)
            {
                logicalName = xrmOptionSet.Value.ToString();
            }
            else if (value is bool b)
            {
                // Support for 2-value option sets
                AttributeUtility.ConvertBoolToBooleanOptionSetOption(b, out logicalName);
            }
            else
            {
                logicalName = value.ToString();
            }

            return type.TryGetValue(logicalName, out osValue);
        }

        public override object ToObject()
        {
            return _entity;
        }

        public override void ToExpression(StringBuilder sb, FormulaValueSerializerSettings settings)
        {
            var tableName = _connection.GetSerializationName(_entity.LogicalName);
            var id = _entity.Id.ToString("D");
            var keyName = GetPrimaryKeyName();

            // Note that function names are case sensitive.
            var expr = $"LookUp({IdentToken.MakeValidIdentifier(tableName)}, {keyName}=GUID(\"{id}\"))";
            sb.Append(expr);
        }

        public override string GetPrimaryKeyName()
        {
            return _metadata.PrimaryIdAttribute;
        }

        public override void ShallowCopyFieldInPlace(string fieldName)
        {
            var clonedEntity = new Entity(Entity.LogicalName, Entity.Id);

            foreach (var attr in Entity.Attributes)
            {
                clonedEntity[attr.Key] = attr.Value;
            }

            _entity = clonedEntity;
        }
    }
}
