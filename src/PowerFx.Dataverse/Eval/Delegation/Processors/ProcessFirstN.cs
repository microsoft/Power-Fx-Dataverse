﻿// Copyright (c) Microsoft Corporation.
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
        private RetVal ProcessFirstN(CallNode node, RetVal tableArg, Context context)
        {
            if (!DelegationUtility.CanDelegateFirst(tableArg.DelegationMetadata))
            {
                return ProcessOtherCall(node, tableArg, context);
            }

            // Add default count of 1 if not specified.
            if (node.Args.Count == 1)
            {
                node.Args.Add(new NumberLiteralNode(IRContext.NotInSource(FormulaType.Number), 1));
            }
            else if (node.Args.Count != 2)
            {
                return CreateNotSupportedErrorAndReturn(node, tableArg);
            }

            if (tableArg.TryAddTopCount(node.Args[1], node, out RetVal result))
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
