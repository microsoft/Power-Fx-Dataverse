//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FxOptionSetValue = Microsoft.PowerFx.Types.OptionSetValue;
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
        private EntityMetadata _metadata;

        // Used to resolve entity relationships (dot operators). 
        private readonly IDataverseServices _dataverseServices;

        internal DataverseRecordValue(Entity entity, EntityMetadata metadata, RecordType recordType, IDataverseServices services)
            : base(recordType)
        {
            _entity = entity ?? throw new ArgumentNullException(nameof(entity));
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _dataverseServices = services;
        }

        internal Entity Entity => _entity;
        internal EntityMetadata Metadata => _metadata;

        protected override bool TryGetField(FormulaType fieldType, string fieldName, out FormulaValue result)
        {
            // IR should convert the fieldName from display to Logical Name. 
            if (!_entity.Attributes.TryGetValue(fieldName, out var value))
            {
                result = null;
                return false;
            }

            if (value is EntityReference reference)
            {
                DataverseResponse<Entity> newEntity = _dataverseServices.RetrieveAsync(reference.LogicalName, reference.Id).ConfigureAwait(false).GetAwaiter().GetResult();

                if (newEntity.HasError)
                {
                    result = null;
                    return false;
                }

                result = new DataverseRecordValue(newEntity.Response, _metadata, (RecordType)fieldType, _dataverseServices);
                return true;
            }

            if (value is DateTime dt && dt.Kind == DateTimeKind.Utc)
            {
                // $$$ Get correct timezone from symbols
                value = TimeZoneInfo.ConvertTimeFromUtc(dt, TimeZoneInfo.Local);
            }

            // Handle primitives
            if (PrimitiveValueConversions.TryMarshal(value, fieldType, out result))
            {
                return true;
            }

            // Options, enums, etc?
            if (fieldType is OptionSetValueType opt)
            {
                if (TryGetValue(opt, value, out var osResult))
                {
                    result = osResult;
                    return true;
                }
            }

            // Unhandled case... 
            result = null;
            return false;
        }

        public override async Task<DValue<RecordValue>> UpdateFieldsAsync(RecordValue record, CancellationToken cancellationToken)
        {
            UpdateEntityWithRecord(record, out var error);

            if (error != null)
            {
                return error;
            }

            DataverseResponse<Entity> result = await _dataverseServices.UpdateAsync(_entity, cancellationToken);

            if (result.HasError)
            {
                return result.DValueError(nameof(IDataverseUpdater.UpdateAsync));
            }

            _entity = result.Response;
            return DValue<RecordValue>.Of(this);
        }

        // Record should already be logical names. 
        private void UpdateEntityWithRecord(RecordValue record, out DValue<RecordValue> error, [CallerMemberName] string methodName = null)
        {
            error = null;

            foreach (var field in record.Fields)
            {
                AttributeMetadata amd = _metadata.Attributes.FirstOrDefault(amd => amd.LogicalName == field.Name);
                try
                {
                    object fieldValue = AttributeUtility.ToAttributeObject(amd, field.Value);

                    string fieldName = field.Name;
                    _entity.Attributes[fieldName] = fieldValue;
                }
                catch (NotImplementedException)
                {
                    error = DataverseExtensions.DataverseError<RecordValue>($"Key {field.Name} with type {_entity.Attributes[field.Name].GetType().Name}/{field.Value.Type} is not supported yet.", methodName);
                }
            }
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
    }
}
