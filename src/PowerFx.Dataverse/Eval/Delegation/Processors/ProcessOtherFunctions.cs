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
        private RetVal ProcessOtherCall(CallNode node, RetVal tableArg, Context context)
        {            
            List<IntermediateNode> args = new List<IntermediateNode>();
            bool isDelegating = false;

            // "other" calls won't be delegable but inner arguments could be, so let's investigate those
            foreach (IntermediateNode arg in node.Args)
            {
                RetVal rv = arg.Accept(this, context);

                if (rv.IsDelegating)
                {                    
                    IntermediateNode newNode = Materialize(rv);

                    if (!ReferenceEquals(newNode, arg))
                    {
                        isDelegating = true;
                    }

                    args.Add(newNode);
                }
                else
                {
                    args.Add(arg);
                }
            }

            if (isDelegating)
            {
                if (node.Scope != null)
                {
                    return Ret(new CallNode(node.IRContext, node.Function, node.Scope, args));
                }

                return Ret(new CallNode(node.IRContext, node.Function, args));
            }

            return CreateNotSupportedErrorAndReturn(node, tableArg);
        }
    }
}
