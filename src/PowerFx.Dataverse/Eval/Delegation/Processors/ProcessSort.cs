// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.Types.Enums;
using Microsoft.PowerFx.Core.Utils;
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
            IList<(string, bool)> sortColumns = new List<(string, bool)>();
            bool canDelegate = true;
            context = context.GetContextForPredicateEval(node, tableArg);
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
                    return ProcessOtherCall(node, tableArg, context);
                }

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
                        return ProcessOtherCall(node, tableArg, context);
                    }
                }

                sortColumns.Add((fieldName, isAscending));
            }

            if (tableArg.TryAddOrderBy(sortColumns, node, out var result))
            {
                return result;
            }

            return ProcessOtherCall(node, tableArg, context);
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
