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
        private readonly string _foreignTableAlias;
        private readonly ColumnMap _rightMap;

        public LinkEntity LinkEntity => GetLinkEntity();

        public string LinkToEntityName => _foreignTable;

        public string ForeignTableAlias => _foreignTableAlias;

        internal IEnumerable<string> RightFields => _rightMap.AsStringDictionary().Values;

        public FxJoinNode(string sourceTable, string foreignTable, string fromAttribute, string toAttribute, string joinType, string foreignTableAlias, ColumnMap rightMap)
        {
            _sourceTable = sourceTable;
            _foreignTable = foreignTable;
            _fromAttribute = fromAttribute;
            _toAttribute = toAttribute;
            _joinType = joinType;            
            _rightMap = rightMap;
            _foreignTableAlias = foreignTableAlias;            
        }

        public static JoinOperator ToJoinOperator(string joinType)
        {
            return joinType switch
            {
                "Inner" => JoinOperator.Inner,
                "Left" => JoinOperator.LeftOuter,

                // $$$ There is no Right join in DV operator, this will have to be replaced later
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
            linkEntity.EntityAlias = _foreignTableAlias;

            ColumnSet columnSet = new ColumnSet();

            foreach (KeyValuePair<string, string> column in _rightMap.AsStringDictionary())
            {
                columnSet.AttributeExpressions.Add(new XrmAttributeExpression(column.Value) { Alias = column.Key });
            }

            linkEntity.Columns = columnSet;

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
            sb.Append(_foreignTableAlias);
            sb.Append("] <");
            sb.Append(_rightMap.ToString());
            sb.Append(">}");

            return sb.ToString();
        }
    }
}
