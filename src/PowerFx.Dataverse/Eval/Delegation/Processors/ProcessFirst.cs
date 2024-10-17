// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Types;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        private RetVal ProcessFirst(CallNode node, RetVal tableArg, Context context)
        {
            IntermediateNode orderBy = tableArg.HasOrderBy ? tableArg.OrderBy : null;

            if (tableArg.DelegationMetadata?.SortDelegationMetadata == null)
            {
                return ProcessOtherCall(node, tableArg, context);
            }

            var countOne = new NumberLiteralNode(IRContext.NotInSource(FormulaType.Number), 1);
            var res = new RetVal(_hooks, node, tableArg._sourceTableIRNode, tableArg.TableType, tableArg.Filter, orderBy: orderBy, countOne, _maxRows, tableArg.ColumnMap);
            return res;
        }
    }
}
