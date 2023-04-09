using Microsoft.AppMagic.Authoring.Importers.DataDescription;
using Microsoft.AppMagic.Authoring.Importers.ServiceConfig;
using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Texl.Builtins;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Dataverse.DataSource;
using Microsoft.PowerFx.Dataverse.Functions;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using BuiltinFunctionsCore = Microsoft.PowerFx.Core.Texl.BuiltinFunctionsCore;
using Span = Microsoft.PowerFx.Syntax.Span;

namespace Microsoft.PowerFx.Dataverse
{
    // Rewrite the tree inject delegation 
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