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
        private readonly IEnumerable<EnvironmentVariableDefinitionEntity> _definitions;

        public override IEnumerable<string> FieldNames => GetFieldNames();

        private IEnumerable<string> GetFieldNames()
        {
            foreach (var definition in _definitions)
            {
                yield return definition.schemaname;
            }
        }

        public DataverseEnvironmentVariablesRecordType(IDataverseReader client)
            : base()
        {
            _client = client;

            var filter = new FilterExpression();

            // Data source and Secret types are not supported.
            filter.FilterOperator = LogicalOperator.Or;
            filter.AddCondition("type", ConditionOperator.Equal, EnvironmentVariableType.Decimal);
            filter.AddCondition("type", ConditionOperator.Equal, EnvironmentVariableType.String);
            filter.AddCondition("type", ConditionOperator.Equal, EnvironmentVariableType.JSON);
            filter.AddCondition("type", ConditionOperator.Equal, EnvironmentVariableType.Boolean);

            _definitions = _client.RetrieveMultipleAsync<EnvironmentVariableDefinitionEntity>(filter, CancellationToken.None).Result;
        }

        public override bool TryGetFieldType(string displayOrLogicalName, out string logical, out FormulaType type)
        {
            if (!TryGetFieldDefinition(displayOrLogicalName, out type, out var environmentVariableDefinitionEntity))
            {
                logical = null;
                return false;
            }

            logical = environmentVariableDefinitionEntity.schemaname;

            return true;
        }

        public override bool TryGetFieldType(string name, out FormulaType type)
        {
            return TryGetFieldDefinition(name, out type, out _);
        }

        /// <summary>
        /// Get variable field definition ie type, definition id.
        /// </summary>
        /// <param name="name">Variable logical/display name.</param>
        /// <param name="type">Variable type.</param>
        /// <param name="environmentVariableDefinitionEntity">Variable definition entity.</param>
        /// <returns>True if found. False otherwise.</returns>
        /// <exception cref="Exception">Variable not found.</exception>
        internal bool TryGetFieldDefinition(string name, out FormulaType type, out EnvironmentVariableDefinitionEntity environmentVariableDefinitionEntity)
        {
            environmentVariableDefinitionEntity = _definitions.Single(entity => entity.schemaname == name || entity.displayname == name);

            if (environmentVariableDefinitionEntity == null)
            {
                type = null;
                return false;
            }

            var variableType = (EnvironmentVariableType)environmentVariableDefinitionEntity.type.Value;

            switch (variableType)
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
                    throw new Exception($"Type {variableType} not supported.");
            }

            return true;
        }

        public override bool Equals(object other)
        {
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

        public DataverseEnvironmentVariablesRecordValue(IDataverseReader client) 
            : base(new DataverseEnvironmentVariablesRecordType(client))
        {
            _client = client;
        }

        protected override bool TryGetField(FormulaType fieldType, string fieldName, out FormulaValue result)
        {
            var valid = ((DataverseEnvironmentVariablesRecordType)Type).TryGetFieldDefinition(fieldName, out _, out var environmentVariableDefinitionEntity);

            if (!valid)
            {
                result = default;
                return false;
            }

            var environmentVariableValueEntity = _client.RetrieveAsync<EnvironmentVariableValueEntity>(
                environmentVariableDefinitionEntity.environmentvariabledefinitionid, 
                CancellationToken.None, 
                "environmentvariabledefinitionid").Result;

            var type = (EnvironmentVariableType)environmentVariableDefinitionEntity.type.Value;

            FormulaValue varValue = null;

            switch (type)
            {
                case EnvironmentVariableType.String:
                    varValue = FormulaValue.New(environmentVariableValueEntity.value);
                    break;

                case EnvironmentVariableType.Decimal:
                    varValue = FormulaValue.New(Convert.ToDecimal(environmentVariableValueEntity.value));
                    break;

                case EnvironmentVariableType.Boolean:
                    varValue = FormulaValue.New(environmentVariableValueEntity.value.ToLower() == "no" ? false : true);
                    break;

                case EnvironmentVariableType.JSON:
                    varValue = FormulaValueJSON.FromJson(JsonDocument.Parse(environmentVariableValueEntity.value.ToString()).RootElement, FormulaType.UntypedObject);
                    break;

                default:
                    throw new Exception($"Type {type} not supported.");
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
