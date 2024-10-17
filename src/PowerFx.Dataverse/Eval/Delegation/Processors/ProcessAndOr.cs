// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Syntax;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        private RetVal ProcessAndOr(CallNode node, Context context, Func<IList<IntermediateNode>, IntermediateNode> filterDelegate)
        {
            bool isDelegating = true;
            List<IntermediateNode> delegatedChild = new ();

            foreach (var arg in node.Args)
            {
                var delegatedArg = arg is LazyEvalNode lazyEvalNode
                                    ? lazyEvalNode.Child.Accept(this, context)
                                    : arg.Accept(this, context);

                if (delegatedArg.IsDelegating)
                {
                    if (delegatedArg.HasFilter)
                    {
                        delegatedChild.Add(delegatedArg.Filter);
                    }
                }
                else
                {
                    isDelegating = false;
                    break;
                }
            }

            if (isDelegating)
            {
                var filter = filterDelegate(delegatedChild);
                var rVal = CreateBinaryOpRetVal(context, node, filter);
                return rVal;
            }

            return new RetVal(node);
        }

        public RetVal ProcessOr(CallNode node, Context context)
        {
            if (context.DelegationMetadata?.FilterDelegationMetadata.IsBinaryOpSupportedByTable(BinaryOp.Or) != true)
            {
                return new RetVal(node);
            }

            var callerReturnType = context.CallerNode.IRContext.ResultType;
            return ProcessAndOr(node, context, delegatedChildren => _hooks.MakeOrCall(callerReturnType, delegatedChildren, node.Scope));
        }

        public RetVal ProcessAnd(CallNode node, Context context)
        {
            if (context.DelegationMetadata?.FilterDelegationMetadata.IsBinaryOpSupportedByTable(Syntax.BinaryOp.And) != true)
            {
                return new RetVal(node);
            }

            var callerReturnType = context.CallerNode.IRContext.ResultType;
            return ProcessAndOr(node, context, delegatedChildren => _hooks.MakeAndCall(callerReturnType, delegatedChildren, node.Scope));
        }
    }
}
