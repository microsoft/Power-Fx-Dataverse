// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OptionSetValue = Microsoft.Xrm.Sdk.OptionSetValue;

namespace Microsoft.PowerFx.Dataverse
{
    internal class DataverseEnvironmentVariablesRecordType : RecordType
    {
        private IEnumerable<EnvironmentVariableDefinitionEntity> _definitions;

        public override IEnumerable<string> FieldNames => GetFieldNames();

        private IEnumerable<string> GetFieldNames()
        {
            foreach (var definition in _definitions)
            {
                yield return definition.schemaname;
            }
        }

        public DataverseEnvironmentVariablesRecordType(DisplayNameProvider provider, IEnumerable<EnvironmentVariableDefinitionEntity> definitions)
            : base(provider)
        {
            _definitions = definitions;
        }

        public override bool TryGetFieldType(string name, out FormulaType type)
        {
            if (!_definitions.Any(entity => entity.schemaname == name || entity.displayname == name))
            {
                type = null;
                return false;
            }

            var varType = (EnvironmentVariableType)_definitions.Single(entity => entity.schemaname == name || entity.displayname == name).type.Value;

            switch (varType)
            {
                case EnvironmentVariableType.String:
                    type = FormulaType.String;
                    break;

                case EnvironmentVariableType.Decimal:
                    type = FormulaType.Decimal;
                    break;

                case EnvironmentVariableType.Boolean:
                    type = FormulaType.Boolean;
                    break;

                case EnvironmentVariableType.JSON:
                    type = FormulaType.UntypedObject;
                    break;

                default:
                    throw new NotImplementedException($"Type {varType} not supported.");
            }

            return true;
        }

        internal string GetDefaultValue(string fieldName)
        {
            return _definitions.Single(entity => entity.schemaname == fieldName || entity.displayname == fieldName).defaultvalue;
        }

        public Guid GetFieldDefinitionId(string fieldName)
        {
            return _definitions.Single(entity => entity.schemaname == fieldName || entity.displayname == fieldName).environmentvariabledefinitionid;
        }

        public override bool Equals(object other)
        {
            return ReferenceEquals(this, other);
        }

        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }
    }

    internal class DataverseEnvironmentVariablesRecordValue : RecordValue
    {
        private readonly IDataverseReader _client;

        public DataverseEnvironmentVariablesRecordValue(DataverseEnvironmentVariablesRecordType recordType, IDataverseReader client) 
            : base(recordType)
        {
            _client = client;
        }

        protected override bool TryGetField(FormulaType fieldType, string fieldName, out FormulaValue result)
        {
            var rawFormulaValue = GetRawValueAsync(_client, (DataverseEnvironmentVariablesRecordType)Type, fieldName).Result;

            if (rawFormulaValue is ErrorValue)
            {
                result = rawFormulaValue;
            }
            else
            {
                result = ParseRawValue(((StringValue)rawFormulaValue).Value, fieldType);
            }

            return true;
        }

        public static async Task<FormulaValue> GetRawValueAsync(IDataverseReader reader, DataverseEnvironmentVariablesRecordType type, string fieldName)
        {
            var definitionId = type.GetFieldDefinitionId(fieldName);

            string responseValue = null;

            try
            {
                var filter = new FilterExpression();

                filter.AddCondition(nameof(EnvironmentVariableValueEntity.environmentvariabledefinitionid), ConditionOperator.Equal, definitionId);

                var environmentVariableValueEntity = await reader.RetrieveAsync<EnvironmentVariableValueEntity>(filter, CancellationToken.None).ConfigureAwait(false);

                return FormulaValue.New(environmentVariableValueEntity.value);
            }
            catch (EntityCountException ex)
            {
                if (ex.Count == 0)
                {
                    // No overwritten variable value. Get default value.
                    var defaultValue = type.GetDefaultValue(fieldName);
                    return FormulaValue.New(defaultValue);
                }
                else
                {
                    return BuildErrorValue($"Variable value has {ex.Count} duplicated values.");
                }
            }
        }

        public static FormulaValue ParseRawValue(string rawValue, FormulaType type)
        {
            const string yes = "Yes";
            const string no = "No";

            if (type is StringType)
            {
                return FormulaValue.New(rawValue);
            }
            else if (type is BooleanType)
            {
                if (rawValue == no)
                {
                    return FormulaValue.New(false);
                }
                else if (rawValue == yes)
                {
                    return FormulaValue.New(true);
                }
                else
                {
                    return BuildErrorValue(rawValue, type);
                }
            }
            else if (type is DecimalType)
            {
                if (decimal.TryParse(rawValue, out var decimalResult))
                {
                    return FormulaValue.New(Convert.ToDecimal(rawValue));
                }
                else
                {
                    return BuildErrorValue(rawValue, type);
                }
            }
            else if (type is UntypedObjectType)
            {
                byte[] encodedBytes = Encoding.UTF8.GetBytes(rawValue);
                Utf8JsonReader reader = new Utf8JsonReader(encodedBytes);

                if (JsonDocument.TryParseValue(ref reader, out var jsonResult))
                {
                    return FormulaValueJSON.FromJson(jsonResult.RootElement, FormulaType.UntypedObject);
                }
                else
                {
                    return BuildErrorValue(rawValue, type);
                }
            }
            else
            {
                throw new NotImplementedException($"Type {type} not supported.");
            }
        }

        private static ErrorValue BuildErrorValue(string value, FormulaType type)
        {
            return BuildErrorValue($"Could not convert '{value}' to {type} type.");
        }

        private static ErrorValue BuildErrorValue(string message)
        {
            return FormulaValue.NewError(DataverseHelpers.GetExpressionError(message));
        }
    }

#pragma warning disable SA1300 // Element should begin with upper-case letter
    [DebuggerDisplay("Environment variable definition: {uniquename}")]
    [DataverseEntity(TableName)]
    [DataverseEntityPrimaryId(nameof(environmentvariabledefinitionid))]
    internal class EnvironmentVariableDefinitionEntity
    {
        public const string TableName = "environmentvariabledefinition";

        public string displayname { get; set; }

        public string schemaname { get; set; }

        public string defaultvalue { get; set; }

        public OptionSetValue type { get; set; }

        public Guid environmentvariabledefinitionid { get; set; }
    }

    [DebuggerDisplay("Environment variable value: {uniquename}")]
    [DataverseEntity(TableName)]
    [DataverseEntityPrimaryId(nameof(environmentvariabledefinitionid))]
    internal class EnvironmentVariableValueEntity
    {
        public const string TableName = "environmentvariablevalue";

        public string value { get; set; }

        public EntityReference environmentvariabledefinitionid { get; set; }
    }
#pragma warning restore SA1300 // Element should begin with upper-case letter

    /// <summary>
    /// Dataverse environment variables type enum.
    /// </summary>
    // https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/entities/environmentvariabledefinition#type-choicesoptions
    public enum EnvironmentVariableType
    {
        String = 100000000,

        Decimal = 100000001,

        Boolean = 100000002,

        JSON = 100000003,

        DataSource = 100000004,

        Secret = 100000005,
    }
}
