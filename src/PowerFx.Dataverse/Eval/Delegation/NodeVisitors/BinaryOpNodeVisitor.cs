// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using BinaryOpNode = Microsoft.PowerFx.Core.IR.Nodes.BinaryOpNode;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;
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
            else if (callerReturnType is TableType tableReturnType)
            {
                tableType = (TableType)callerReturnType;
            }
            else
            {
                tableType = context.CallerTableRetVal.TableType;
            }

            IntermediateNode binaryOpNodeLeft = node.Left;
            IntermediateNode binaryOpNodeRight = node.Right;

            if (!TryGetFieldNameOrRelationField(context, binaryOpNodeLeft, binaryOpNodeRight, node.Op, out var columnInfo, out IntermediateNode rightNode, out BinaryOpKind operation, out IList<string> relations, out var fieldFunction))
            {
                // If we can't get the field name, probably there is a field function in the binary operation.
                ProcessSpecialCalls(context, node.Op, ref binaryOpNodeLeft, ref binaryOpNodeRight, out var newNode);

                if (newNode != null)
                {
                    return newNode.Accept(this, context);
                }

                if (!TryGetFieldNameOrRelationField(context, binaryOpNodeLeft, binaryOpNodeRight, node.Op, out columnInfo, out rightNode, out operation, out relations, out fieldFunction))
                {
                    return new RetVal(node);
                }
            }

            if (!IsRelationDelegationAllowed(tableType, relations))
            {
                return new RetVal(node);
            }

            var findThisRecord = ThisRecordIRVisitor.FindThisRecordUsage(caller, rightNode);
            if (findThisRecord != null)
            {
                return CreateThisRecordErrorAndReturn(node, findThisRecord);
            }

            var findBehaviorFunc = BehaviorIRVisitor.Find(rightNode);
            if (findBehaviorFunc != null)
            {
                return CreateBehaviorErrorAndReturn(node, findBehaviorFunc);
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
                var eqNode = _hooks.MakeEqCall(callerSourceTable, tableType, relations, fieldFunction, columnInfo.RealColumnName, operation, rightNode, callerScope);
                ret = CreateBinaryOpRetVal(context, node, eqNode);
            }
            else if (IsOpKindInequalityComparison(operation))
            {
                var neqNode = _hooks.MakeNeqCall(callerSourceTable, tableType, relations, fieldFunction, columnInfo.RealColumnName, operation, rightNode, callerScope);
                ret = CreateBinaryOpRetVal(context, node, neqNode);
            }
            else if (IsOpKindLessThanComparison(operation))
            {
                var ltNode = _hooks.MakeLtCall(callerSourceTable, tableType, relations, fieldFunction, columnInfo.RealColumnName, operation, rightNode, callerScope);
                ret = CreateBinaryOpRetVal(context, node, ltNode);
            }
            else if (IsOpKindLessThanEqualComparison(operation))
            {
                var leqNode = _hooks.MakeLeqCall(callerSourceTable, tableType, relations, fieldFunction, columnInfo.RealColumnName, operation, rightNode, callerScope);
                ret = CreateBinaryOpRetVal(context, node, leqNode);
            }
            else if (IsOpKindGreaterThanComparison(operation))
            {
                var gtNode = _hooks.MakeGtCall(callerSourceTable, tableType, relations, fieldFunction, columnInfo.RealColumnName, operation, rightNode, callerScope);
                ret = CreateBinaryOpRetVal(context, node, gtNode);
            }
            else if (IsOpKindGreaterThanEqalComparison(operation))
            {
                var geqNode = _hooks.MakeGeqCall(callerSourceTable, tableType, relations, fieldFunction, columnInfo.RealColumnName, operation, rightNode, callerScope);
                ret = CreateBinaryOpRetVal(context, node, geqNode);
            }
            else if (operation == BinaryOpKind.InText)
            {
                var inNode = _hooks.MakeInCall(callerSourceTable, tableType, relations, fieldFunction, columnInfo.RealColumnName, operation, rightNode, callerScope);
                ret = CreateBinaryOpRetVal(context, node, inNode);
            }
            else
            {
                ret = new RetVal(node);
            }

            return ret;
        }

        private void ProcessSpecialCalls(Context context, BinaryOpKind kind, ref IntermediateNode nodeLeft, ref IntermediateNode nodeRight, out IntermediateNode newNode)
        {
            // Let's just transform the left/right nodes and the rest of the code will validate if we can delegate and generate the delegated expression

            // Check for CallNode presence
            (CallNode call, bool isCallOnLeft, bool isCallOnRight) = nodeLeft is CallNode callLeft ? (callLeft, true, false) : nodeRight is CallNode callRight ? (callRight, false, true) : (null, false, false);

            newNode = null;

            if (call != null)
            {
                if (call.Function == BuiltinFunctionsCore.DateAdd)
                {
                    ProcessDateAdd(ref nodeLeft, ref nodeRight, call, isCallOnLeft, isCallOnRight);
                }

                if (call.Function == BuiltinFunctionsCore.DateDiff)
                {
                    ProcessDateDiff(ref nodeLeft, ref nodeRight, call, isCallOnLeft, isCallOnRight, context);
                }

                if (call.Function == BuiltinFunctionsCore.Year)
                {
                    if ((isCallOnLeft && nodeRight is CallNode rightCallNode && rightCallNode.Function == BuiltinFunctionsCore.Year) ||
                        (isCallOnRight && nodeLeft is CallNode leftCallNode && leftCallNode.Function == BuiltinFunctionsCore.Year))
                    {
                        return;
                    }

                    if ((call.Args[0] is UnaryOpNode unaryOpNode && unaryOpNode.Child is ScopeAccessNode) || call.Args[0] is ScopeAccessNode)
                    {
                        newNode = ProcessYear(call, isCallOnLeft ? nodeRight : nodeLeft, kind);
                    }
                    else
                    {
                        return;
                    }                    
                }
            }
        }

        private bool TryGetFieldNameOrRelationField(Context context, IntermediateNode left, IntermediateNode right, BinaryOpKind op, out FxColumnInfo fieldName, out IntermediateNode node, out BinaryOpKind opKind, out IList<string> relations, out IEnumerable<FieldFunction> fieldFunctions)
        {
            relations = null;

            // Either left or right is field (not both)
            return TryGetFieldName(context, left, right, op, out fieldName, out node, out opKind, out fieldFunctions) ||
                   TryGetRelationField(context, left, right, op, out fieldName, out relations, out node, out opKind, out fieldFunctions);
        }

        // Let's just transform the left/right nodes and the rest of the code will validate if we can delegate and generate the delegated expression
        private static void ProcessDateAdd(ref IntermediateNode nodeLeft, ref IntermediateNode nodeRight, CallNode call, bool isCallOnLeft, bool isCallOnRight)
        {
            IntermediateNode arg0 = call.Args[0];            // datetime
            IntermediateNode negArg1 = Negate(call.Args[1]); // -duration

            if (isCallOnLeft)
            {
                // DateAdd(datetime, duration, [unit]) Op Xyz
                //     datetime + duration Op Xyz
                //     datetime Op Xyz - duration
                // datetime Op DateAdd(Xyz, -duration, [unit])
                nodeLeft = arg0;
                nodeRight = call.Args.Count == 2
                            ? new CallNode(IRContext.NotInSource(nodeRight.IRContext.ResultType), BuiltinFunctionsCore.DateAdd, nodeRight, EnsureNumber(negArg1))
                            : new CallNode(IRContext.NotInSource(nodeRight.IRContext.ResultType), BuiltinFunctionsCore.DateAdd, nodeRight, EnsureNumber(negArg1), call.Args[2]);
            }

            if (isCallOnRight)
            {
                // Xyz Op DateAdd(datetime, duration, [unit])
                //     Xyz Op datetime + duration
                //     Xyz - duration Op datetime
                // DateAdd(Xyz, -duration, [unit]) Op datetime
                nodeLeft = call.Args.Count == 2
                           ? new CallNode(IRContext.NotInSource(nodeLeft.IRContext.ResultType), BuiltinFunctionsCore.DateAdd, nodeLeft, EnsureNumber(negArg1))
                           : new CallNode(IRContext.NotInSource(nodeLeft.IRContext.ResultType), BuiltinFunctionsCore.DateAdd, nodeLeft, EnsureNumber(negArg1), call.Args[2]);
                nodeRight = arg0;
            }
        }

        // Let's just transform the left/right nodes and the rest of the code will validate if we can delegate and generate the delegated expression
        private void ProcessDateDiff(ref IntermediateNode nodeLeft, ref IntermediateNode nodeRight, CallNode call, bool isCallOnLeft, bool isCallOnRight, Context context)
        {
            // DateDiff(start, end, [unit])
            IntermediateNode arg0 = call.Args[0];
            IntermediateNode arg1 = call.Args[1];

            if (TryGetFieldName(context, arg0, out _, out var invertCoercion, out var coercionKind, out var fieldFunctions) && fieldFunctions.IsNullOrEmpty())
            {
                // arg0 = datetime
                // arg1 = end

                if (isCallOnLeft)
                {
                    // DateDiff(datetime, end, [unit]) Op Xyz
                    //     end - datetime Op Xyz
                    //     end Op Xyz + datetime
                    //     end - Xyz Op datetime
                    // DateAdd(end, -Xyz, [unit]) Op datetime
                    nodeLeft = call.Args.Count == 2
                               ? new CallNode(IRContext.NotInSource(arg1.IRContext.ResultType), BuiltinFunctionsCore.DateAdd, arg1, EnsureNumber(Negate(nodeRight)))
                               : new CallNode(IRContext.NotInSource(arg1.IRContext.ResultType), BuiltinFunctionsCore.DateAdd, arg1, EnsureNumber(Negate(nodeRight)), call.Args[2]);
                    nodeRight = arg0;
                }

                if (isCallOnRight)
                {
                    // Xyz Op DateDiff(datetime, end, [unit])
                    //     Xyz Op end - datetime
                    //     datetime + Xyz Op end
                    //     datetime Op end - Xyz
                    // datetime Op DateAdd(end, -Xyz, [unit])
                    nodeRight = call.Args.Count == 2
                                ? new CallNode(IRContext.NotInSource(arg1.IRContext.ResultType), BuiltinFunctionsCore.DateAdd, arg1, EnsureNumber(Negate(nodeLeft)))
                                : new CallNode(IRContext.NotInSource(arg1.IRContext.ResultType), BuiltinFunctionsCore.DateAdd, arg1, EnsureNumber(Negate(nodeLeft)), call.Args[2]);
                    nodeLeft = arg0;
                }
            }
            else if (TryGetFieldName(context, arg1, out _, out invertCoercion, out coercionKind, out fieldFunctions) && fieldFunctions.IsNullOrEmpty())
            {
                // arg0 = start
                // arg1 = datetime

                if (isCallOnLeft)
                {
                    // DateDiff(start, datetime, [unit]) Op Xyz
                    //     datetime - start Op Xyz
                    //     datetime Op start + Xyz
                    // datetime Op DateAdd(start, Xyz, [unit])
                    nodeLeft = arg1;
                    nodeRight = call.Args.Count == 2
                                ? new CallNode(IRContext.NotInSource(arg0.IRContext.ResultType), BuiltinFunctionsCore.DateAdd, arg0, EnsureNumber(nodeRight))
                                : new CallNode(IRContext.NotInSource(arg0.IRContext.ResultType), BuiltinFunctionsCore.DateAdd, arg0, EnsureNumber(nodeRight), call.Args[2]);
                }

                if (isCallOnRight)
                {
                    // Xyz Op DateDiff(start, datetime, [unit])
                    //     Xyz Op datetime - start
                    //     Xyz + start Op datetime
                    // DateAdd(start, Xyz) Op datetime
                    nodeRight = arg1;
                    nodeLeft = call.Args.Count == 2
                               ? new CallNode(IRContext.NotInSource(arg0.IRContext.ResultType), BuiltinFunctionsCore.DateAdd, arg0, EnsureNumber(nodeLeft))
                               : new CallNode(IRContext.NotInSource(arg0.IRContext.ResultType), BuiltinFunctionsCore.DateAdd, arg0, EnsureNumber(nodeLeft), call.Args[2]);
                }
            }
        }

        private static IntermediateNode CreateAndCallNode(IntermediateNode yearNode, IntermediateNode fieldAccess)
        {
            // column >= Date(arg, 1, 1) && column < Date(arg + 1, 1, 1)
            return new CallNode(
                IRContext.NotInSource(FormulaType.Boolean),
                BuiltinFunctionsCore.And,
                new BinaryOpNode(IRContext.NotInSource(FormulaType.Boolean), BinaryOpKind.GeqDateTime, fieldAccess, DelegationUtility.CreateEarliestDateTime(yearNode)),
                new BinaryOpNode(IRContext.NotInSource(FormulaType.Boolean), BinaryOpKind.LtDateTime, fieldAccess, DelegationUtility.CreateLatestDateTime(yearNode)));
        }

        private static IntermediateNode CreateOrCallNode(IntermediateNode yearNode, IntermediateNode fieldAccess)
        {
            // column < Date(arg, 1, 1) || column >= Date(arg + 1, 1, 1)
            return new CallNode(
                IRContext.NotInSource(FormulaType.Boolean),
                BuiltinFunctionsCore.Or,
                new BinaryOpNode(IRContext.NotInSource(FormulaType.Boolean), BinaryOpKind.LtDateTime, fieldAccess, DelegationUtility.CreateEarliestDateTime(yearNode)),
                new BinaryOpNode(IRContext.NotInSource(FormulaType.Boolean), BinaryOpKind.GeqDateTime, fieldAccess, DelegationUtility.CreateLatestDateTime(yearNode)));
        }

        private static IntermediateNode CreateLtDateTimeBinaryNode(IntermediateNode yearNode, IntermediateNode fieldAccess)
        {
            // column < Date(arg, 1, 1)
            return new BinaryOpNode(IRContext.NotInSource(FormulaType.Boolean), BinaryOpKind.LtDateTime, fieldAccess, DelegationUtility.CreateEarliestDateTime(yearNode));
        }

        private static IntermediateNode CreateLeqDateTimeBinaryNode(IntermediateNode yearNode, IntermediateNode fieldAccess)
        {
            // column < Date(arg + 1, 1, 1)
            return new BinaryOpNode(IRContext.NotInSource(FormulaType.Boolean), BinaryOpKind.LtDateTime, fieldAccess, DelegationUtility.CreateLatestDateTime(yearNode));
        }

        private static IntermediateNode CreateGtDateTimeBinaryNode(IntermediateNode yearNode, IntermediateNode fieldAccess)
        {
            // column > Date(arg, 1, 1)
            return new BinaryOpNode(IRContext.NotInSource(FormulaType.Boolean), BinaryOpKind.GtDateTime, fieldAccess, DelegationUtility.CreateLatestDateTime(yearNode));
        }

        private static IntermediateNode CreateGeqDateTimeBinaryNode(IntermediateNode yearNode, IntermediateNode fieldAccess)
        {
            // column >= Date(arg, 1, 1)
            return new BinaryOpNode(IRContext.NotInSource(FormulaType.Boolean), BinaryOpKind.GeqDateTime, fieldAccess, DelegationUtility.CreateEarliestDateTime(yearNode));
        }

        /// <summary>
        /// Process a binary operator when Year() function is present.
        /// </summary>
        /// <param name="callNode">The Year() call node.</param>
        /// <param name="argNode">The oposite node from Year() call node.</param>
        /// <param name="kind">Binary operation kind.</param>
        /// <returns></returns>
        private IntermediateNode ProcessYear(CallNode callNode, IntermediateNode argNode, BinaryOpKind kind)
        {
            var callNodeArg = callNode.Args[0];

            switch (kind)
            {
                // Translates to column in between two dates
                case BinaryOpKind.EqDecimals:
                    return CreateAndCallNode(argNode, callNodeArg);

                // Translates to column is not in between two dates
                case BinaryOpKind.NeqDecimals:
                    return CreateOrCallNode(argNode, callNodeArg);

                // Translates to column is less than a date
                case BinaryOpKind.LtDecimals:
                    return CreateLtDateTimeBinaryNode(argNode, callNodeArg);

                // Translates to column is less than or equal to a date
                case BinaryOpKind.LeqDecimals:
                    return CreateLeqDateTimeBinaryNode(argNode, callNodeArg);

                // Translates to column is greater than a date
                case BinaryOpKind.GtDecimals:
                    return CreateGtDateTimeBinaryNode(argNode, callNodeArg);

                // Translates to column is greater than or equal to a date
                case BinaryOpKind.GeqDecimals:
                    return CreateGeqDateTimeBinaryNode(argNode, callNodeArg);

                default:
                    return null;
            }
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

        private static IntermediateNode EnsureNumber(IntermediateNode node)
        {
            if (node.IRContext.ResultType != FormulaType.Number &&
                node.IRContext.ResultType != FormulaType.Decimal)
            {
                throw new InvalidOperationException("Expecting decimal or number type");
            }

            if (node.IRContext.ResultType == FormulaType.Number)
            {
                return node;
            }

            return new CallNode(IRContext.NotInSource(FormulaType.Number), BuiltinFunctionsCore.Float, node);
        }

        private static IntermediateNode Negate(IntermediateNode node)
        {
            return node.IRContext.ResultType._type.Kind switch
            {
                // If existing negation exists, we optimize so that -(-x) => x
                DKind.Number => node is UnaryOpNode uon && uon.Child.IRContext.ResultType == node.IRContext.ResultType && uon.Op == UnaryOpKind.Negate ? uon.Child : new UnaryOpNode(node.IRContext, UnaryOpKind.Negate, node),
                DKind.Decimal => node is UnaryOpNode uon && uon.Child.IRContext.ResultType == node.IRContext.ResultType && uon.Op == UnaryOpKind.NegateDecimal ? uon.Child : new UnaryOpNode(node.IRContext, UnaryOpKind.NegateDecimal, node),

                DKind.Date => new UnaryOpNode(node.IRContext, UnaryOpKind.NegateDate, node),
                DKind.DateTime => new UnaryOpNode(node.IRContext, UnaryOpKind.NegateDateTime, node),
                DKind.DateTimeNoTimeZone => new UnaryOpNode(node.IRContext, UnaryOpKind.NegateDateTime, node),
                DKind.Time => new UnaryOpNode(node.IRContext, UnaryOpKind.NegateTime, node),

                _ => throw new InvalidOperationException($"Cannnot negate {node.IRContext.ResultType._type.Kind} kind")
            };
        }

        private bool TryGetRelationField(Context context, IntermediateNode left, IntermediateNode right, BinaryOpKind op, out FxColumnInfo fieldName, out IList<string> relations, out IntermediateNode node, out BinaryOpKind opKind, out IEnumerable<FieldFunction> fieldFunctions)
        {
            if (TryGetRelationField(context, left, out var leftField, out var leftRelation, out var invertCoercion, out var coercionKind, out fieldFunctions) &&
                !TryGetFieldName(context, right, out _, out _, out _, out _))
            {
                fieldName = leftField;
                relations = leftRelation;
                node = right;
                opKind = op;
                return true;
            }
            else if (TryGetRelationField(context, right, out var rightField, out var rightRelation, out invertCoercion, out coercionKind, out fieldFunctions) &&
                !TryGetFieldName(context, left, out _, out _, out _, out _))
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
            else if (TryGetRelationField(context, left, out var leftField2, out _, out _, out _, out fieldFunctions) &&
                TryGetRelationField(context, right, out var rightField2, out _, out _, out _, out fieldFunctions))
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
            fieldFunctions = default;
            return false;
        }
    }
}
