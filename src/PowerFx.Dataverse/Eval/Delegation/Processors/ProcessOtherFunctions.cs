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
        private RetVal ProcessOtherFunctions(CallNode node, RetVal tableArg)
        {
            var maybeDelegableTable = Materialize(tableArg);

            // If TableArg was delegable, then replace it and no need to add warning. As expr like Concat(Filter(), expr) works fine.
            if (!ReferenceEquals(node.Args[0], maybeDelegableTable))
            {
                var delegableArgs = new List<IntermediateNode>() { maybeDelegableTable };
                delegableArgs.AddRange(node.Args.Skip(1));
                CallNode delegableCallNode;
                if (node.Scope != null)
                {
                    delegableCallNode = new CallNode(node.IRContext, node.Function, node.Scope, delegableArgs);
                }
                else
                {
                    delegableCallNode = new CallNode(node.IRContext, node.Function, delegableArgs);
                }

                return Ret(delegableCallNode);
            }

            return CreateNotSupportedErrorAndReturn(node, tableArg);
        }
    }
}
