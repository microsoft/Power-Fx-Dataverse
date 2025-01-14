// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Dataverse;
using Microsoft.PowerFx.Dataverse.Eval;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression
{
    [Obsolete("preview")]
    internal class FxColumnInfo
    {
        private readonly string _realColumnName;

        public string RealColumnName => _realColumnName;

        private readonly string _aliasColumnName;

        public string AliasColumnName => _aliasColumnName;

        private readonly bool _isDistinct;

        public bool IsDistinct => _isDistinct;

        /// <summary>
        /// Initializes a new instance of the <see cref="FxColumnInfo"/> class.
        /// </summary>
        /// <param name="realColumnName">Logical name of column present in Data Source.</param>
        /// <param name="aliasColumnName">Alias for the column name, if it is same as <paramref name="realColumnName"/> it will be set to null.</param>
        public FxColumnInfo(string realColumnName, string aliasColumnName = null, bool isDistinct = false)
        {
            _realColumnName = realColumnName ?? throw new ArgumentNullException("realColumnName cannot be null");
            _aliasColumnName = realColumnName == aliasColumnName ? null : aliasColumnName;
            _isDistinct = isDistinct;
        }

        public FxColumnInfo CloneAndUpdateAlias(string aliasColumnName)
        {
            return new FxColumnInfo(_realColumnName, aliasColumnName, _isDistinct);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            if (IsDistinct)
            {
                sb.Append("DISTINCT(");
            }

            sb.Append(RealColumnName);

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
