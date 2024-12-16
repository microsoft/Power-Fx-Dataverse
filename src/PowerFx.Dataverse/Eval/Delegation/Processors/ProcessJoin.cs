// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Core.Types.Enums;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Types;
using Microsoft.Rest.Serialization;
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
                    // Aliasing entity is important to avoid collision
                    // Add '_J' suffix to identify relation as coming from JOIN relationship
                    string entityAlias = $"{rightTable.TableType.TableSymbolName}_{Guid.NewGuid().ToString("N")}{DelegationEngineExtensions.LinkEntityJoinSuffix}";

                    // list of column renames from left table, if any
                    // by default, all columns from left table are included
                    ColumnMap leftMap = new ColumnMap((node.Args[LeftTableColumnArg] as RecordNode).Fields.ToDictionary(f => new DName((f.Value as TextLiteralNode).LiteralValue), f => GetColumnName(f.Key, null)));

                    // list of column renamed from right table
                    // we use 'entityAlias.' prefix for columns as DV will return it for right table columns we have selected
                    ColumnMap rightMap = new ColumnMap((node.Args[RightTableColumnArg] as RecordNode).Fields.ToDictionary(f => new DName((f.Value as TextLiteralNode).LiteralValue), f => GetColumnName(f.Key, entityAlias)));

                    // get column names to be renamed with their types
                    RecordNode rightColumns = GetColumnsWithTypes(node.Args[RightTableColumnArg] as RecordNode, rightTable);

                    // cumulate left and right maps
                    ColumnMap mergedMap = ColumnMap.Merge(leftMap, rightMap);

                    // Join recordNode to transport all parameters we need 
                    RecordNode joinNode = GetJoinRecordNode(leftTable.TableType.TableSymbolName, rightTable.TableType.TableSymbolName, fromAttribute, toAttribute, joinType, entityAlias, rightColumns);
                    
                    return leftTable.With(node, tableType: joinReturnType, join: joinNode, map: mergedMap);
                }
            }

            return ProcessOtherCall(node, leftTable, rightTable, context);
        }

        // Gets a RecordNode with types
        private static RecordNode GetColumnsWithTypes(RecordNode node, RetVal table)
        {
            RecordType recordType = RecordType.Empty();
            Dictionary<DName, IntermediateNode> rrn = new Dictionary<DName, IntermediateNode>();
            foreach (KeyValuePair<DName, IntermediateNode> rf in node.Fields)
            {
                string name = rf.Key.Value;

                if (table.TableType.TryGetFieldType(name, out FormulaType fType))
                {
                    recordType = recordType.Add(name, fType);
                    rrn.Add(rf.Key, new CallNode(IRContext.NotInSource(fType), BuiltinFunctionsCore.Blank));
                }
            }

            return new RecordNode(IRContext.NotInSource(recordType), rrn);
        }

        private static TextLiteralNode GetColumnName(DName name, string alias)
        {
            if (string.IsNullOrEmpty(alias))
            {
                return new TextLiteralNode(IRContext.NotInSource(FormulaType.String), name.Value);
            }

            return new TextLiteralNode(IRContext.NotInSource(FormulaType.String), $"{alias}.{name.Value}");
        }

        private static RecordNode GetJoinRecordNode(string sourceTable, string foreignTable, string fromAttribute, string toAttribute, string joinType, string entityAlias, RecordNode foreignTableColumns)
        {
            RecordType rt = RecordType.Empty();
            Dictionary<DName, IntermediateNode> dic = new Dictionary<DName, IntermediateNode>();

            rt = rt.Add("sourceTable", FormulaType.String);
            dic[new DName("sourceTable")] = new TextLiteralNode(IRContext.NotInSource(FormulaType.String), sourceTable);

            rt = rt.Add("foreignTable", FormulaType.String);
            dic[new DName("foreignTable")] = new TextLiteralNode(IRContext.NotInSource(FormulaType.String), foreignTable);

            rt = rt.Add("fromAttribute", FormulaType.String);
            dic[new DName("fromAttribute")] = new TextLiteralNode(IRContext.NotInSource(FormulaType.String), fromAttribute);

            rt = rt.Add("toAttribute", FormulaType.String);
            dic[new DName("toAttribute")] = new TextLiteralNode(IRContext.NotInSource(FormulaType.String), toAttribute);

            rt = rt.Add("joinType", FormulaType.String);
            dic[new DName("joinType")] = new TextLiteralNode(IRContext.NotInSource(FormulaType.String), joinType);

            rt = rt.Add("entityAlias", FormulaType.String);
            dic[new DName("entityAlias")] = new TextLiteralNode(IRContext.NotInSource(FormulaType.String), entityAlias);

            rt = rt.Add("foreignTableColumns", foreignTableColumns.IRContext.ResultType);
            dic[new DName("foreignTableColumns")] = foreignTableColumns;

            return new RecordNode(IRContext.NotInSource(rt), dic);
        }
    }
}
