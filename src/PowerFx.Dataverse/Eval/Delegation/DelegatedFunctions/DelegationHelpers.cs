// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation
{
    internal static class DelegationHelpers
    {
        public static JoinOperator ToJoinOperator(this string joinType)
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

        public static LinkEntity GetLinkEntity(this RecordValue recordValue, out IEnumerable<NamedValue> joinColumns)
        {
            string sourceTable = ((StringValue)recordValue.GetField("sourceTable")).Value;
            string foreignTable = ((StringValue)recordValue.GetField("foreignTable")).Value;
            string fromAttribute = ((StringValue)recordValue.GetField("fromAttribute")).Value;
            string toAttribute = ((StringValue)recordValue.GetField("toAttribute")).Value;
            string joinType = ((StringValue)recordValue.GetField("joinType")).Value;
            string entityAlias = ((StringValue)recordValue.GetField("entityAlias")).Value;

            joinColumns = ((RecordValue)recordValue.GetField("foreignTableColumns")).Fields;

            JoinOperator joinOperator = joinType.ToJoinOperator();

            // Join between source & foreign table, using equality comparison between 'from' & 'to' attributes, with specified JOIN operator
            // EntityAlias is used in OData $apply=join(foreignTable as <entityAlias>) and DV Entity attribute names will be prefixed with this alias
            // hence the need to rename columns with a columnMap afterwards
            LinkEntity linkEntity = new LinkEntity(sourceTable, foreignTable, fromAttribute, toAttribute, joinOperator);
            linkEntity.EntityAlias = entityAlias;
            linkEntity.Columns = new ColumnSet(joinColumns.Select(nv => nv.Name).ToArray());

            return linkEntity;
        }
    }
}
