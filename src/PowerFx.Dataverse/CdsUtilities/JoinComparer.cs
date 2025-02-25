// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse.CdsUtilities
{
    internal class JoinComparer : IEqualityComparer<FxJoinNode>
    {
        public bool Equals(FxJoinNode x, FxJoinNode y)
        {
            if (x == null || y == null)
            {
                return false;
            }

            return x.SourceTable == y.SourceTable
                && x.FromAttribute == y.FromAttribute
                && x.ForeignTable == y.ForeignTable
                && x.ToAttribute == y.ToAttribute
                && x.JoinType == y.JoinType;
        }

        public int GetHashCode(FxJoinNode obj)
        {
            return Tuple.Create(obj.SourceTable, obj.FromAttribute, obj.ForeignTable, obj.ToAttribute, obj.JoinType).GetHashCode();
        }
    }
}
