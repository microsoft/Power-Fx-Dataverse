// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.Types.Enums;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.DelegatedFunctions;
using Microsoft.PowerFx.Types;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        private RetVal ProcessSort(CallNode node, RetVal tableArg, Context context)
        {           
            bool canDelegate = true;

            List<IntermediateNode> arguments = new List<IntermediateNode>() { tableArg.HasFilter ? tableArg.Filter : node.Args[0] };

            context = context.GetContextForPredicateEval(node, tableArg);

            // If existing First[N], Sort[ByColumns], or ShowColumns we don't delegate
            // When multiple Sort would occur, we cannot reliably group OrderBy commands
            if (tableArg.HasTopCount || tableArg.HasOrderBy || tableArg.HasColumnMap || tableArg.HasGroupBy)
            {
                return NoTransform(node, tableArg);
            }

            int i = 1;

            while (i < node.Args.Count)
            {
                string fieldName;

                if (node.Args[i] is TextLiteralNode tln)
                {
                    fieldName = tln.LiteralValue;
                }
                else if (!TryGetSimpleFieldName(context, ((LazyEvalNode)node.Args[i]).Child, out fieldName))
                {
                    return NoTransform(node, tableArg);
                }

                arguments.Add(new TextLiteralNode(IRContext.NotInSource(FormulaType.String), fieldName));

                i++;
                bool isAscending = true;

                // this argument is optional and will default to Ascending if not provided
                if (i < node.Args.Count)
                {
                    if (TryGetEnumValue(node.Args[i], "SortOrder", out string sortOrder))
                    {
                        isAscending = sortOrder.Equals("Ascending", StringComparison.Ordinal);
                        i++;
                    }
                    else
                    {
                        return NoTransform(node, tableArg);
                    }
                }

                canDelegate &= DelegationUtility.CanDelegateSort(fieldName, isAscending, tableArg.DelegationMetadata?.SortDelegationMetadata);

                arguments.Add(new BooleanLiteralNode(IRContext.NotInSource(FormulaType.Boolean), isAscending));
            }

            if (canDelegate)
            {
                var sortFunc = new DelegatedSort(_hooks);
                IntermediateNode orderByNode = new CallNode(node.IRContext, sortFunc, arguments);

                return tableArg.With(node, orderby: orderByNode); 
            }

            return ProcessOtherCall(node, tableArg, context);
        }

        private RetVal NoTransform(CallNode node, RetVal tableArg)
        {
            IntermediateNode materializeTable = Materialize(tableArg);

            if (!ReferenceEquals(node.Args[0], materializeTable))
            {
                List<IntermediateNode> arguments = new List<IntermediateNode>() { materializeTable };
                arguments.AddRange(node.Args.Skip(1));

                CallNode delegatedSort = new CallNode(node.IRContext, node.Function, node.Scope, arguments);
                return Ret(delegatedSort);
            }

            return Ret(node);
        }

        private bool TryGetEnumValue(IntermediateNode arg, string enumName, out string val)
        {
            if (arg is RecordFieldAccessNode rfan && rfan.From is ResolvedObjectNode ron && ron.Value is EnumSymbol es && es.EntityName.Value == enumName)
            {
                val = rfan.Field.Value;
                return true;
            }

            val = null;
            return false;
        }
    }
}
