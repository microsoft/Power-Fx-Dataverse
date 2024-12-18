// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression
{
    [Obsolete("preview")]
    public class FxJoinNode
    {
        private string _sourceTable;
        private string _foreignTable;
        private string _fromAttribute;
        private string _toAttribute;
        private string _joinType;
        private string _entityAlias;

        // used to store the list of right columns with their types
        private RecordType _rightColumns;

        // contains the JOIN return type processed by the binder
        private TableType _joinReturnType;

        // constains the interim type containing right column names with entity alias prefix
        private RecordType _joinIntermediateType;

        public LinkEntity LinkEntity => GetLinkEntity();

        public RecordType JoinColumns => _rightColumns;

        public string LinkToEntityName => _foreignTable;

        public string EntityAlias => _entityAlias;

        public FormulaType IntermediateType => _joinIntermediateType;

        public FxJoinNode(string sourceTable, string foreignTable, string fromAttribute, string toAttribute, string joinType, string entityAlias, IEnumerable<string> rightColumnNames, TableType rightTableType)
        {
            _sourceTable = sourceTable;
            _foreignTable = foreignTable;
            _fromAttribute = fromAttribute;
            _toAttribute = toAttribute;
            _joinType = joinType;
            _entityAlias = entityAlias;
            _rightColumns = GetColumnsWithTypes(rightColumnNames, rightTableType);
        }

        internal void ProcessMap(TableType leftTableType, ColumnMap map)
        {
            RecordType recordType = leftTableType.ToRecord();
            RecordType rt = RecordType.Empty();

            foreach (KeyValuePair<string, string> kvp in map.AsStringDictionary())
            {
                string newName = kvp.Key;
                string oldName = kvp.Value;

                if (!recordType.TryGetFieldType(oldName, out FormulaType oldNameType))
                {
                    oldName = GetRealFieldName(oldName);
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

        // identify real field name when used in Join type link entities
        // validate they have the following format: '<anything>_<guid_without_dashes>_J.<fieldName>'
        private static Regex joinRegex = new Regex(@$"_[0-9a-fA-F]{{32}}{DelegationEngineExtensions.LinkEntityJoinSuffix}\.(?<n>.+)$", RegexOptions.Compiled);

        private static string GetRealFieldName(string fieldName)
        {
            Match m = joinRegex.Match(fieldName);
            return m.Success ? m.Groups["n"].Value : fieldName;
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
