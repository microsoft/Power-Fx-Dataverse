// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        private RetVal ProcessNot(CallNode node, Context context)
        {
            if (!context.IsPredicateEvalInProgress)
            {
                return base.Visit(node, context);
            }

            IList<string> relations = null;

            // Currently only supports lamda Not(IsBlank()) delegation.
            if (node.Args[0] is CallNode callNode && 
                callNode.Function.Name == BuiltinFunctionsCore.IsBlank.Name && 
                ((TryGetFieldName(context, callNode, out string fieldName, out bool invertCoercion, out _) && !invertCoercion) ||
                 (TryGetRelationField(context, callNode, out fieldName, out relations, out invertCoercion, out _) && !invertCoercion)))
            {
                var blankNode = new CallNode(IRContext.NotInSource(FormulaType.Blank), BuiltinFunctionsCore.Blank);

                if (context.DelegationMetadata?.FilterDelegationMetadata.IsUnaryOpSupportedByTable(UnaryOp.Not) == true)
                {
                    // BinaryOpKind doesn't matter for Not(IsBlank()) because all value will be compared to null, so just use NeqText.
                    CallNode neqNode = _hooks.MakeNeqCall(context.CallerTableNode, context.CallerTableRetVal.TableType, relations, fieldName, BinaryOpKind.NeqText, blankNode, context.CallerNode.Scope);
                    
                    return CreateBinaryOpRetVal(context, node, neqNode);                    
                }
            }

            RetVal arg0c = node.Args[0].Accept(this, context);

            return base.Visit(node, context, arg0c);
        }
    }
}
