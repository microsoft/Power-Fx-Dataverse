// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Types;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        private RetVal ProcessDistinct(CallNode node, RetVal tableArg, Context context)
        {
            context = context.GetContextForPredicateEval(node, tableArg);

            // Distinct can't be delegated if: Return type is not primitive, or if the field is not a direct field of the table.
            if (TryDelegateDistinct(tableArg, node, context, out var result))
            {
                return result;
            }

            return ProcessOtherCall(node, tableArg, context);
        }

        private bool TryDelegateDistinct(RetVal tableArg, CallNode distinctCallNode, Context context, out RetVal result)
        {
            if (TryGetSimpleFieldName(context, ((LazyEvalNode)distinctCallNode.Args[1]).Child, out var fieldName) && 
                tableArg.TryAddDistinct(fieldName, distinctCallNode, out result))
            {
                return true;
            }

            result = null;
            return false;
        }

        // $$$ We should block this at Authoring time.
        private static bool IsReturnTypePrimitive(FormulaType returnType)
        {
            if (returnType is TableType tableType)
            {
                var column = tableType.FieldNames.First();
                if (tableType.GetFieldType(column)._type.IsPrimitive)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
