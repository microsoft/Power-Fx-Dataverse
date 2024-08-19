// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Types;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        public override RetVal Visit(CallNode node, Context context)
        {
            var funcName = node.Function.Name;

            if (funcName == BuiltinFunctionsCore.And.Name && context.IsPredicateEvalInProgress)
            {
                return ProcessAnd(node, context);
            }
            else if (funcName == BuiltinFunctionsCore.Or.Name && context.IsPredicateEvalInProgress)
            {
                return ProcessOr(node, context);
            }            
            else if (funcName == BuiltinFunctionsCore.IsBlank.Name && context.IsPredicateEvalInProgress)
            {
                return ProcessIsBlank(node, context);
            }

            // Some functions don't require delegation.
            // Using a table diretly as arg0 here doesn't generate a warning.
            if (funcName == BuiltinFunctionsCore.IsBlank.Name ||
                funcName == BuiltinFunctionsCore.IsError.Name ||
                funcName == "Patch" || funcName == "Collect")
            {
                RetVal arg0c = node.Args[0].Accept(this, context);

                return base.Visit(node, context, arg0c);
            }

            if (node.Args.Count == 0)
            {
                // Delegated functions require arg0 is the table.
                // So a 0-length args can't delegate.
                return base.Visit(node, context);
            }

            // Since With supports scopes, it needs to be processed differently.
            if (funcName == BuiltinFunctionsCore.With.Name)
            {
                return ProcessWith(node, context);
            }

            // Only below function fulfills assumption that first arg is Table
            if (!(node.Function.ParamTypes.Length > 0 && node.Function.ParamTypes[0].IsTable))
            {
                return base.Visit(node, context);
            }

            RetVal tableArg;

            // special casing Scope access for With()
            if (node.Args[0] is ScopeAccessNode scopedFirstArg && scopedFirstArg.IRContext.ResultType is TableType && scopedFirstArg.Value is ScopeAccessSymbol scopedSymbol
                && TryGetScopedVariable(context.WithScopes, scopedSymbol.Name, out var scopedNode))
            {
                tableArg = scopedNode;
            }
            else
            {
                tableArg = node.Args[0].Accept(this, context);
            }

            if (!tableArg.IsDelegating)
            {
                return base.Visit(node, context, tableArg);
            }

            RetVal ret = funcName switch
            {
                "Distinct" => ProcessDistinct(node, tableArg, context),
                "Filter" => ProcessFilter(node, tableArg, context),
                "First" => ProcessFirst(node, tableArg),
                "FirstN" => ProcessFirstN(node, tableArg),
                "ForAll" => ProcessForAll(node, tableArg, context),
                "LookUp" => ProcessLookUp(node, tableArg, context),
                "Sort" or
                "SortByColumns" => ProcessSort(node, tableArg, context),
                "ShowColumns" => ProcessShowColumns(node, tableArg),
                _ => ProcessOtherCall(node, tableArg, context)
            };

            return ret;
        }
    }
}
