// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Core.Types.Enums;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.DelegatedFunctions;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;
using BinaryOpNode = Microsoft.PowerFx.Core.IR.Nodes.BinaryOpNode;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;
using RecordNode = Microsoft.PowerFx.Core.IR.Nodes.RecordNode;
using Span = Microsoft.PowerFx.Syntax.Span;
using UnaryOpNode = Microsoft.PowerFx.Core.IR.Nodes.UnaryOpNode;

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
            var callerReturnType = context.CallerNode.IRContext.ResultType;
            return ProcessAndOr(node, context, delegatedChildren => _hooks.MakeOrCall(callerReturnType, delegatedChildren, node.Scope));
        }

        public RetVal ProcessAnd(CallNode node, Context context)
        {
            var callerReturnType = context.CallerNode.IRContext.ResultType;
            return ProcessAndOr(node, context, delegatedChildren => _hooks.MakeAndCall(callerReturnType, delegatedChildren, node.Scope));
        }
    }
}
