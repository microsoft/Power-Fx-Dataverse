// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Dataverse;
using Microsoft.PowerFx.Dataverse.Eval;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression
{
    [Obsolete("preview")]
    public class FxColumnInfo
    {
        private readonly string _realColumnName;

        public string RealColumnName => _realColumnName;

        private readonly string _aliasColumnName;

        public string AliasColumnName => _aliasColumnName;

        private readonly bool _isDistinct;

        public bool IsDistinct => _isDistinct;

        private readonly SummarizeMethod _aggregateOperation;

        public SummarizeMethod AggregateMethod => _aggregateOperation;

        private readonly bool _isGroupByProperty;

        public bool IsGroupByProperty => _isGroupByProperty;

        /// <summary>
        /// Initializes a new instance of the <see cref="FxColumnInfo"/> class.
        /// </summary>
        /// <param name="realColumnName">Logical name of column present in Data Source.</param>
        /// <param name="aliasColumnName">Alias for the column name, if it is same as <paramref name="realColumnName"/> it will be set to null.</param>
        internal FxColumnInfo(string realColumnName, string aliasColumnName = null, bool isDistinct = false, bool isGroupByProperty = false, SummarizeMethod aggregateOperation = SummarizeMethod.None)
        {
            if (realColumnName == null && aggregateOperation != SummarizeMethod.CountRows)
            {
                throw new ArgumentNullException($"{nameof(realColumnName)} cannot be null.");
            }

            _realColumnName = realColumnName;
            _aliasColumnName = realColumnName == aliasColumnName ? null : aliasColumnName;
            _isDistinct = isDistinct;
            _aggregateOperation = aggregateOperation;
            _isGroupByProperty = isGroupByProperty;
        }

        public FxColumnInfo CloneAndUpdateAlias(string aliasColumnName)
        {
            return new FxColumnInfo(_realColumnName, aliasColumnName, _isDistinct);
        }

        internal FxColumnInfo CloneAndUpdateAggregation(SummarizeMethod aggregation)
        {
            return new FxColumnInfo(_realColumnName, _aliasColumnName, _isDistinct, _isGroupByProperty, aggregation);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            if (IsDistinct)
            {
                sb.Append("DISTINCT(");
            }

            if (_aggregateOperation != SummarizeMethod.None)
            {
                sb.Append(_aggregateOperation.ToString().ToUpper());
                sb.Append("(");
            }

            sb.Append(RealColumnName);

            if (_aggregateOperation != SummarizeMethod.None)
            {
                sb.Append(")");
            }

            if (AliasColumnName != null)
            {
                sb.Append(" As ");
                sb.Append(AliasColumnName);
            }

            if (IsDistinct)
            {
                sb.Append(")");
            }

            return sb.ToString();
        }
    }
}
