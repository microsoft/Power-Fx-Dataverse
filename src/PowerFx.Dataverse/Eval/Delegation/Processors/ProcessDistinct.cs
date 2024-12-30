// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

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
            if (tableArg.HasGroupBy)
            {
                return ProcessOtherCall(node, tableArg, context);
            }

            context = context.GetContextForPredicateEval(node, tableArg);

            // Distinct can't be delegated if: Return type is not primitive, or if the field is not a direct field of the table.
            if (!TryDelegateDistinct(tableArg, node, context, out var fieldName, out var count, out var columnMap))
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
            var resultingTable = tableArg.With(node, count: count, map: columnMap); 

            return resultingTable;
        }

        private bool TryDelegateDistinct(RetVal tableArg, CallNode distinctCallNode, Context context, out string fieldName, out IntermediateNode count, out ColumnMap columnMap)
        {
            columnMap = null;
            fieldName = null;
            count = tableArg.HasTopCount ? tableArg.TopCountOrDefault : null;

            var canDelegate = count == null
                && TryGetSimpleFieldName(context, ((LazyEvalNode)distinctCallNode.Args[1]).Child, out fieldName)
                && IsReturnTypePrimitive(distinctCallNode.IRContext.ResultType)
                && !DelegationUtility.IsElasticTable(tableArg.TableType);

            if (canDelegate)
            {             
                if (ColumnMap.HasDistinct(tableArg.ColumnMap))
                {
                    string f = fieldName;
                    fieldName = tableArg.ColumnMap.AsStringDictionary().FirstOrDefault(kvp => kvp.Value == f).Key;
                }

                if (DelegationUtility.CanDelegateDistinct(fieldName, context.DelegationMetadata?.FilterDelegationMetadata))
                {
                    // let's create a single column map ("Value", fieldName) with a distinct on fieldName
                    columnMap = new ColumnMap(fieldName);

                    // Combine with an existing map
                    columnMap = ColumnMap.Combine(tableArg.ColumnMap, columnMap, tableArg.TableType);

                    return true;
                }
                else
                {                    
                    canDelegate = false;
                }
            }
            else
            {
                canDelegate = false;
            }

            return canDelegate;
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
