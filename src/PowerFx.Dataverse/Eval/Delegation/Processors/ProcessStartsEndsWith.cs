// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Dataverse.DataSource;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        /// <summary>
        /// Delegates the expression which has StartsWith/EndsWith in predicate. e.g. Filter(t, StartsWith(t.name, "abc")).
        /// </summary>
        private RetVal ProcessStartsEndsWith(CallNode node, Context context, bool isStartWith)
        {
            if (!context.IsPredicateEvalInProgress)
            {
                return base.Visit(node, context);
            }

            IList<string> relations = null;

            if (TryGetValidFieldAndRelation(context, node, out string fieldName, out IntermediateNode rightNode, out relations)

                // check if the field supports starts/ends with in capabilities.
                && context.DelegationMetadata?.DoesColumnSupportStartsEndsWith(fieldName, context.GetCallerTableFieldType(fieldName), isStartWith) == true)
            {
                var startsEndsWithNode = _hooks.MakeStartsEndsWithCall(context.CallerTableNode, context.CallerTableRetVal.TableType, relations, fieldName, rightNode, context.CallerNode.Scope, isStartWith);
                
                var ret = CreateBinaryOpRetVal(context, node, startsEndsWithNode);
                return ret;
            }

            RetVal arg0c = node.Args[0].Accept(this, context);

            return base.Visit(node, context, arg0c);
        }

        private bool TryGetValidFieldAndRelation(
            Context context,
            CallNode node,
            out string fieldName,
            out IntermediateNode rightNode,
            out IList<string> relations)
        {
            // Initialize output variables
            fieldName = null;
            rightNode = null;
            relations = null;

            // Try to get the field name using either of the methods
            return (TryGetFieldName(context: context, left: node.Args[0], right: node.Args[1], op: BinaryOpKind.Invalid, out fieldName, out rightNode, out _, out var fieldFunctions) && fieldFunctions.IsNullOrEmpty())
                || (TryGetRelationField(context: context, left: node.Args[0], right: node.Args[1], op: BinaryOpKind.Invalid, out fieldName, out relations, out rightNode, out _, out fieldFunctions) && !fieldFunctions.Any());
        }
    }
}
