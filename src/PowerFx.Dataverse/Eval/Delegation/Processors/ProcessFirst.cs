// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Types;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        private RetVal ProcessFirst(CallNode node, RetVal tableArg, Context context)
        {
            IntermediateNode orderBy = tableArg.HasOrderBy ? tableArg.OrderBy : null;

            if (!DelegationUtility.CanDelegateFirst(tableArg.DelegationMetadata))
            {
                return ProcessOtherCall(node, tableArg, context);
            }

            var countOne = new NumberLiteralNode(IRContext.NotInSource(FormulaType.Number), 1);

            RetVal result;
            if (tableArg.TryAddTopCount(countOne, node, out result))
            {
                return result;
            }
            else
            {
                return ProcessOtherCall(node, tableArg, context);
            }
        }
    }
}
