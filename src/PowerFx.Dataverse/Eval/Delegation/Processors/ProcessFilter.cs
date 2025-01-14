// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        private RetVal ProcessFilter(CallNode node, RetVal tableArg, Context context)
        {
            // Filter with group by is not supported. Ie Filter(Summarize(...), ...), other way around is supported.
            if (node.Args.Count != 2)
            {
                return CreateNotSupportedErrorAndReturn(node, tableArg);
            }

            if (tableArg.HasGroupBy || tableArg.HasJoin)
            {
                return ProcessOtherCall(node, tableArg, context);
            }

            IntermediateNode predicate = node.Args[1];
            var predicteContext = context.GetContextForPredicateEval(node, tableArg);
            var pr = predicate.Accept(this, predicteContext);

            if (!pr.IsDelegating)
            {
                // Though entire predicate is not delegable, pr._originalNode may still have delegation buried inside it.
                if (!ReferenceEquals(pr.OriginalNode, predicate))
                {
                    node = new CallNode(node.IRContext, node.Function, node.Scope, new List<IntermediateNode>() { node.Args[0], pr.OriginalNode });
                }

                return CreateNotSupportedErrorAndReturn(node, tableArg);
            }

            // If tableArg has top count, that means we need to materialize the tableArg and can't delegate.
            RetVal result;
            if (tableArg.TryAddFilter(pr.Filter, node, out result))
            {
                return result;
            }
            else
            {
                return MaterializeTableAndAddWarning(tableArg, node);
            }
        }
    }
}
