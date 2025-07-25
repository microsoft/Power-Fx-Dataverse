﻿// Copyright (c) Microsoft Corporation.
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
            if (TryGetSimpleFieldName(context, node.Args[1], out var fieldName))
            {
                var forAllColumns = new List<(FxColumnInfo, string)>();
                forAllColumns.Add((fieldName, DVSymbolTable.SingleColumnTableFieldName));
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
                var forAllColumns = new List<(FxColumnInfo, string)>();
                foreach (KeyValuePair<DName, IntermediateNode> kvp in recordNode.Fields)
                {
                    string newFieldName = kvp.Key.Value;

                    if (TryGetSimpleFieldName(context, kvp.Value, out var currentFieldName))
                    {
                        forAllColumns.Add((currentFieldName, newFieldName));
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
                    if (tableArg.TryAddColumnRenames(forAllColumns, node, out var result))
                    {
                        return result;
                    }
                }
            }

            return ProcessOtherCall(node, tableArg, context);
        }
    }
}
