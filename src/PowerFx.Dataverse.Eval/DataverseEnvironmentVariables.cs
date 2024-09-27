// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OptionSetValue = Microsoft.Xrm.Sdk.OptionSetValue;

namespace Microsoft.PowerFx.Dataverse
{
    public class DataverseEnvironmentVariables : EnvironmentVariables
    {
        private readonly IDataverseServices _client;
        private DataCollection<Entity> _entities;
        private RecordValue _inner;

        public new RecordType Type => BuildType();

        public DataverseEnvironmentVariables(IDataverseServices client) 
            : base(RecordType.Empty())
        {
            _client = client;
        }

        protected RecordType BuildType()
        {
            var queryDefinition = new QueryExpression("environmentvariabledefinition") { ColumnSet = new ColumnSet("displayname", "schemaname", "type") };
            var entityCollectionDefinition = _client.RetrieveMultipleAsync(queryDefinition);
            var recordType = RecordType.Empty();

            if (entityCollectionDefinition.Result.HasError)
            {
                // !!!TODO How to handle error?
            }

            _entities = entityCollectionDefinition.Result.Response.Entities;

            foreach (Entity entity in _entities)
            {
                var displayname = entity.Attributes["displayname"].ToString();
                var schemaname = entity.Attributes["schemaname"].ToString();
                var type = (OptionSetValue)entity.Attributes["type"];
                FormulaType formulaType = null;
                FormulaValue varValue = null;

                switch (type.Value)
                {
                    case 100000000: // string
                        formulaType = FormulaType.String;
                        break;

                    case 100000001: // decimal/number
                        formulaType = FormulaType.Decimal;
                        break;

                    case 100000002: // boolean
                        formulaType = FormulaType.Boolean;
                        break;

                    case 100000003: // JSON
                        formulaType = FormulaType.UntypedObject;
                        break;

                    default:
                        throw new Exception($"Type {type.Value} not supported.");
                }

                recordType = recordType.Add(schemaname, formulaType, displayname);
            }

            return recordType;
        }

        protected override bool TryGetField(FormulaType fieldType, string fieldName, out FormulaValue result)
        {
            if (_inner == null)
            {
                var queryValue = new QueryExpression("environmentvariablevalue") { ColumnSet = new ColumnSet("value", "environmentvariabledefinitionid") };
                var entityCollectionValue = _client.RetrieveMultipleAsync(queryValue);

                var definitionValueDict = new Dictionary<Guid, string>();

                foreach (Entity entity in entityCollectionValue.Result.Response.Entities)
                {
                    definitionValueDict[((EntityReference)entity.Attributes["environmentvariabledefinitionid"]).Id] = entity.Attributes["value"].ToString();
                }

                var fields = new List<NamedValue>();

                foreach (var entity in _entities)
                {
                    var schemaname = entity.Attributes["schemaname"].ToString();
                    var type = (OptionSetValue)entity.Attributes["type"];

                    FormulaValue varValue = null;

                    switch (type.Value)
                    {
                        case 100000000: // string
                            varValue = definitionValueDict.TryGetValue(entity.Id, out var variable) ? FormulaValue.New(variable) : null;
                            break;

                        case 100000001: // decimal/number
                            varValue = definitionValueDict.TryGetValue(entity.Id, out variable) ? FormulaValue.New(Convert.ToDecimal(variable)) : null;
                            break;

                        case 100000002: // boolean
                            varValue = definitionValueDict.TryGetValue(entity.Id, out variable) ? FormulaValue.New(variable.ToLower() == "no" ? false : true) : null;
                            break;

                        case 100000003: // JSON
                            varValue = definitionValueDict.TryGetValue(entity.Id, out variable) ? FormulaValueJSON.FromJson(JsonDocument.Parse(variable.ToString()).RootElement, FormulaType.UntypedObject) : null;
                            break;

                        default:
                            throw new Exception($"Type {type.Value} not supported.");
                    }

                    if (varValue != null)
                    {
                        fields.Add(new NamedValue(schemaname, varValue));
                    }
                }

                _inner = FormulaValue.NewRecordFromFields(Type, fields);
            }

            result = _inner.GetField(fieldName);

            return true;
        }
    }
}
