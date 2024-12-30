// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression
{
    [Obsolete("preview")]
    public class FxJoinNode
    {
        private readonly string _sourceTable;
        private readonly string _foreignTable;
        private readonly string _fromAttribute;
        private readonly string _toAttribute;
        private readonly string _joinType;
        private readonly string _entityAlias;

        // used to store the list of right columns with their types
        private RecordType _rightColumns;

        // constains the interim type containing right column names with entity alias prefix
        private RecordType _joinIntermediateType;

        public LinkEntity LinkEntity => GetLinkEntity();

        public RecordType JoinColumns => _rightColumns;

        public string LinkToEntityName => _foreignTable;

        public string EntityAlias => _entityAlias;

        // When a JOIN operation occurs, there will potentially be column conflicts and to avoid them we'll use the LinkEntity.EntityAlias for that
        // This RecordType is the one containing the right columns as they come from the datasource, with the alias prefix
        // It is computed in calling ProcessMap method below (all left columns + the right columns from the map)
        public RecordType IntermediateType => _joinIntermediateType;

        public FxJoinNode(string sourceTable, TableType rightTableType, string fromAttribute, string toAttribute, string joinType, string entityAlias, IEnumerable<string> rightColumnNames)
        {
            _sourceTable = sourceTable;
            _foreignTable = rightTableType.TableSymbolName;
            _fromAttribute = fromAttribute;
            _toAttribute = toAttribute;
            _joinType = joinType;
            _entityAlias = entityAlias;
            _rightColumns = GetColumnsWithTypes(rightColumnNames, rightTableType);
        }

        public void ProcessMap(TableType leftTableType, ColumnMap map)
        {
            RecordType recordType = leftTableType.ToRecord();
            RecordType rt = RecordType.Empty();

            foreach (KeyValuePair<string, string> kvp in map.AsStringDictionary())
            {
                string newName = kvp.Key;
                string oldName = kvp.Value;

                if (!recordType.TryGetFieldType(oldName, out FormulaType oldNameType) && DelegationUtility.TryGetFieldName(oldName, out _, out string realOldName))
                {
                    oldName = realOldName;
                    oldNameType = _rightColumns.GetFieldType(oldName);
                }

                string fieldName = null;

                if (recordType.TryGetFieldType(oldName, out _))
                {
                    fieldName = oldName;
                }
                else
                {
                    fieldName = $"{_entityAlias}.{oldName}";
                }

                rt = rt.Add(fieldName, oldNameType);
            }

            _joinIntermediateType = rt;
        }

        private static RecordType GetColumnsWithTypes(IEnumerable<string> columnNames, TableType tableType)
        {
            RecordType recordType = RecordType.Empty();

            foreach (string name in columnNames)
            {
                if (tableType.TryGetFieldType(name, out FormulaType fType))
                {
                    recordType = recordType.Add(name, fType);
                }
            }

            return recordType;
        }

        public static JoinOperator ToJoinOperator(string joinType)
        {
            return joinType switch
            {
                "Inner" => JoinOperator.Inner,
                "Left" => JoinOperator.LeftOuter,
                "Right" => JoinOperator.In,
                "Full" => JoinOperator.All,
                _ => throw new InvalidOperationException($"Unknown JoinType {joinType}")
            };
        }

        public LinkEntity GetLinkEntity()
        {
            JoinOperator joinOperator = ToJoinOperator(_joinType);

            // Join between source & foreign table, using equality comparison between 'from' & 'to' attributes, with specified JOIN operator
            // EntityAlias is used in OData $apply=join(foreignTable as <entityAlias>) and DV Entity attribute names will be prefixed with this alias
            // hence the need to rename columns with a columnMap afterwards
            LinkEntity linkEntity = new LinkEntity(_sourceTable, _foreignTable, _fromAttribute, _toAttribute, joinOperator);
            linkEntity.EntityAlias = _entityAlias;
            linkEntity.Columns = new ColumnSet(_rightColumns.FieldNames.ToArray());

            return linkEntity;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append('{');
            sb.Append(_sourceTable);
            sb.Append('.');
            sb.Append(_fromAttribute);
            sb.Append('=');
            sb.Append(_foreignTable);
            sb.Append('.');
            sb.Append(_toAttribute);
            sb.Append(',');
            sb.Append(_joinType);
            sb.Append(" [");
            sb.Append(_entityAlias);
            sb.Append("] ");
            sb.Append(_rightColumns._type.ToString());
            sb.Append('}');

            return sb.ToString();
        }
    }
}
