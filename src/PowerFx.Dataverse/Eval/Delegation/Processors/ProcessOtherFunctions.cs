// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        /// <summary>
        /// Use it to delegate subqueries when whole function can't be delegated.
        /// </summary>
        private RetVal ProcessOtherCall(CallNode node, RetVal tableArg, Context context)
        {
            var maybeDelegableTable = Materialize(tableArg);

            // If TableArg was delegable, then replace it and no need to add warning. As expr like Concat(Filter(), expr) works fine.
            if (!ReferenceEquals(node.Args[0], maybeDelegableTable))
            {
                var delegableArgs = new List<IntermediateNode>() { maybeDelegableTable };
                delegableArgs.AddRange(node.Args.Skip(1));
                CallNode delegableCallNode;
                if (node.Scope != null)
                {
                    delegableCallNode = new CallNode(node.IRContext, node.Function, node.Scope, delegableArgs);
                }
                else
                {
                    delegableCallNode = new CallNode(node.IRContext, node.Function, delegableArgs);
                }

                return Ret(delegableCallNode);
            }

            // Traverse children to see if any could be delegable
            RetVal rv = base.Visit(node, context);

            if (!rv.IsDelegating)
            {
                // Here we check is any of the CallNode args might have been delegated
                // and, if so, we recreate the CallNode based on these new arguments
                if (!ReferenceEquals(rv.OriginalNode, node))
                {                    
                    node = new CallNode(node.IRContext, node.Function, node.Scope, ((CallNode)rv.OriginalNode).Args);
                }
            }
            
            return CreateNotSupportedErrorAndReturn(node, tableArg);
        }
    }
}
