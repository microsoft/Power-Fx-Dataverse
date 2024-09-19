// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Linq;
using Microsoft.PowerFx.Connectors;
using Microsoft.PowerFx.Core.Entities;
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
                    IExternalTabularDataSource e = node.IRContext.ResultType._type.AssociatedDataSources?.FirstOrDefault(ads => ads.EntityName == nameSym.Name);                                      
                    
                    if (e?.IsDelegatable == true)
                    {
                        var ret = new RetVal(_hooks, node, node, aggType, filter: null, orderBy: null, count: null, _maxRows, columnMap: null, delegationMetadata: e.DelegationMetadata);
                        return ret;
                    }
                }
            }

            // Just a regular variable or non-delegable table, don't bother delegating.
            return Ret(node);
        }
    }
}
