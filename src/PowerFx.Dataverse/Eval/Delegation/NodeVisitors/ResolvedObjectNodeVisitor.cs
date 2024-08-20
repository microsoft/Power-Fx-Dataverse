// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse
{
    // Rewrite the tree to inject delegation.
    // If we encounter a dataverse table (something that should be delegated) during the walk, we either:
    // - successfully delegate, which means rewriting to a call an efficient DelegatedFunction,
    // - leave IR unchanged (don't delegate), but issue a warning.
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        // ResolvedObject is a symbol injected by the host.
        // All Table references start as resolved objects.
        public override RetVal Visit(ResolvedObjectNode node, Context context)
        {
            if (!context._ignoreDelegation && node.IRContext.ResultType is TableType aggType)
            {
                // Does the resolve object refer to a dataverse Table?
                if (node.Value is NameSymbol nameSym)
                {
                    var symbolTable = nameSym.Owner;

                    // We need to tell the difference between a direct table,
                    // and another global variable that has that table's type (such as global := Filter(table, true).
                    bool isRealTable = _hooks.IsDelegableSymbolTable(symbolTable);

                    if (isRealTable)
                    {
                        var ret = new RetVal(_hooks, context, node, node, aggType, filter: null, orderBy: null, count: null, _maxRows, columnMap: null);
                        return ret;
                    }
                }
            }

            // Just a regular variable, don't bother delegating.
            return Ret(node);
        }
    }
}
