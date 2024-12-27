// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.Functions.Delegation.DelegationMetadata;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.Utils;
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
                TextLiteralNode column = new TextLiteralNode(IRContext.NotInSource(FormulaType.String), fieldName);

                // Create a map with ("Value", fieldName)
                ColumnMap map = new ColumnMap(new Dictionary<DName, TextLiteralNode>() { { new DName("Value"), column } });

                // Combine with an existing map
                map = ColumnMap.Combine(tableArg.ColumnMap, map, tableArg.TableType);

                return tableArg.With(node, map: map);
            }

            // check if we have a record of (newName: oldName)
            if (((LazyEvalNode)node.Args[1]).Child is RecordNode recordNode)
            {
                Dictionary<DName, TextLiteralNode> dic = new Dictionary<DName, TextLiteralNode>();
                bool canDelegate = true;

                foreach (KeyValuePair<DName, IntermediateNode> kvp in recordNode.Fields)
                {
                    string newFieldName = kvp.Key.Value;

                    if (TryGetSimpleFieldName(context, kvp.Value, out string currentFieldName))
                    {
                        TextLiteralNode currentColumn = new TextLiteralNode(IRContext.NotInSource(FormulaType.String), currentFieldName);
                        dic.Add(new DName(newFieldName), currentColumn);
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
                    // Combine with an existing map
                    ColumnMap map = ColumnMap.Combine(tableArg.ColumnMap, new ColumnMap(dic), tableArg.TableType);
                    
                    return tableArg.With(node, map: map);
                }
            }

            return ProcessOtherCall(node, tableArg, context);
        }
    }
}
