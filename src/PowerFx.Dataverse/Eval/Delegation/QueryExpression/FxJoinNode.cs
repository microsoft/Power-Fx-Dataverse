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

        public string SourceTable => _sourceTable;

        private readonly string _foreignTable;

        public string ForeignTable => _foreignTable;

        private readonly string _fromAttribute;

        public string FromAttribute => _fromAttribute;

        private readonly string _toAttribute;

        public string ToAttribute => _toAttribute;

        private readonly FxJoinType _joinType;

        public FxJoinType JoinType => _joinType;

        private readonly string _foreignTableAlias;

        public string ForeignTableAlias => _foreignTableAlias;

        private readonly FxColumnMap _rightMap;

        public FxColumnMap RightTablColumnMap => _rightMap;

        internal IEnumerable<string> RightRealFieldNames => _rightMap.ColumnInfoMap.Values.Select(c => c.RealColumnName);

        internal RecordType JoinTableRecordType => _rightMap.SourceTableRecordType;

        internal IDelegationMetadata RightTableDelegationMetadata => JoinTableRecordType._type.AssociatedDataSources.FirstOrDefault()?.DelegationMetadata;

        public FxJoinNode(string sourceTable, string foreignTable, string fromAttribute, string toAttribute, FxJoinType joinType, string foreignTableAlias, FxColumnMap rightMap)
        {
            _sourceTable = sourceTable ?? throw new ArgumentNullException(nameof(sourceTable));
            _foreignTable = foreignTable ?? throw new ArgumentNullException(nameof(foreignTable));
            _fromAttribute = fromAttribute ?? throw new ArgumentNullException(nameof(fromAttribute));
            _toAttribute = toAttribute ?? throw new ArgumentNullException(nameof(toAttribute));
            _joinType = joinType;
            _rightMap = rightMap ?? throw new ArgumentNullException(nameof(rightMap));
            _foreignTableAlias = foreignTableAlias ?? throw new ArgumentNullException(nameof(foreignTableAlias));
        }

        public FxJoinNode With(FxColumnMap rightMap)
        {
            return new FxJoinNode(_sourceTable, _foreignTable, _fromAttribute, _toAttribute, _joinType, _foreignTableAlias, rightMap);
        }

        public FxJoinNode WithEmptyColumnMap()
        {
            return new FxJoinNode(_sourceTable, _foreignTable, _fromAttribute, _toAttribute, _joinType, _foreignTableAlias, new FxColumnMap(_rightMap.SourceTableRecordType));
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

    [Obsolete("preview")]
    public enum FxJoinType
    {
        Inner,
        Left,
        Right,
        Full
    }
}
