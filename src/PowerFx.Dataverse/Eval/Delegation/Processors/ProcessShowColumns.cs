// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Linq;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        private RetVal ProcessShowColumns(CallNode node, RetVal tableArg, Context context)
        {
            var columnNames = node.Args.Skip(1).Select(arg =>
            {
                if (arg is TextLiteralNode tln)
                {
                    return tln.LiteralValue;
                }
                else
                {
                    throw new InvalidOperationException($"Expecting {nameof(TextLiteralNode)} and received {arg.GetType().Name}");
                }
            });

            //foreach (var arg in node.Args.Skip(1))
            //{
            //    if (arg is TextLiteralNode tln)
            //    {
            //        map.AddColumn(tln.LiteralValue);
            //    }
            //    else
            //    {
            //        throw new InvalidOperationException($"Expecting {nameof(TextLiteralNode)} and received {arg.GetType().Name}");
            //    }
            //}

            if (tableArg.TryUpdateColumnSelection(columnNames, node, out var resultingTable))
            {
                if (node is CallNode maybeGuidCall && maybeGuidCall.Function is DelegatedRetrieveGUIDFunction)
                {
                    var guidCallWithColSet = _hooks.MakeRetrieveCall(resultingTable, maybeGuidCall.Args[1]);
                    return Ret(guidCallWithColSet);
                }

                return resultingTable;
            }

            return ProcessOtherCall(node, tableArg, context);
        }
    }
}
