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
    public class DataverseEnvironmentVariablesRecordType : RecordType
    {
        private readonly IDataverseReader _client;
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
            if (other is DataverseEnvironmentVariablesRecordType)
            {
                return true;
            }

            return false;
        }

        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }
    }

    public class DataverseEnvironmentVariablesRecordValue : RecordValue
    {
        private readonly IDataverseReader _client;

        public DataverseEnvironmentVariablesRecordValue(DataverseEnvironmentVariablesRecordType recordType, IDataverseReader client) 
            : base(recordType)
        {
            _client = client;
        }

        protected override bool TryGetField(FormulaType fieldType, string fieldName, out FormulaValue result)
        {
            var dataverseEnvironmentVariablesRecordType = (DataverseEnvironmentVariablesRecordType)Type;
            dataverseEnvironmentVariablesRecordType.TryGetFieldType(fieldName, out var logical, out var type);

            var definitionId = dataverseEnvironmentVariablesRecordType.GetFieldDefinitionId(fieldName);

            string responseValue = null;

            try
            {
                var environmentVariableValueEntity = _client.RetrieveAsync<EnvironmentVariableValueEntity>(
                    definitionId,
                    CancellationToken.None,
                    nameof(EnvironmentVariableValueEntity.environmentvariabledefinitionid)).Result;

                responseValue = environmentVariableValueEntity.value;
            }
            catch (AggregateException ex)
            {
                if (ex.InnerException is EntityCountException entityCountException && entityCountException.Count == 0)
                {
                    // No overwritten variable value. Get default value.
                    responseValue = dataverseEnvironmentVariablesRecordType.GetDefaultValue(fieldName);
                }
                else
                {
                    throw ex;
                }
            }

            FormulaValue varValue = null;

            if (type is StringType)
            {
                varValue = FormulaValue.New(responseValue);
            }
            else if (type is BooleanType)
            {
                var booleanStringValue = responseValue.ToLower();
                if (booleanStringValue == "no" || booleanStringValue == "yes")
                {
                    booleanStringValue = booleanStringValue == "yes" ? "true" : "false";
                }

                if (bool.TryParse(booleanStringValue, out var boolResult))
                {
                    varValue = FormulaValue.New(boolResult);
                }
                else
                {
                    varValue = BuildErrorValue(responseValue, fieldType);
                }                
            }
            else if (type is DecimalType)
            {
                if (decimal.TryParse(responseValue, out var decimalResult))
                {
                    varValue = FormulaValue.New(Convert.ToDecimal(responseValue));
                }
                else
                {
                    varValue = BuildErrorValue(responseValue, fieldType);
                }
            }
            else if (fieldType is UntypedObjectType)
            {
                byte[] encodedBytes = Encoding.UTF8.GetBytes(responseValue);
                Utf8JsonReader reader = new Utf8JsonReader(encodedBytes);

                if (JsonDocument.TryParseValue(ref reader, out var jsonResult))
                {
                    varValue = FormulaValueJSON.FromJson(jsonResult.RootElement, FormulaType.UntypedObject);
                }
                else
                {
                    varValue = BuildErrorValue(responseValue, fieldType);
                }
            }
            else
            {
                throw new NotImplementedException($"Type {type} not supported.");
            }

            result = varValue;
            return true;
        }

        private ErrorValue BuildErrorValue(string value, FormulaType type)
        {
            return FormulaValue.NewError(DataverseHelpers.GetExpressionError($"Could not convert '{value}' to {type} type."));
        }
    }

#pragma warning disable SA1300 // Element should begin with upper-case letter
    [DebuggerDisplay("Environment variable definition: {uniquename}")]
    [DataverseEntity(TableName)]
    [DataverseEntityPrimaryId(nameof(environmentvariabledefinitionid))]
    public class EnvironmentVariableDefinitionEntity
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
    public class EnvironmentVariableValueEntity
    {
        public const string TableName = "environmentvariablevalue";

        public string value { get; set; }

        public EntityReference environmentvariabledefinitionid { get; set; }
    }
#pragma warning restore SA1300 // Element should begin with upper-case letter

    /// <summary>
    /// Dataverse environment variables type enum.
    /// </summary>
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
