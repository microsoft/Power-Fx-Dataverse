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
        private RetVal ProcessWith(CallNode node, Context context)
        {
            IntermediateNode arg0Intermediate;
            IntermediateNode arg1Intermediate;
            var arg1 = (LazyEvalNode)node.Args[1];

            if (node.Args[0] is RecordNode arg0)
            {
                var withScope = RecordNodeToDictionary(arg0, context);
                arg0Intermediate = new RecordNode(arg0.IRContext, withScope.ToDictionary(kv => new DName(kv.Key), kv => Materialize(kv.Value)));

                context.PushWithScope(withScope);
                arg1Intermediate = Materialize(arg1.Child.Accept(this, context));
                var poppedWithScope = context.PopWithScope();

                if (withScope != poppedWithScope)
                {
                    throw new InvalidOperationException("With scope stack is corrupted");
                }
            }
            else
            {
                arg0Intermediate = Materialize(node.Args[0].Accept(this, context));
                arg1Intermediate = Materialize(arg1.Child.Accept(this, context));
            }

            if (!ReferenceEquals(arg1Intermediate, arg1.Child))
            {
                var lazyArg1 = new LazyEvalNode(arg1.Child.IRContext, arg1Intermediate);
                return Ret(new CallNode(node.IRContext, node.Function, node.Scope, new List<IntermediateNode>() { arg0Intermediate, lazyArg1 }));
            }
            else
            {
                return Ret(new CallNode(node.IRContext, node.Function, node.Scope, new List<IntermediateNode>() { arg0Intermediate, arg1 }));
            }
        }

        private IDictionary<string, RetVal> RecordNodeToDictionary(RecordNode arg0, Context context)
        {
            var scope = new Dictionary<string, RetVal>();
            foreach (var field in arg0.Fields)
            {
                var valueRetVal = field.Value.Accept(this, context);
                scope.Add(field.Key.Value, valueRetVal);
            }

            return scope;
        }
    }
}
