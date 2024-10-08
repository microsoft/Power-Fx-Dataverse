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

        public Guid GetFieldDefinitionId(string fieldName)
        {
            return _definitions.Single(entity => entity.schemaname == fieldName || entity.displayname == fieldName).environmentvariabledefinitionid;
        }

        public override bool Equals(object other)
        {
            if (other == null)
            {
                return false;
            }

            if (other is DataverseEnvironmentVariablesRecordType)
            {
                return true;
            }

            throw new InvalidOperationException();
        }

        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }
    }

    public class DataverseEnvironmentVariablesRecordValue : RecordValue
    {
        private readonly IDataverseReader _client;

        public DataverseEnvironmentVariablesRecordValue(RecordType recordType, IDataverseReader client) 
            : base(recordType)
        {
            _client = client;
        }

        protected override bool TryGetField(FormulaType fieldType, string fieldName, out FormulaValue result)
        {
            var dataverseEnvironmentVariablesRecordType = (DataverseEnvironmentVariablesRecordType)Type;
            dataverseEnvironmentVariablesRecordType.TryGetFieldType(fieldName, out var logical, out var type);

            var definitionId = dataverseEnvironmentVariablesRecordType.GetFieldDefinitionId(fieldName);
            var environmentVariableValueEntity = _client.RetrieveAsync<EnvironmentVariableValueEntity>(
                definitionId, 
                CancellationToken.None, 
                "environmentvariabledefinitionid").Result;

            FormulaValue varValue = null;

            if (type is StringType)
            {
                varValue = FormulaValue.New(environmentVariableValueEntity.value);
            }
            else if (type is BooleanType)
            {
                varValue = FormulaValue.New(environmentVariableValueEntity.value.ToLower() == "no" ? false : true);
            }
            else if (type is DecimalType)
            {
                varValue = FormulaValue.New(Convert.ToDecimal(environmentVariableValueEntity.value));
            }
            else if (fieldType is UntypedObjectType)
            {
                varValue = FormulaValueJSON.FromJson(JsonDocument.Parse(environmentVariableValueEntity.value.ToString()).RootElement, FormulaType.UntypedObject);
            }
            else
            {
                throw new NotImplementedException($"Type {type} not supported.");
            }

            result = varValue;
            return true;
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

    public class EnvironmentVariablesDisplayNameProvider : DisplayNameProvider
    {
        private readonly IEnumerable<KeyValuePair<DName, DName>> _logicalToDisplayPairs;

        public EnvironmentVariablesDisplayNameProvider(IEnumerable<KeyValuePair<DName, DName>> logicalToDisplayPairs)
        {
            _logicalToDisplayPairs = logicalToDisplayPairs;
        }

        public override IEnumerable<KeyValuePair<DName, DName>> LogicalToDisplayPairs => _logicalToDisplayPairs;

        public override bool TryGetDisplayName(DName logicalName, out DName displayName)
        {
            if (!_logicalToDisplayPairs.Any(kpv => kpv.Key.Value == logicalName))
            {
                displayName = default;
                return false;
            }

            displayName = _logicalToDisplayPairs.First(kpv => kpv.Key.Value == logicalName).Value;
            return true;
        }

        public override bool TryGetLogicalName(DName displayName, out DName logicalName)
        {
            if (!_logicalToDisplayPairs.Any(kpv => kpv.Value.Value == displayName))
            {
                displayName = default;
                return false;
            }

            logicalName = _logicalToDisplayPairs.First(kpv => kpv.Value.Value == displayName).Key;
            return true;
        }
    }
}
