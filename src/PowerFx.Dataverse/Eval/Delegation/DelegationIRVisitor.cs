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
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Types;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;
using BinaryOpNode = Microsoft.PowerFx.Core.IR.Nodes.BinaryOpNode;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;
using Span = Microsoft.PowerFx.Syntax.Span;
using UnaryOpNode = Microsoft.PowerFx.Core.IR.Nodes.UnaryOpNode;

namespace Microsoft.PowerFx.Dataverse
{
    // Rewrite the tree to inject delegation.
    // If we encounter a dataverse table (something that should be delegated) during the walk, we either:
    // - successfully delegate, which means rewriting to a call an efficient DelegatedFunction,
    // - leave IR unchanged (don't delegate), but issue a warning.
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        internal static readonly JsonSerializerOptions _jsonSerializerDefaultOptions = new JsonSerializerOptions() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };

        // Ideally, this would just be in Dataverse.Eval nuget, but
        // Only Dataverse nuget has InternalsVisisble access to implement an IR walker.
        // So implement the walker in lower layer, and have callbacks into Dataverse.Eval layer as needed.
        private readonly DelegationHooks _hooks;

        private readonly int _maxRows;

        // For reporting delegation Warnings.
        private readonly ICollection<ExpressionError> _errors;

        public DelegationIRVisitor(DelegationHooks hooks, ICollection<ExpressionError> errors, int maxRow)
        {
            _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
            _errors = errors ?? throw new ArgumentNullException(nameof(errors));
            _maxRows = maxRow;
        }

        public override IntermediateNode Materialize(RetVal ret)
        {
            // if ret has no filter or count, then we can just return the original node.
            if (ret.IsDelegating && (ret.HasFilter || ret.HasTopCount || ret.HasOrderBy || ret.HasColumnMap))
            {
                var res = _hooks.MakeQueryExecutorCall(ret);
                return res;
            }

            return ret.OriginalNode;
        }

        public bool TryGetFieldName(Context context, IntermediateNode left, IntermediateNode right, BinaryOpKind op, out string fieldName, out IntermediateNode node, out BinaryOpKind opKind)
        {
            if (TryGetFieldName(context, left, out var leftField) && !TryGetFieldName(context, right, out _))
            {
                if (op == BinaryOpKind.InText && right.IRContext.ResultType == FormulaType.String &&
                                                  left.IRContext.ResultType == FormulaType.String)
                {
                    opKind = default;
                    node = default;
                    fieldName = default;
                    return false;
                }

                fieldName = leftField;
                node = right;
                opKind = op;
                return true;
            }
            else if (TryGetFieldName(context, right, out var rightField) && !TryGetFieldName(context, left, out _))
            {
                fieldName = rightField;
                node = left;

                if (op == BinaryOpKind.InText && right.IRContext.ResultType == FormulaType.String && 
                                                  left.IRContext.ResultType == FormulaType.String)
                {
                    opKind = op;
                    return true;
                }

                if (TryInvertLeftRight(op, out var invertedOp))
                {
                    opKind = invertedOp;
                    return true;
                }
                
                // will return false
            }
            else if (TryGetFieldName(context, left, out var leftField2) && TryGetFieldName(context, right, out var rightField2))
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

                    // will return false
                }
            }

            opKind = default;
            node = default;
            fieldName = default;
            return false;
        }

        public bool TryGetFieldName(Context context, IntermediateNode node, out string fieldName)
        {
            IntermediateNode maybeScopeAccessNode;

            // If the node had injected float coercion, then we need to pull scope access node from it.
            if (node is CallNode functionCall &&
                (functionCall.Function == BuiltinFunctionsCore.Float || functionCall.Function == BuiltinFunctionsCore.Value || functionCall.Function.Name == BuiltinFunctionsCore.IsBlank.Name) &&
                functionCall.Args.Count == 1)
            {
                maybeScopeAccessNode = functionCall.Args[0];
            }
            else if (node is UnaryOpNode unaryOp && AllowedCoercions(unaryOp))
            {
                maybeScopeAccessNode = unaryOp.Child;
            }
            else
            {
                maybeScopeAccessNode = node;
            }

            if (maybeScopeAccessNode is ScopeAccessNode scopeAccessNode)
            {
                if (scopeAccessNode.Value is ScopeAccessSymbol scopeAccessSymbol)
                {
                    var callerScope = context.CallerNode.Scope;
                    var callerId = callerScope.Id;
                    if (scopeAccessSymbol.Parent.Id == callerId)
                    {
                        fieldName = scopeAccessSymbol.Name;

                        if (fieldName == "Value" && ColumnMap.HasDistinct(context.CallerTableRetVal.ColumnMap))
                        {
                            fieldName = context.CallerTableRetVal.ColumnMap.Distinct;
                        }

                        return true;
                    }
                }
            }

            fieldName = default;
            return false;
        }

        internal static bool IsOpKindEqualityComparison(BinaryOpKind op)
        {
            return op == BinaryOpKind.EqBoolean ||
                op == BinaryOpKind.EqCurrency ||
                op == BinaryOpKind.EqDate ||
                op == BinaryOpKind.EqDateTime ||
                op == BinaryOpKind.EqDecimals ||
                op == BinaryOpKind.EqGuid ||
                op == BinaryOpKind.EqNumbers ||
                op == BinaryOpKind.EqText ||
                op == BinaryOpKind.EqTime ||
                op == BinaryOpKind.EqOptionSetValue ||
                op == BinaryOpKind.EqPolymorphic;
        }

        internal static bool IsOpKindInequalityComparison(BinaryOpKind op)
        {
            return op == BinaryOpKind.NeqBoolean ||
                op == BinaryOpKind.NeqCurrency ||
                op == BinaryOpKind.NeqDate ||
                op == BinaryOpKind.NeqDateTime ||
                op == BinaryOpKind.NeqDecimals ||
                op == BinaryOpKind.NeqGuid ||
                op == BinaryOpKind.NeqNumbers ||
                op == BinaryOpKind.NeqText ||
                op == BinaryOpKind.NeqTime ||
                op == BinaryOpKind.NeqOptionSetValue ||
                op == BinaryOpKind.NeqPolymorphic;
        }

        internal static bool IsOpKindLessThanComparison(BinaryOpKind op)
        {
            return op == BinaryOpKind.LtNumbers ||
                op == BinaryOpKind.LtDecimals ||
                op == BinaryOpKind.LtDateTime ||
                op == BinaryOpKind.LtDate ||
                op == BinaryOpKind.LtTime;
        }

        internal static bool IsOpKindLessThanEqualComparison(BinaryOpKind op)
        {
            return op == BinaryOpKind.LeqNumbers ||
                op == BinaryOpKind.LeqDecimals ||
                op == BinaryOpKind.LeqDateTime ||
                op == BinaryOpKind.LeqDate ||
                op == BinaryOpKind.LeqTime;
        }

        internal static bool IsOpKindGreaterThanComparison(BinaryOpKind op)
        {
            return op == BinaryOpKind.GtNumbers ||
                op == BinaryOpKind.GtDecimals ||
                op == BinaryOpKind.GtDateTime ||
                op == BinaryOpKind.GtDate ||
                op == BinaryOpKind.GtTime;
        }

        internal static bool IsOpKindGreaterThanEqalComparison(BinaryOpKind op)
        {
            return op == BinaryOpKind.GeqNumbers ||
                op == BinaryOpKind.GeqDecimals ||
                op == BinaryOpKind.GeqDateTime ||
                op == BinaryOpKind.GeqDate ||
                op == BinaryOpKind.GeqTime;
        }

        protected override RetVal Ret(IntermediateNode node)
        {
            return new RetVal(node);
        }

        private static bool TryGetEntityName(FormulaType type, out string entityName)
        {
            var ads = type._type.AssociatedDataSources.FirstOrDefault();
            if (ads != null)
            {
                entityName = ads.EntityName.Value;
                return true;
            }

            entityName = default;
            return false;
        }

        private static bool AllowedCoercions(UnaryOpNode unaryOp) =>
            unaryOp.Op switch
            {
                UnaryOpKind.DateTimeToTime => true,
                UnaryOpKind.DateToTime => true,
                UnaryOpKind.TimeToDate => true,
                UnaryOpKind.DateTimeToDate => true,
                UnaryOpKind.TimeToDateTime => true,
                UnaryOpKind.DateToDateTime => true,
                _ => false
            };

        // If an attempted delegation can't be complete, then fail it.
        private void AddError(ExpressionError error)
        {
            _errors.Add(error);
        }

        private RetVal MaterializeTableAndAddWarning(RetVal tableArg, CallNode node)
        {
            var tableCallNode = Materialize(tableArg);
            var args = new List<IntermediateNode>() { tableCallNode, node.Args[1] };
            var newCall = _hooks.MakeCallNode(node.Function, node.IRContext, args, node.Scope);

            return CreateNotSupportedErrorAndReturn(newCall, tableArg);
        }

        private RetVal CreateBinaryOpRetVal(Context context, IntermediateNode node, IntermediateNode eqNode)
        {
            var callerTable = context.CallerTableNode;
            var callerTableReturnType = callerTable.IRContext.ResultType as TableType ?? throw new InvalidOperationException("CallerTable ReturnType should always be TableType");

            return new RetVal(_hooks, node, callerTable, callerTableReturnType, eqNode, orderBy: null, count: null, _maxRows, columnMap: null);
        }

        private RetVal CreateNotSupportedErrorAndReturn(CallNode node, RetVal tableArg)
        {
            if (tableArg == null || !tableArg.TryGetLogicalName(out var tableName))
            {
                tableName = "table"; // some default
            }

            var reason = new ExpressionError()
            {
                MessageArgs = new object[] { tableName, _maxRows },
                Span = tableArg?._sourceTableIRNode.IRContext.SourceContext ?? new Span(1, 2),
                Severity = ErrorSeverity.Warning,
                ResourceKey = TexlStrings.WrnDelegationTableNotSupported
            };

            this.AddError(reason);

            return new RetVal(node);
        }

        private RetVal CreateBehaviorErrorAndReturn(CallNode node, BehaviorIRVisitor.RetVal findBehaviorFunc)
        {
            var reason = new ExpressionError()
            {
                MessageArgs = new object[] { node.Function.Name, findBehaviorFunc.Name },
                Span = findBehaviorFunc.Span,
                Severity = ErrorSeverity.Warning,
                ResourceKey = TexlStrings.WrnDelegationBehaviorFunction
            };

            AddError(reason);
            return new RetVal(node);
        }

        private RetVal CreateBehaviorErrorAndReturn(BinaryOpNode node, BehaviorIRVisitor.RetVal findBehaviorFunc)
        {
            var reason = new ExpressionError()
            {
                MessageArgs = new object[] { findBehaviorFunc.Name },
                Span = node.IRContext.SourceContext,
                Severity = ErrorSeverity.Warning,
                ResourceKey = TexlStrings.WrnDelegationBehaviorFunction
            };

            AddError(reason);
            return new RetVal(node);
        }

        private RetVal CreateThisRecordErrorAndReturn(CallNode node, ThisRecordIRVisitor.RetVal findThisRecord)
        {
            var reason = new ExpressionError()
            {
                MessageArgs = new object[] { node.Function.Name },
                Span = findThisRecord.Span,
                Severity = ErrorSeverity.Warning,
                ResourceKey = TexlStrings.WrnDelegationRefersThisRecord
            };
            AddError(reason);
            return new RetVal(node);
        }

        private RetVal CreateThisRecordErrorAndReturn(BinaryOpNode node, ThisRecordIRVisitor.RetVal findThisRecord)
        {
            var reason = new ExpressionError()
            {
                MessageArgs = new object[] { node.Op.ToString() },
                Span = node.IRContext.SourceContext,
                Severity = ErrorSeverity.Warning,
                ResourceKey = TexlStrings.WrnDelegationRefersThisRecord
            };
            AddError(reason);
            return new RetVal(node);
        }

        private bool TryGetScopedVariable(Stack<IDictionary<string, RetVal>> withScopes, string variable, out RetVal node)
        {
            if (withScopes.Count() == 0)
            {
                node = default;
                return false;
            }

            foreach (var kv in withScopes)
            {
                if (kv.TryGetValue(variable, out node))
                {
                    return true;
                }
            }

            node = default;
            return false;
        }

        private bool IsTableArgLookUpDelegable(Context context, RetVal tableArg)
        {
            if (tableArg.OriginalNode is ResolvedObjectNode
                || (tableArg._sourceTableIRNode.InnerNode is ScopeAccessNode scopedTableArg
                    && scopedTableArg.Value is ScopeAccessSymbol scopedSymbol
                    && TryGetScopedVariable(context.WithScopes, scopedSymbol.Name, out var scopedNode)
                    && scopedNode.OriginalNode is ResolvedObjectNode)
                || (tableArg.IsDelegating && tableArg.OriginalNode is CallNode callNode
                    && (callNode.Function.Name == "ShowColumns" ||
                        callNode.Function.Name == "Distinct" ||
                        callNode.Function.Name == "Sort" ||
                        callNode.Function.Name == "SortByColumns")))
            {
                return true;
            }

            return false;
        }

        private bool TryGetRelationField(Context context, IntermediateNode node, out string fieldName, out IList<string> relations)
        {
            relations = new List<string>();

            IntermediateNode maybeFieldAccessNode;

            // If the node had injected float coercion, then we need to pull field access node from it.
            if (node is CallNode functionCall &&
                (functionCall.Function == BuiltinFunctionsCore.Float || functionCall.Function == BuiltinFunctionsCore.Value || functionCall.Function.Name == BuiltinFunctionsCore.IsBlank.Name) &&
                functionCall.Args.Count == 1)
            {
                maybeFieldAccessNode = functionCall.Args[0];
            }
            else
            {
                maybeFieldAccessNode = node;
            }

            if (maybeFieldAccessNode is RecordFieldAccessNode fieldAccess)
            {
                fieldName = fieldAccess.Field;
                if (TryGetFieldName(context, fieldAccess.From, out var fromField))
                {
                    // fetch the primary key name on relation here. If its 1 depth relation, then we can delegate without fetching the related record.
                    if (relations.Count == 0)
                    {
                        context.CallerTableRetVal.TableType.TryGetFieldType(fromField, out var fromFieldType);
                        if (fromFieldType is RecordType fromFieldRelation &&
                            fromFieldRelation.TryGetPrimaryKeyFieldName(out var primaryKeyFieldName) && 
                            fieldName == primaryKeyFieldName)
                        {
                            if (context.CallerTableRetVal.Metadata.TryGetManyToOneRelationship(fromField, out var relation2))
                            {
                                fieldName = relation2.ReferencingAttribute;
                            }
                            else
                            {
                                fieldName = fromField;
                            }

                            relations = null;
                            return true;
                        }
                    }

                    string targetEntityName = null;
                    if (context.CallerTableRetVal.Metadata.TryGetManyToOneRelationship(fromField, out var relation))
                    {
                        targetEntityName = relation.ReferencedEntity;
                    }

                    var relationMetadata = new RelationMetadata(fromField, false, null);

                    var serializedRelationMetadata = DelegationUtility.SerializeRelationMetadata(relationMetadata);
                    relations.Add(serializedRelationMetadata);
                    return true;
                }
                else if (fieldAccess.From is CallNode callNode && callNode.Function.Name == BuiltinFunctionsCore.AsType.Name)
                {
                    if (TryGetEntityName(callNode.Args[1].IRContext.ResultType, out var targetEntityName) && TryGetEntityName(context.CallerTableNode.IRContext.ResultType, out _))
                    {
                        TryGetFieldName(context, callNode.Args[0], out fromField);
                        AttributeUtility.TryGetLogicalNameFromOdataName(fromField, out var logicalName);
                        var relationMetadata = new RelationMetadata(logicalName, true, targetEntityName);

                        var serializedRelationMetadata = DelegationUtility.SerializeRelationMetadata(relationMetadata);
                        relations.Add(serializedRelationMetadata);
                        return true;
                    }
                }
            }

            fieldName = default;
            return false;
        }

        private bool TryInvertLeftRight(BinaryOpKind op, out BinaryOpKind invertedOp)
        {
            switch (op)
            {
                case BinaryOpKind.LtNumbers:
                    invertedOp = BinaryOpKind.GtNumbers;
                    return true;

                case BinaryOpKind.LtDecimals:
                    invertedOp = BinaryOpKind.GtDecimals;
                    return true;

                case BinaryOpKind.LtDateTime:
                    invertedOp = BinaryOpKind.GtDateTime;
                    return true;

                case BinaryOpKind.LtDate:
                    invertedOp = BinaryOpKind.GtDate;
                    return true;

                case BinaryOpKind.LtTime:
                    invertedOp = BinaryOpKind.GtTime;
                    return true;

                case BinaryOpKind.LeqNumbers:
                    invertedOp = BinaryOpKind.GeqNumbers;
                    return true;

                case BinaryOpKind.LeqDecimals:
                    invertedOp = BinaryOpKind.GeqDecimals;
                    return true;

                case BinaryOpKind.LeqDateTime:
                    invertedOp = BinaryOpKind.GeqDateTime;
                    return true;

                case BinaryOpKind.LeqDate:
                    invertedOp = BinaryOpKind.GeqDate;
                    return true;

                case BinaryOpKind.LeqTime:
                    invertedOp = BinaryOpKind.GeqTime;
                    return true;

                case BinaryOpKind.GtNumbers:
                    invertedOp = BinaryOpKind.LtNumbers;
                    return true;

                case BinaryOpKind.GtDecimals:
                    invertedOp = BinaryOpKind.LtDecimals;
                    return true;

                case BinaryOpKind.GtDateTime:
                    invertedOp = BinaryOpKind.LtDateTime;
                    return true;

                case BinaryOpKind.GtDate:
                    invertedOp = BinaryOpKind.LtDate;
                    return true;

                case BinaryOpKind.GtTime:
                    invertedOp = BinaryOpKind.LtTime;
                    return true;

                case BinaryOpKind.GeqNumbers:
                    invertedOp = BinaryOpKind.LeqNumbers;
                    return true;

                case BinaryOpKind.GeqDecimals:
                    invertedOp = BinaryOpKind.LeqDecimals;
                    return true;

                case BinaryOpKind.GeqDateTime:
                    invertedOp = BinaryOpKind.LeqDateTime;
                    return true;

                case BinaryOpKind.GeqDate:
                    invertedOp = BinaryOpKind.LeqDate;
                    return true;

                case BinaryOpKind.GeqTime:
                    invertedOp = BinaryOpKind.LeqTime;
                    return true;

                default:
                    invertedOp = default;
                    return false;
            }
        }
    }
}
