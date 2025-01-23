// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;
using RecordNode = Microsoft.PowerFx.Core.IR.Nodes.RecordNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        private RetVal ProcessCountIf(CallNode node, RetVal tableArg, Context context)
        {
            var predicate = node.Args[1];
            var predicteContext = context.GetContextForPredicateEval(node, tableArg);
            var pr = predicate.Accept(this, predicteContext);

            if (tableArg.TryAddReturnRowCount(node, predicate: pr, out RetVal result))
            {
                return result;
            }

            return ProcessOtherCall(node, tableArg, context);
        }

        private RetVal ProcessCountRows(CallNode node, RetVal tableArg, Context context)
        {
            if (tableArg.TryAddReturnRowCount(node, predicate: null, out RetVal result))
            {
                return result;
            }

            return ProcessOtherCall(node, tableArg, context);
        }
    }
}
