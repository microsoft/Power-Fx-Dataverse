// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Types;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;
using RecordNode = Microsoft.PowerFx.Core.IR.Nodes.RecordNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        private RetVal ProcessForAll(CallNode node, RetVal tableArg, Context context)
        {
            context = context.GetContextForPredicateEval(node, tableArg);

            // check if we have a simple field name here
            if (TryGetSimpleFieldName(context, ((LazyEvalNode)node.Args[1]).Child, out string fieldName))
            {
                // $$$ update this from Core.
                var singleColumnFieldName = "Value";
                var forAllColumns = new List<(string, string)>();
                forAllColumns.Add((fieldName, singleColumnFieldName));
                if (tableArg.TryAddColumnRenames(forAllColumns, node, out var result))
                {
                    return result;
                }
            }

            // check if we have a record of (newName: oldName)
            else if (((LazyEvalNode)node.Args[1]).Child is RecordNode recordNode)
            {
                Dictionary<DName, TextLiteralNode> dic = new Dictionary<DName, TextLiteralNode>();
                bool canDelegate = true;
                var forAllColumns = new List<FxColumnInfo>();
                foreach (KeyValuePair<DName, IntermediateNode> kvp in recordNode.Fields)
                {
                    string newFieldName = kvp.Key.Value;

                    if (TryGetSimpleFieldName(context, kvp.Value, out string currentFieldName))
                    {
                        map.UpdateAlias(currentFieldName, newFieldName);
                    }
                    else
                    {
                        // If any record field is not a trivial field name, we'll not delegate
                        // ex: { newName: oldName * 2 }
                        canDelegate = false;
                        break;
                    }
                }

                if (canDelegate)
                {
                    if (tableArg.TryAddColumnMap(map, node, out var result))
                    {
                        return result;
                    }
                }
            }

            return ProcessOtherCall(node, tableArg, context);
        }
    }
}
