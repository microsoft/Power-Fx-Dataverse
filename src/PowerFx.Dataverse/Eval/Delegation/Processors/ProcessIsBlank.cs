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
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
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
        private RetVal ProcessIsBlank(CallNode node, Context context)
        {
            IList<string> relations = null;

            // $$$ revisit coercion kind here.
            if ((TryGetFieldName(context, node, out var fieldName, out var invertCoercion, out _, out var fieldOperation) && !invertCoercion)
                || (TryGetRelationField(context, node, out fieldName, out relations, out invertCoercion, out var coercionKind, out fieldOperation) && !invertCoercion))
            {
                var blankNode = new CallNode(IRContext.NotInSource(FormulaType.Blank), BuiltinFunctionsCore.Blank);

                // BinaryOpKind doesn't matter for IsBlank because all value will be compared to null, so just use EqText.
                var eqNode = _hooks.MakeEqCall(context.CallerTableNode, context.CallerTableNode.IRContext.ResultType, relations, fieldOperation, fieldName, BinaryOpKind.EqText, blankNode, context.CallerNode.Scope);
                var ret = CreateBinaryOpRetVal(context, node, eqNode);
                return ret;
            }

            RetVal arg0c = node.Args[0].Accept(this, context);

            return base.Visit(node, context, arg0c);
        }
    }
}
