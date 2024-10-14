// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
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
            IntermediateNode filter = tableArg.HasFilter ? tableArg.Filter : null;
            IntermediateNode orderBy = tableArg.HasOrderBy ? tableArg.OrderBy : null;
            IntermediateNode count = tableArg.HasTopCount ? tableArg.TopCountOrDefault : null;

            context = context.GetContextForPredicateEval(node, tableArg);

            ColumnMap map = null;
            string fieldName = null;

            // Distinct can't be delegated if: Return type is not primitive, or if the field is not a direct field of the table.
            bool cantDelegate = count != null
                || !(TryGetFieldName(context, ((LazyEvalNode)node.Args[1]).Child, out fieldName, out bool invertCoercion, out _) ^ invertCoercion)
                || !IsReturnTypePrimitive(node.IRContext.ResultType)
                || DelegationUtility.IsElasticTable(tableArg.TableType);

            if (!cantDelegate)
            {
                // let's create a single column map ("Value", fieldName) with a distinct on fieldName
                map = new ColumnMap(fieldName);

                // Combine with an existing map
                map = ColumnMap.Combine(tableArg.ColumnMap, map);                

                cantDelegate |= !DelegationUtility.CanDelegateDistinct(map.Distinct, context.CallerTableRetVal.DelegationMetadata.FilterDelegationMetadata);
            }

            if (cantDelegate)
            {
                var materializeTable = Materialize(tableArg);
                if (!ReferenceEquals(node.Args[0], materializeTable))
                {
                    var delegatedDistinct = new CallNode(node.IRContext, node.Function, node.Scope, new List<IntermediateNode>() { materializeTable, node.Args[1] });
                    return Ret(delegatedDistinct);
                }

                return Ret(node);
            }

            // change to original node to current node and appends columnSet and Distinct.
            var resultingTable = new RetVal(_hooks, node, tableArg._sourceTableIRNode, tableArg.TableType, filter, orderBy: orderBy, count, _maxRows, map);

            return resultingTable;
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
