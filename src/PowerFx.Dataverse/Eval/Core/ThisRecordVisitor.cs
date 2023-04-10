using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Span = Microsoft.PowerFx.Syntax.Span;

namespace Microsoft.PowerFx.Dataverse.Eval.Core
{
    // Search tree to see if a branch uses 'ThisRecord'. 
    // This can be used for detecting Loop Invariant Code Motion, which is essential for determining
    // if a predicate is delegable. 
    internal class ThisRecordVisitor : SearchIRVisitor<ThisRecordVisitor.RetVal, ThisRecordVisitor.Context>
    {
        private CallNode _caller;

        public class RetVal
        {
            public IntermediateNode _usage;

            // Where is the reference to ThisRecord?
            public Span Span => _usage.IRContext.SourceContext;
        }
        public class Context
        {
        }

        // Does expr use 'ThisRecord' for CallNode?
        public static RetVal FindThisRecordUsage(CallNode call, IntermediateNode expr)
        {
            var v = new ThisRecordVisitor { _caller = call };
            var ret = expr.Accept(v, null);
            return ret;
        }

        public override RetVal Visit(ScopeAccessNode node, Context context)
        {
            if (node.Value is ScopeAccessSymbol x)
            {
                if (x.Parent.Id == _caller.Scope.Id)
                {
                    // Found access to ThisRecord 
                    return new RetVal
                    {
                        _usage = node
                    };
                }
            }
            return null;
        }
    }
}