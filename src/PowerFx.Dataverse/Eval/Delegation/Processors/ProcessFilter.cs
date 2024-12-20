// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
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

            // Can't Filter() on Group By.
            if (tableArg.HasGroupByNode)
            {
                return ProcessOtherCall(node, tableArg, context);
            }

            IntermediateNode predicate = node.Args[1];
            IntermediateNode orderBy = tableArg.HasOrderBy ? tableArg.OrderBy : null;

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

            RetVal result;

            // If tableArg has top count, that means we need to materialize the tableArg and can't delegate.
            if (tableArg.HasTopCount)
            {
                result = MaterializeTableAndAddWarning(tableArg, node);
            }
            else
            {
                // Since table was delegating it potentially has filter attached to it, so also add that filter to the new filter.
                var filterCombined = tableArg.AddFilter(pr.Filter, node.Scope);
                result = new RetVal(_hooks, node, tableArg._sourceTableIRNode, tableArg.TableType, filterCombined, orderBy: orderBy, count: null, _maxRows, tableArg.ColumnMap, groupByNode: null);
            }

            return result;
        }
    }
}
