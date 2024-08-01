// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Diagnostics.Contracts;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Span = Microsoft.PowerFx.Syntax.Span;

namespace Microsoft.PowerFx.Dataverse.Eval.Core
{
    // Search for any behavior functions.
    internal class BehaviorIRVisitor : SearchIRVisitor<BehaviorIRVisitor.RetVal, BehaviorIRVisitor.Context>
    {
        public class RetVal
        {
            public readonly CallNode _behaviorFunc;

            public RetVal(CallNode behaviorFunc)
            {
                Contract.Assert(behaviorFunc != null);
                _behaviorFunc = behaviorFunc;
            }

            public string Name => _behaviorFunc.Function.Name;

            // Where is the reference to ThisRecord?
            public Span Span => _behaviorFunc.IRContext.SourceContext;
        }

        public class Context
        {
        }

        // Find any case of behavior function.
        public static RetVal Find(IntermediateNode expr)
        {
            var v = new BehaviorIRVisitor();
            var ret = expr.Accept(v, null);
            return ret;
        }

        public override RetVal Visit(CallNode node, Context context)
        {
            var func = node.Function;
            if (func.IsBehaviorOnly)
            {
                return new RetVal(node);
            }

            return null;
        }
    }
}
