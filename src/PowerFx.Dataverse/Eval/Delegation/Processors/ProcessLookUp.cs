// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using BinaryOpNode = Microsoft.PowerFx.Core.IR.Nodes.BinaryOpNode;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        private RetVal ProcessLookUp(CallNode node, RetVal tableArg, Context context)
        {
            RetVal result;

            if (node.Args.Count != 2)
            {
                return CreateNotSupportedErrorAndReturn(node, tableArg);
            }

            // LookUp() with group by is not supported. Ie LookUp(Summarize(...), ...), other way around is supported.
            if (tableArg.HasGroupByNode)
            {
                return ProcessOtherCall(node, tableArg, context);
            }

            IntermediateNode orderBy = tableArg.HasOrderBy ? tableArg.OrderBy : null;

            var predicate = node.Args[1];
            var predicteContext = context.GetContextForPredicateEval(node, tableArg);

            // Pattern match to see if predicate is GUID delegable.
            if (predicate is LazyEvalNode arg1b)
            {
                if (arg1b.Child is BinaryOpNode binOp &&
                    binOp.Op == BinaryOpKind.EqGuid &&
                    TryMatchPrimaryId(binOp.Left, binOp.Right, out _, out var guidValue, tableArg))
                {
                    CheckForNopLookup(node);

                    // Pattern match to see if predicate is delegable.
                    //  Lookup(Table, Id=Guid)
                    var retVal2 = guidValue.Accept(this, context);
                    var right = Materialize(retVal2);

                    var findThisRecord = ThisRecordIRVisitor.FindThisRecordUsage(node, right);
                    if (findThisRecord == null)
                    {
                        var findBehaviorFunc = BehaviorIRVisitor.Find(right);
                        if (findBehaviorFunc != null)
                        {
                            CreateBehaviorErrorAndReturn(node, findBehaviorFunc);
                        }
                        else
                        {
                            // We can successfully delegate this call.
                            // __retrieveGUID(table, guid);

                            if (IsTableArgLookUpDelegable(context, tableArg))
                            {
                                var newNode = _hooks.MakeRetrieveCall(tableArg, right);
                                return Ret(newNode);
                            }
                            else
                            {
                                // if tableArg was another delegation (e.g. Filter()), then we need to Materialize the call and can't delegate lookup.
                                return MaterializeTableAndAddWarning(tableArg, node);
                            }
                        }
                    }
                    else
                    {
                        return CreateThisRecordErrorAndReturn(node, findThisRecord);
                    }
                }
                else if (arg1b.Child is CallNode arg1MaybeAnd && arg1MaybeAnd.Function.Name == BuiltinFunctionsCore.And.Name && arg1MaybeAnd.Args.Count == 2)
                {
                    // If LookUp predicate only has primary key comparison and partition id and table is elastic table, then we can delegate the call.
                    var arg1OfAnd = arg1MaybeAnd.Args[0];
                    var arg2OfAnd = ((LazyEvalNode)arg1MaybeAnd.Args[1]).Child;
                    if (TryMatchElasticIds(predicteContext, arg1OfAnd, arg2OfAnd, out var guidArg, out var partitionIdArg))
                    {
                        var guidRetVal = guidArg.Accept(this, context);
                        var partitionIdRetVal = partitionIdArg.Accept(this, context);
                        var materializedGuid = Materialize(guidRetVal);
                        var materializedPartitionId = Materialize(partitionIdRetVal);
                        if (IsTableArgLookUpDelegable(context, tableArg))
                        {
                            var newNode = _hooks.MakeElasticRetrieveCall(tableArg, materializedGuid, materializedPartitionId);
                            return Ret(newNode);
                        }
                        else
                        {
                            // if tableArg was another delegation (e.g. Filter()), then we need to Materialize the call and can't delegate lookup.
                            return MaterializeTableAndAddWarning(tableArg, node);
                        }
                    }
                }
            }

            // Pattern match to see if predicate is delegable when field is non primary key.
            var pr = predicate.Accept(this, predicteContext);

            if (!pr.IsDelegating)
            {
                // Though entire predicate is not delegable, pr._originalNode may still have delegation buried inside it.
                if (!ReferenceEquals(pr.OriginalNode, predicate))
                {
                    node = new CallNode(node.IRContext, node.Function, node.Scope, new List<IntermediateNode>() { node.Args[0], pr.OriginalNode });
                }

                return CreateNotSupportedErrorAndReturn(node, tableArg);
            }

            // if tableArg was DV Table, delegate the call.
            if (IsTableArgLookUpDelegable(context, tableArg))
            {
                var filterCombined = tableArg.AddFilter(pr.Filter, node.Scope);
                result = new RetVal(_hooks, node, tableArg._sourceTableIRNode, tableArg.TableType, filterCombined, orderBy: orderBy, count: null, _maxRows, tableArg.ColumnMap, groupByNode: null);
            }
            else
            {
                // if tableArg was a other delegation (e.g. Filter()), then we need to Materialize the call and can't delegate lookup.
                result = MaterializeTableAndAddWarning(tableArg, node);
            }

            return result;
        }

        // Does this match: primaryKey=value
        private bool MatchPrimaryId(IntermediateNode primaryIdField, RetVal tableArg)
        {
            return primaryIdField is ScopeAccessNode san && san.Value is ScopeAccessSymbol sas
                ? tableArg.TryGetPrimaryIdFieldName(out var primaryId) && (sas.Name == primaryId)
                : false;
        }

        // Normalize order? (Id=Guid) vs (Guid=Id)
        private bool TryMatchPrimaryId(IntermediateNode left, IntermediateNode right, out IntermediateNode primaryIdField, out IntermediateNode guidValue, RetVal tableArg)
        {
            if (MatchPrimaryId(left, tableArg))
            {
                primaryIdField = left;
                guidValue = right;
                return true;
            }
            else if (MatchPrimaryId(right, tableArg))
            {
                primaryIdField = right;
                guidValue = left;
                return true;
            }

            primaryIdField = null;
            guidValue = null;
            return false;
        }

        /// <summary>
        /// If predicate is comparing primary key and partition id, and table is elastic table, then we can delegate the call to a faster api.
        /// </summary>
        /// <param name="predicteContext"></param>
        /// <param name="arg1OfAnd"></param>
        /// <param name="arg2OfAnd"></param>
        /// <param name="guidArg"></param>
        /// <param name="partitionIdArg"></param>
        /// <returns></returns>
        private bool TryMatchElasticIds(Context predicteContext, IntermediateNode arg1OfAnd, IntermediateNode arg2OfAnd, out IntermediateNode guidArg, out IntermediateNode partitionIdArg)
        {
            if (predicteContext.CallerTableRetVal.IsElasticTable && arg1OfAnd is BinaryOpNode arg1b && arg2OfAnd is BinaryOpNode arg2b)
            {
                if (TryMatchPrimaryId(arg1b.Left, arg1b.Right, out _, out guidArg, predicteContext.CallerTableRetVal)
                    && TryGetFieldName(predicteContext, arg2b.Left, arg2b.Right, arg2b.Op, out var fieldName, out var maybePartitionId, out var opKind, out var fieldFunctions)
                    && opKind == default
                    && fieldFunctions.IsNullOrEmpty()
                    && fieldName == "partitionid"
                    && IsOpKindEqualityComparison(opKind))
                {
                    partitionIdArg = maybePartitionId;
                    return true;
                }
                else if (TryMatchPrimaryId(arg2b.Left, arg2b.Right, out _, out guidArg, predicteContext.CallerTableRetVal)
                    && TryGetFieldName(predicteContext, arg1b.Left, arg1b.Right, arg1b.Op, out fieldName, out maybePartitionId, out opKind, out fieldFunctions)
                    && opKind == default
                    && fieldFunctions.IsNullOrEmpty()
                    && fieldName == "partitionid"
                    && IsOpKindEqualityComparison(opKind))
                {
                    partitionIdArg = maybePartitionId;
                    return true;
                }
            }

            guidArg = null;
            partitionIdArg = null;
            return false;
        }

        // Issue warning on typo:
        //  Filter(table, id=id)
        //  LookUp(table, id=id)
        //
        // It's legal (so must be warning, not error). Likely, correct behavior is:
        //  LookUp(table, ThisRecord.id=[@id])
        private void CheckForNopLookup(CallNode node)
        {
            var func = node.Function.Name;
            if (func == "LookUp" || func == "Filter")
            {
                if (node.Args.Count == 2)
                {
                    if (node.Args[1] is LazyEvalNode arg1b && arg1b.Child is BinaryOpNode predicate)
                    {
                        var left = predicate.Left;
                        var right = predicate.Right;

                        if (left is ScopeAccessNode left1 && right is ScopeAccessNode right1)
                        {
                            if (left1.Value is ScopeAccessSymbol left2 && right1.Value is ScopeAccessSymbol right2)
                            {
                                if (left2.Parent.Id == right2.Parent.Id &&
                                    left2.Name == right2.Name)
                                {
                                    var reason = new ExpressionError()
                                    {
                                        Span = predicate.IRContext.SourceContext,
                                        Severity = ErrorSeverity.Warning,
                                        ResourceKey = TexlStrings.WrnDelegationPredicate
                                    };
                                    this.AddError(reason);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
