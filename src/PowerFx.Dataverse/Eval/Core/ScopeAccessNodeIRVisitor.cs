// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Diagnostics.Contracts;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Span = Microsoft.PowerFx.Syntax.Span;

namespace Microsoft.PowerFx.Dataverse.Eval.Core
{
    // Search for any behavior functions.
    internal class ScopeAccessNodeIRVisitor : SearchIRVisitor<ScopeAccessNodeIRVisitor.RetVal, ScopeAccessNodeIRVisitor.Context>
    {
        public class RetVal
        {
            public readonly ScopeAccessNode SanNode;

            public RetVal(ScopeAccessNode sanNode)
            {
                SanNode = sanNode;
            }
        }

        public class Context
        {
        }

        public static bool ContainsScopeAccessNode(IntermediateNode expr) => ScopeAccessNodeIRVisitor.Find(expr)?.SanNode != null;

        public static RetVal Find(IntermediateNode node) => node.Accept(new ScopeAccessNodeIRVisitor(), null);

        public override RetVal Visit(ScopeAccessNode node, Context context) => new RetVal(node);
    }
}
