// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.Functions.Delegation.DelegationMetadata;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
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

        // $$$ this should be enum.
        private readonly string _joinType;
        private readonly string _foreignTableAlias;
        private readonly FxColumnMap _rightMap;
        private readonly string _expand;
        
        internal FxColumnMap RightTablColumnMap => _rightMap;

        public LinkEntity LinkEntity => GetLinkEntity();

        public string LinkToEntityName => _foreignTable;

        public string ForeignTableAlias => _foreignTableAlias;

        public string Expand => _expand;

        internal IEnumerable<string> RightRealFieldNames => _rightMap.ColumnInfoMap.Values.Select(c => c.RealColumnName);

        internal RecordType JoinTableRecordType => _rightMap.SourceTableRecordType;

        internal IDelegationMetadata RightTableDelegationMetadata => JoinTableRecordType._type.AssociatedDataSources.FirstOrDefault()?.DelegationMetadata;

        public FxJoinNode(string sourceTable, string foreignTable, string fromAttribute, string toAttribute, string joinType, string foreignTableAlias, FxColumnMap rightMap, string expand)
        {
            _sourceTable = sourceTable ?? throw new ArgumentNullException(nameof(sourceTable));
            _foreignTable = foreignTable ?? throw new ArgumentNullException(nameof(foreignTable));
            _fromAttribute = fromAttribute ?? throw new ArgumentNullException(nameof(fromAttribute));
            _toAttribute = toAttribute ?? throw new ArgumentNullException(nameof(toAttribute));
            _joinType = joinType ?? throw new ArgumentNullException(nameof(joinType));
            _rightMap = rightMap ?? throw new ArgumentNullException(nameof(rightMap));
            _foreignTableAlias = foreignTableAlias ?? throw new ArgumentNullException(nameof(foreignTableAlias));
            _expand = expand;
        }

        public FxJoinNode With(FxColumnMap rightMap)
        {
            return new FxJoinNode(_sourceTable, _foreignTable, _fromAttribute, _toAttribute, _joinType, _foreignTableAlias, rightMap, _expand);
        }

        public FxJoinNode WithEmptyColumnMap()
        {
            return new FxJoinNode(_sourceTable, _foreignTable, _fromAttribute, _toAttribute, _joinType, _foreignTableAlias, new FxColumnMap(_rightMap.SourceTableRecordType), _expand);
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

            ColumnSet columnSet;
            if (_rightMap.IsEmpty)
            {
                // if column is empty, we are joining in expression but ShowColumns/similar eliminated the right columns.
                columnSet = new ColumnSet(_toAttribute);
            }
            else
            {
                columnSet = _rightMap.ToXRMColumnSet();
            }

            linkEntity.Columns = columnSet;

            return linkEntity;
        }

        // Only used for debugging
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
            sb.Append('>');
            
            if (!string.IsNullOrEmpty(_expand))
            {
                sb.Append(" $expand=");
                sb.Append(_expand);
            }

            sb.Append('}');

            return sb.ToString();
        }
    }
}
