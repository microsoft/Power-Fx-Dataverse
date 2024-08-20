// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Linq;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        private RetVal ProcessShowColumns(CallNode node, RetVal tableArg, Context context)
        {
            IntermediateNode filter = tableArg.HasFilter ? tableArg.Filter : null;
            IntermediateNode orderBy = tableArg.HasOrderBy ? tableArg.OrderBy : null;
            IntermediateNode count = tableArg.HasTopCount ? tableArg.TopCountOrDefault : null;

            // ShowColumns is only a column selector, so let's create a map with (column, column) entries
            ColumnMap map = new ColumnMap(node.Args.Skip(1).Select(i => i is TextLiteralNode tln ? tln : throw new InvalidOperationException($"Expecting {nameof(TextLiteralNode)} and received {i.GetType().Name}")));

            map = ColumnMap.Combine(tableArg.ColumnMap, map);

            // change to original node to current node and appends columnSet.
            var resultingTable = new RetVal(_hooks, context, node, tableArg._sourceTableIRNode, tableArg.TableType, filter, orderBy: orderBy, count, _maxRows, map);

            if (node is CallNode maybeGuidCall && maybeGuidCall.Function is DelegatedRetrieveGUIDFunction)
            {
                var guidCallWithColSet = _hooks.MakeRetrieveCall(resultingTable, maybeGuidCall.Args[1]);
                return Ret(guidCallWithColSet);
            }

            return resultingTable;
        }
    }
}
