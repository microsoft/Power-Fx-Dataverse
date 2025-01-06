// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.Functions.Delegation.DelegationMetadata;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Types.Enums;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Types;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;
using RecordNode = Microsoft.PowerFx.Core.IR.Nodes.RecordNode;

namespace Microsoft.PowerFx.Dataverse
{
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        private const int LeftTableArg = 0;
        private const int RightTableArg = 1;
        private const int PredicateArg = 2;
        private const int JoinTypeArg = 3;
        private const int LeftRightAliasArg = 4;
        private const int LeftTableColumnArg = 5;
        private const int RightTableColumnArg = 6;

        // Join function
        // arg 0 - Left Table
        // arg 1 - Right Table
        // arg 2 - Predicate
        // arg 3 - Join type
        // arg 4 - Left/RightRecord names
        // arg 5 - Left Table column renames
        // arg 6 - Right Table column renames
        private RetVal ProcessJoin(CallNode node, Context context)
        {
            RetVal leftTable = GetTable(node.Args[LeftTableArg], context);
            RetVal rightTable = GetTable(node.Args[RightTableArg], context);

            // both tables need to support delegation
            // if they already have $top, $filter, $orderby..., let's not delegate
            if (!leftTable.IsDelegating || !rightTable.IsDelegating ||
                leftTable.HasTopCount || rightTable.HasTopCount ||
                leftTable.HasOrderBy || rightTable.HasOrderBy ||
                leftTable.HasFilter || rightTable.HasFilter ||
                leftTable.HasJoin || rightTable.HasJoin ||
                leftTable.HasGroupBy || rightTable.HasGroupBy ||
                leftTable.ColumnMap != null || rightTable.ColumnMap != null)
            {
                return base.Visit(node, context, leftTable);
            }

            // Get primary keys
            var leftPrimaryKeys = leftTable.TableType.GetPrimaryKeyNames();
            var rightPrimaryKeys = rightTable.TableType.GetPrimaryKeyNames();

            string joinType = null;

            // Get Join type
            if (node.Args[JoinTypeArg] is RecordFieldAccessNode rfan && rfan.From is ResolvedObjectNode ron && ron.Value is EnumSymbol es && es.EntityName == LanguageConstants.JoinTypeEnumString)
            {
                joinType = rfan.Field.Value;
            }
            else
            {
                throw new InvalidOperationException($"Unknown JoinType");
            }

            string leftRecordName = null;
            string rightRecordName = null;

            if (node.Args[LeftRightAliasArg] is RecordNode lrrn &&
                lrrn.Fields[FunctionJoinScopeInfo.LeftRecord] is TextLiteralNode ltln &&
                lrrn.Fields[FunctionJoinScopeInfo.RightRecord] is TextLiteralNode rtln)
            {
                leftRecordName = ltln.LiteralValue;
                rightRecordName = rtln.LiteralValue;
            }

            TableType joinReturnType = node.IRContext.ResultType as TableType;

            // Will delegate only if
            // - equality comparison
            // - between simple Left/Right record fields
            // - primary key is used and not composed
            if (!string.IsNullOrEmpty(joinType) &&
                node.Args[PredicateArg] is LazyEvalNode len && len.Child is BinaryOpNode bon &&
                IsOpKindEqualityComparison(bon.Op) &&
                bon.Left is RecordFieldAccessNode leftrfan && leftrfan.From is ScopeAccessNode leftsan && leftsan.Value is ScopeAccessSymbol leftsas &&
                bon.Right is RecordFieldAccessNode rightrfan && rightrfan.From is ScopeAccessNode rightsan && rightsan.Value is ScopeAccessSymbol rightsas)
            {
                string leftField = leftrfan.Field.Value;
                string rightField = rightrfan.Field.Value;

                string toAttribute = null;
                string fromAttribute = null;

                if (leftsas.Name.Value == leftRecordName && rightsas.Name.Value == rightRecordName)
                {
                    if ((rightPrimaryKeys.Count() == 1 && rightField == rightPrimaryKeys.First()) ||
                        (leftPrimaryKeys.Count() == 1 && leftField == leftPrimaryKeys.First()))
                    {
                        toAttribute = rightField;
                        fromAttribute = leftField;
                    }
                }

                if (leftsas.Name.Value == rightRecordName && rightsas.Name.Value == leftRecordName)
                {
                    if ((rightPrimaryKeys.Count() == 1 && leftField == rightPrimaryKeys.First()) ||
                        (leftPrimaryKeys.Count() == 1 && rightField == leftPrimaryKeys.First()))
                    {
                        toAttribute = leftField;
                        fromAttribute = rightField;
                    }
                }

                if (!string.IsNullOrEmpty(toAttribute) && !string.IsNullOrEmpty(fromAttribute))
                {
                    // list of column renames from left table, if any
                    // by default, all columns from left table are included
                    ColumnMap leftMap = new ColumnMap((node.Args[LeftTableColumnArg] as RecordNode).Fields.ToDictionary(f => new DName((f.Value as TextLiteralNode).LiteralValue), f => new TextLiteralNode(IRContext.NotInSource(FormulaType.String), f.Key.Value)));

                    // list of column renamed from right table
                    // we use 'entityAlias.' prefix for columns as DV will return it for right table columns we have selected
                    ColumnMap rightMap = new ColumnMap((node.Args[RightTableColumnArg] as RecordNode).Fields.ToDictionary(f => new DName((f.Value as TextLiteralNode).LiteralValue), f => new TextLiteralNode(IRContext.NotInSource(FormulaType.String), f.Key.Value)));

                    // Join node with all parameters
                    FxJoinNode joinNode = new FxJoinNode(
                        leftTable.TableType.TableSymbolName,
                        rightTable.TableType.TableSymbolName,
                        fromAttribute,
                        toAttribute,
                        joinType,
                        rightRecordName, // foreign table alias                        
                        rightMap);

                    return leftTable.With(node, tableType: joinReturnType, join: joinNode, map: leftMap);
                }
            }

            return ProcessOtherCall(node, leftTable, rightTable, context);
        }
    }
}
