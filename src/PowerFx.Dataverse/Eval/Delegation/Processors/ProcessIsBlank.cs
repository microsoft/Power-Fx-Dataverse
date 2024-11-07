// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Types;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        private RetVal ProcessIsBlank(CallNode node, Context context)
        {
            IList<string> relations = null;

            if ((TryGetFieldName(context, node, out var fieldName, out var invertCoercion, out _, out var fieldOperation) && !invertCoercion)
                || (TryGetRelationField(context, node, out fieldName, out relations, out invertCoercion, out var coercionKind, out fieldOperation) && !invertCoercion))
            {
                var blankNode = new CallNode(IRContext.NotInSource(FormulaType.Blank), BuiltinFunctionsCore.Blank);

                string fieldNameForCapabilities = fieldName;

                if (relations != null && relations.Any())
                {
                    RelationMetadata relationMD = DelegationUtility.DeserializeRelatioMetadata(relations.First());
                    fieldNameForCapabilities = relationMD.ReferencingFieldName;
                }

                if (context.DelegationMetadata?.FilterDelegationMetadata.IsDelegationSupportedByColumn(DPath.Root.Append(new DName(fieldNameForCapabilities)), DelegationCapability.Null) == true)
                {
                    // BinaryOpKind doesn't matter for IsBlank because all value will be compared to null, so just use EqText.
                    var eqNode = _hooks.MakeEqCall(context.CallerTableNode, context.CallerTableNode.IRContext.ResultType, relations, fieldOperation, fieldName, BinaryOpKind.EqText, blankNode, context.CallerNode.Scope);
                    var ret = CreateBinaryOpRetVal(context, node, eqNode);
                    return ret;
                }
            }

            RetVal arg0c = node.Args[0].Accept(this, context);

            return base.Visit(node, context, arg0c);
        }
    }
}
