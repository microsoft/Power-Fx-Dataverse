// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Core.Types.Enums;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.DelegatedFunctions;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;
using BinaryOpNode = Microsoft.PowerFx.Core.IR.Nodes.BinaryOpNode;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;
using RecordNode = Microsoft.PowerFx.Core.IR.Nodes.RecordNode;
using Span = Microsoft.PowerFx.Syntax.Span;
using UnaryOpNode = Microsoft.PowerFx.Core.IR.Nodes.UnaryOpNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        // BinaryOpNode can be only materialized when called via CallNode.
        public override RetVal Visit(BinaryOpNode node, Context context)
        {
            if (!context.IsPredicateEvalInProgress)
            {
                return base.Visit(node, context);
            }

            var caller = context.CallerNode;
            var callerReturnType = caller.IRContext.ResultType;
            var callerSourceTable = context.CallerTableNode;
            var callerScope = caller.Scope;

            TableType tableType;
            if (callerReturnType is RecordType recordType)
            {
                tableType = recordType.ToTable();
            }
            else
            {
                tableType = (TableType)callerReturnType;
            }

            IList<string> relations = null;

            // Either left or right is field (not both)
            if (!TryGetFieldName(context, node.Left, node.Right, node.Op, out var fieldName, out var rightNode, out var operation) &&
                !TryGetRelationField(context, node.Left, node.Right, node.Op, out fieldName, out relations, out rightNode, out operation))
            {
                ExpressionIRVisitor eirv = new ExpressionIRVisitor();
                ExpressionIRVisitor.ExpressionContext ec = new ExpressionIRVisitor.ExpressionContext();

                ExpressionIRVisitor.ExpressionRetVal erv = node.Accept(eirv, ec);
                IntermediateNode node2 = eirv.Materialize(erv);

                if (erv == null || node2 == null)
                {
                    return new RetVal(node);
                }

                node = (BinaryOpNode)node2;

                if (!TryGetFieldName(context, node.Left, node.Right, node.Op, out fieldName, out rightNode, out operation) &&
                    !TryGetRelationField(context, node.Left, node.Right, node.Op, out fieldName, out relations, out rightNode, out operation))
                {
                    return new RetVal(node);
                }
            }

            if (context.CallerTableRetVal.HasColumnMap && context.CallerTableRetVal.ColumnMap.AsStringDictionary().TryGetValue(fieldName, out string realFieldName))
            {
                fieldName = realFieldName;
            }

            if (!IsRelationDelegationAllowed(tableType, relations))
            {
                return new RetVal(node);
            }

            var findThisRecord = ThisRecordIRVisitor.FindThisRecordUsage(caller, rightNode);
            if (findThisRecord != null)
            {
                CreateThisRecordErrorAndReturn(caller, findThisRecord);
            }

            var findBehaviorFunc = BehaviorIRVisitor.Find(rightNode);
            if (findBehaviorFunc != null)
            {
                return CreateBehaviorErrorAndReturn(caller, findBehaviorFunc);
            }

            var retDelegationVisitor = rightNode.Accept(this, context);
            if (retDelegationVisitor.IsDelegating)
            {
                rightNode = Materialize(retDelegationVisitor);
            }
            else
            {
                rightNode = retDelegationVisitor.OriginalNode;
            }

            RetVal ret;

            if (IsOpKindEqualityComparison(operation))
            {
                var eqNode = _hooks.MakeEqCall(callerSourceTable, tableType, relations, fieldName, operation, rightNode, callerScope);
                ret = CreateBinaryOpRetVal(context, node, eqNode);
            }
            else if (IsOpKindInequalityComparison(operation))
            {
                var neqNode = _hooks.MakeNeqCall(callerSourceTable, tableType, relations, fieldName, operation, rightNode, callerScope);
                ret = CreateBinaryOpRetVal(context, node, neqNode);
            }
            else if (IsOpKindLessThanComparison(operation))
            {
                var ltNode = _hooks.MakeLtCall(callerSourceTable, tableType, relations, fieldName, operation, rightNode, callerScope);
                ret = CreateBinaryOpRetVal(context, node, ltNode);
            }
            else if (IsOpKindLessThanEqualComparison(operation))
            {
                var leqNode = _hooks.MakeLeqCall(callerSourceTable, tableType, relations, fieldName, operation, rightNode, callerScope);
                ret = CreateBinaryOpRetVal(context, node, leqNode);
            }
            else if (IsOpKindGreaterThanComparison(operation))
            {
                var gtNode = _hooks.MakeGtCall(callerSourceTable, tableType, relations, fieldName, operation, rightNode, callerScope);
                ret = CreateBinaryOpRetVal(context, node, gtNode);
            }
            else if (IsOpKindGreaterThanEqalComparison(operation))
            {
                var geqNode = _hooks.MakeGeqCall(callerSourceTable, tableType, relations, fieldName, operation, rightNode, callerScope);
                ret = CreateBinaryOpRetVal(context, node, geqNode);
            }
            else
            {
                ret = new RetVal(node);
            }

            return ret;
        }

        private bool IsRelationDelegationAllowed(TableType tableType, IList<string> relations)
        {
            // Assume we can delegate if we can't find the metadata. It is possible if the table was the output of ShowColumns().
            var result = true;

            // For Elastic tables, we can delegate only if the field is a direct field of the table and NOT if it is a relation. As elastic table currently doesn't support relations in delegation.
            if (DelegationUtility.TryGetEntityMetadata(tableType, out var metadata))
            {
                result = !(metadata.IsElasticTable() && relations != null && relations.Count > 0);
            }

            return result;
        }

        private bool TryGetRelationField(Context context, IntermediateNode left, IntermediateNode right, BinaryOpKind op, out string fieldName, out IList<string> relations, out IntermediateNode node, out BinaryOpKind opKind)
        {
            if (TryGetRelationField(context, left, out var leftField, out var leftRelation) && !TryGetFieldName(context, right, out _))
            {
                fieldName = leftField;
                relations = leftRelation;
                node = right;
                opKind = op;
                return true;
            }
            else if (TryGetRelationField(context, right, out var rightField, out var rightRelation) && !TryGetFieldName(context, left, out _))
            {
                fieldName = rightField;
                relations = rightRelation;
                node = left;
                if (TryInvertLeftRight(op, out var invertedOp))
                {
                    opKind = invertedOp;
                    return true;
                }
                else
                {
                    opKind = default;
                    return false;
                }
            }
            else if (TryGetRelationField(context, left, out var leftField2, out _) && TryGetRelationField(context, right, out var rightField2, out _))
            {
                if (leftField2 == rightField2)
                {
                    // Issue warning
                    if (IsOpKindEqualityComparison(op))
                    {
                        var min = left.IRContext.SourceContext.Lim;
                        var lim = right.IRContext.SourceContext.Min;
                        var span = new Span(min, lim);
                        var reason = new ExpressionError()
                        {
                            Span = span,
                            Severity = ErrorSeverity.Warning,
                            ResourceKey = TexlStrings.WrnDelegationPredicate
                        };
                        this.AddError(reason);
                    }

                    opKind = op;
                    fieldName = default;
                    relations = default;
                    node = default;
                    return false;
                }
            }

            opKind = op;
            node = default;
            fieldName = default;
            relations = default;
            return false;
        }
    }
}
