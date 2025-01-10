// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Texl.Builtins;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Capture Dataverse field-level reads and writes within a formula.
    /// </summary>
    public class DependencyInfo
    {
        /// <summary>
        /// A dictionary of field logical names on related records, indexed by the related entity logical name.
        /// </summary>
        /// <example>
        /// On account, the formula "Name & 'Primary Contact'.'Full Name'" would return
        ///    "contact" => { "fullname" }
        /// The formula "Name & 'Primary Contact'.'Full Name' & Sum(Contacts, 'Number Of Childeren')" would return
        ///    "contact" => { "fullname", "numberofchildren" }.
        /// </example>
        public Dictionary<string, HashSet<string>> FieldReads { get; set; }

        public Dictionary<string, HashSet<string>> FieldWrites { get; set; }

        public bool HasWrites => FieldWrites != null && FieldWrites.Count > 0;

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            DumpHelper(sb, "Read", FieldReads);
            DumpHelper(sb, "Write", FieldWrites);

            return sb.ToString();
        }

        private static void DumpHelper(StringBuilder sb, string kind, Dictionary<string, HashSet<string>> dict)
        {
            if (dict != null)
            {
                foreach (var kv in dict)
                {
                    sb.Append(kind);
                    sb.Append(" ");
                    sb.Append(kv.Key);
                    sb.Append(": ");

                    bool first = true;
                    foreach (var x in kv.Value)
                    {
                        if (!first)
                        {
                            sb.Append(", ");
                        }

                        first = false;
                        sb.Append(x);
                    }

                    sb.AppendLine("; ");
                }
            }
        }

        public static DependencyInfo Scan(CheckResult check, CdsEntityMetadataProvider metadataCache)
        {
            var ir = check.ApplyIR(); //throws on errors

            var ctx = new DependencyVisitor.Context();
            var v = new DependencyVisitor(metadataCache);

            ir.TopNode.Accept(v, ctx);

            return v.Info;
        }
    }

    // IR has already:
    // - resolved everything to logical names.
    // - resolved implicit ThisRecord
    internal class DependencyVisitor : IRNodeVisitor<DependencyVisitor.RetVal, DependencyVisitor.Context>
    {
        // IR is already in logical names, but needed for resolving relationship names back to attribute names.
        private readonly CdsEntityMetadataProvider _metadataCache;

        // Track reults.
        public DependencyInfo Info { get; private set; } = new DependencyInfo();

        public DependencyVisitor(CdsEntityMetadataProvider metadataCache)
        {
            _metadataCache = metadataCache ?? throw new ArgumentNullException(nameof(metadataCache));
        }

        public override RetVal Visit(TextLiteralNode node, Context context)
        {
            return null;
        }

        public override RetVal Visit(NumberLiteralNode node, Context context)
        {
            return null;
        }

        public override RetVal Visit(BooleanLiteralNode node, Context context)
        {
            return null;
        }

        public override RetVal Visit(DecimalLiteralNode node, Context context)
        {
            return null;
        }

        public override RetVal Visit(ColorLiteralNode node, Context context)
        {
            return null;
        }

        public override RetVal Visit(RecordNode node, Context context)
        {
            foreach (var kv in node.Fields)
            {
                kv.Value.Accept(this, context);
            }

            return null;
        }

        public override RetVal Visit(ErrorNode node, Context context)
        {
            return null;
        }

        public override RetVal Visit(LazyEvalNode node, Context context)
        {
            return node.Child.Accept(this, context);
        }

        private readonly Dictionary<int, FormulaType> _scopeTypes = new Dictionary<int, FormulaType>();

        public override RetVal Visit(CallNode node, Context context)
        {
            // Scope is created against type of arg0
            if (node.Scope != null)
            {
                var arg0 = node.Args[0];
                _scopeTypes[node.Scope.Id] = arg0.IRContext.ResultType;
            }

            // If arg0 is a write-only arg, then skip it for reading.
            bool firstArgIsWrite = false;

            // Special casing Delegation runtime helper added during IR Rewriting.
            if (node.Function is DelegatedOperatorFunction)
            {
                string tableLogicalName = null;
                if (node.Args[0].IRContext.ResultType is AggregateType aggType)
                {
                    tableLogicalName = aggType.TableSymbolName;
                }
                else
                {
                    throw new InvalidOperationException($"{nameof(DelegatedOperatorFunction)} IR helper must have first argument an aggregate type");
                }

                // Relationship case
                if (node.Args.Count > 3)
                {
                    if (node.Args[3] is CallNode maybeTableArg && maybeTableArg.Function is TableFunction tableArg && maybeTableArg.Args.Count == 1)
                    {
                        var arg0 = (RecordNode)maybeTableArg.Args[0];
                        if (arg0.Fields.TryGetValue(FieldInfoRecord.SingleColumnTableColumnDName, out var valueNode) && valueNode is TextLiteralNode textNode)
                        {
                            var relationshipObj = DelegationUtility.DeserializeRelatioMetadata(textNode.LiteralValue);
                            AddFieldRead(tableLogicalName, relationshipObj.ReferencingFieldName);

                            var referencedEntityName = relationshipObj.ReferencedEntityName;

                            // referencedEntityName is null for non polymorphic relationships.
                            if (referencedEntityName == null)
                            {
                                if (_metadataCache.TryGetXrmEntityMetadata(tableLogicalName, out var entityMetadata) &&
                                    entityMetadata.TryGetManyToOneRelationship(relationshipObj.ReferencingFieldName, out var relation))
                                {
                                    referencedEntityName = relation.ReferencedEntity;
                                }
                                else
                                {
                                    throw new InvalidOperationException(referencedEntityName + " is null and can't be resolved");
                                }
                            }

                            // Arg1 is the field FieldInfoRecord being compared on the target entity.
                            var infoRecord = (RecordNode)node.Args[1];

                            var fieldName = ((TextLiteralNode)infoRecord.Fields.First(field => field.Key.Value == FieldInfoRecord.FieldNameColumnName).Value).LiteralValue;
                            AddFieldRead(referencedEntityName, fieldName);
                        }
                        else
                        {
                            throw new InvalidOperationException($"{nameof(DelegatedOperatorFunction)} IR helper must have fourth argument a table function with a single argument of type record with a field named Value");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"{nameof(DelegatedOperatorFunction)} IR helper must have fourth argument a table function");
                    }
                }
                else
                {
                    if (node.Args[1] is RecordNode fieldInfoRecord
                        && fieldInfoRecord.Fields.TryGetValue(new Core.Utils.DName(FieldInfoRecord.FieldNameColumnName), out var maybeField)
                        && maybeField is TextLiteralNode field)
                    {
                        string fieldName = field.LiteralValue;

                        if (context.ColumnMap != null && context.ColumnMap.TryGetColumnInfo(fieldName, out var fieldInfo))
                        {
                            fieldName = fieldInfo.RealColumnName;
                        }

                        AddFieldRead(tableLogicalName, fieldName);
                    }
                    else
                    {
                        throw new InvalidOperationException($"{nameof(DelegatedOperatorFunction)} IR helper must have second argument a field Info record");
                    }
                }
            }

            // Patch, Collect
            // Set
            var func = node.Function.Name;

            if (func == "Summarize")
            {
                var tableName = ((AggregateType)node.Args[0].IRContext.ResultType).TableSymbolName;
                foreach (var arg in node.Args.Skip(1))
                {
                    if (arg is TextLiteralNode columnNode)
                    {
                        AddFieldRead(tableName, columnNode.LiteralValue);
                    }
                    else
                    {
                        arg.Accept(this, context);
                    }
                }

                return null;
            }

            if (func == "Set")
            {
                firstArgIsWrite = true;

                // Set(field, ...);
                var arg0 = node.Args[0];
                if (arg0 is ResolvedObjectNode r)
                {
                    var obj = r.Value;
                    if (obj is NameSymbol sym)
                    {
                        if (sym.Owner is SymbolTableOverRecordType symTable)
                        {
                            RecordType type = symTable.Type;
                            var tableLogicalName = type.TableSymbolName;

                            // on current table
                            var fieldLogicalName = sym.Name;

                            AddFieldWrite(tableLogicalName, fieldLogicalName);
                        }
                    }
                }
                else if (arg0 is RecordFieldAccessNode recordFieldAccessNode)
                {
                    recordFieldAccessNode.From.Accept(this, context);
                    var ltype = recordFieldAccessNode.From.IRContext.ResultType;
                    if (ltype is RecordType ltypeRecord)
                    {
                        // Logical name of the table on left side.
                        // This will be null for non-dataverse records
                        var tableLogicalName = ltypeRecord.TableSymbolName;

                        if (tableLogicalName != null)
                        {
                            var fieldLogicalName = recordFieldAccessNode.Field.Value;
                            AddFieldWrite(tableLogicalName, fieldLogicalName);
                        }
                    }
                }
            }

            int argRecordWrite = 0;
            if (func == "Patch")
            {
                // Patch(table, record, fields);
                firstArgIsWrite = true;
                argRecordWrite = 2;
                if (node.Args.Count != 3)
                {
                    throw new NotSupportedException($"Can't analyze Patch overload: {node}");
                }
            }
            else if (func == "Collect")
            {
                // Collect(table, fields);
                firstArgIsWrite = true;
                argRecordWrite = 1;
                if (node.Args.Count != 2)
                {
                    throw new NotSupportedException($"Can't analyze Collect overload: {node}");
                }
            }
            else if (func == "ClearCollect")
            {
                // ClearCollect(table, fields);
                firstArgIsWrite = true;
                argRecordWrite = 1;
            }
            else if (func == "Remove")
            {
                // Remove(table, fields);
                firstArgIsWrite = true;
                argRecordWrite = 1;
            }
            else if (func == "Join")
            {                
                var remote = ((AggregateType)node.Args[1].IRContext.ResultType).TableSymbolName;

                // Right column fields
                if (node.Args[6] is RecordNode rn)
                {
                    foreach (KeyValuePair<DName, IntermediateNode> kvp in rn.Fields)
                    {                        
                        AddFieldRead(remote, kvp.Key.Value);                        
                    }
                }
            }

            if (argRecordWrite > 0)
            {
                // Patch(table, record, fields);
                var argTableType = node.Args[0];
                if (argTableType.IRContext.ResultType is TableType type)
                {
                    var tableLogicalName = type.TableSymbolName;

                    // Every field in the record is a field write.
                    var argWrites = node.Args[argRecordWrite];

                    IntermediateNode maybeRecordNode;
                    if (argWrites is AggregateCoercionNode aggregateCoercionNode)
                    {
                        maybeRecordNode = aggregateCoercionNode.Child;
                    }
                    else
                    {
                        maybeRecordNode = argWrites;
                    }

                    if (maybeRecordNode is RecordNode writes)
                    {
                        foreach (var kv in writes.Fields)
                        {
                            var fieldLogicalName = kv.Key.Value;
                            AddFieldWrite(tableLogicalName, fieldLogicalName);
                        }
                    }
                }
            }

            Context newContext = node.Function is DelegateFunction df && df.IsUsingColumnMap(node, out FxColumnMap columnMap)
                     ? context.WithColumnMap(columnMap)
                     : context;

            // Find all dependencies in args
            // This will catch reads.
            foreach (var arg in node.Args.Skip(firstArgIsWrite ? 1 : 0))
            {
                arg.Accept(this, newContext);
            }

            if (node.Function is DelegateFunction df2 && df2.IsUsingJoinNode(node, out FxJoinNode joinNode))
            {
                // Predicate
                AddFieldRead(joinNode.LinkEntity.LinkFromEntityName, joinNode.LinkEntity.LinkFromAttributeName);
                AddFieldRead(joinNode.LinkEntity.LinkToEntityName, joinNode.LinkEntity.LinkToAttributeName);

                // Right column map
                foreach (string rightField in joinNode.RightRealFieldNames)
                {
                    AddFieldRead(joinNode.LinkEntity.LinkToEntityName, rightField);
                }
            }

            return null;
        }

        public override RetVal Visit(BinaryOpNode node, Context context)
        {
            node.Left.Accept(this, context);
            node.Right.Accept(this, context);
            return null;
        }

        public override RetVal Visit(UnaryOpNode node, Context context)
        {
            return node.Child.Accept(this, context);
        }

        public override RetVal Visit(ScopeAccessNode node, Context context)
        {
            // Could be a symbol from RowScope.
            // Price in "LookUp(t1,Price=255)"
            if (node.Value is ScopeAccessSymbol sym)
            {
                if (_scopeTypes.TryGetValue(sym.Parent.Id, out var type))
                {
                    // Ignore ThisRecord scopeaccess node. e.g. Summarize(table, f1, Sum(ThisGroup, f2)) where ThisGroup should be ignored.
                    if (type is TableType tableType && node.IRContext.ResultType is not AggregateType)
                    {
                        var tableLogicalName = tableType.TableSymbolName;
                        var fieldLogicalName = sym.Name.Value;

                        AddFieldRead(tableLogicalName, fieldLogicalName);

                        return null;
                    }
                }
            }

            // Any symbol access here is some temporary local, and not a field.
            return null;
        }

        // field              // IR will implicity recognize as ThisRecod.field
        // ThisRecord.field   // IR will get type of ThisRecord
        // First(Remote).Data // IR will get type on left of dot.
        public override RetVal Visit(RecordFieldAccessNode node, Context context)
        {                           
            node.From.Accept(this, context);         

            var ltype = node.From.IRContext.ResultType;
            if (ltype is RecordType ltypeRecord)
            {
                // Logical name of the table on left side.
                // This will be null for non-dataverse records
                var tableLogicalName = ltypeRecord.TableSymbolName;
                if (tableLogicalName != null)
                {
                    var fieldLogicalName = node.Field.Value;
                    AddFieldRead(tableLogicalName, fieldLogicalName);
                }
            }

            return null;
        }

        public override RetVal Visit(ResolvedObjectNode node, Context context)
        {
            if (node.IRContext.ResultType is AggregateType aggType)
            {
                var tableLogicalName = aggType.TableSymbolName;
                if (tableLogicalName != null)
                {
                    AddFieldRead(tableLogicalName, null);
                }
            }

            // Check if identifer is a field access on a table in row scope
            var obj = node.Value;
            if (obj is NameSymbol sym)
            {
                if (sym.Owner is SymbolTableOverRecordType symTable)
                {
                    RecordType type = symTable.Type;
                    var tableLogicalName = type.TableSymbolName;

                    if (symTable.IsThisRecord(sym))
                    {
                        // "ThisRecord". Whole entity
                        AddFieldRead(tableLogicalName, null);
                        return null;
                    }

                    // on current table
                    var fieldLogicalName = sym.Name;

                    AddFieldRead(tableLogicalName, fieldLogicalName);
                }
            }
            else if (obj is GroupByObjectFormulaValue groupByFV && groupByFV.GroupBy != null)
            {
                var groupByNode = groupByFV.GroupBy;
                var tableName = ((AggregateType)node.IRContext.ResultType).TableSymbolName;
                foreach (var tableField in groupByNode.GroupingProperties)
                {
                    AddFieldRead(tableName, tableField);
                }

                foreach (var aggregateExpr in groupByNode.FxAggregateExpressions)
                {
                    AddFieldRead(tableName, aggregateExpr.PropertyName);
                }
            }

            return null;
        }

        public override RetVal Visit(SingleColumnTableAccessNode node, Context context)
        {
            throw new NotImplementedException();
        }

        public override RetVal Visit(ChainingNode node, Context context)
        {
            foreach (var child in node.Nodes)
            {
                child.Accept(this, context);
            }

            return null;
        }

        public override RetVal Visit(AggregateCoercionNode node, Context context)
        {
            foreach (var kv in node.FieldCoercions)
            {
                kv.Value.Accept(this, context);
            }

            return null;
        }

        public class RetVal
        {
        }

        public class Context
        {
            public FxColumnMap ColumnMap { get; private set; }

            public Context()
            {
                ColumnMap = null;
            }

            public Context WithColumnMap(FxColumnMap columnMap)
            {
                // replace any existing columnMap with the new one as the context is local only
                return new Context() { ColumnMap = columnMap };
            }
        }

        // Translate relationship names to actual field references.
        public string Translate(string tableLogicalName, string fieldLogicalName)
        {
            if (_metadataCache.TryGetXrmEntityMetadata(tableLogicalName, out var entityMetadata))
            {
                // Normal case.
                if (entityMetadata.TryGetAttribute(fieldLogicalName, out _))
                {
                    return fieldLogicalName;
                }

                // Relationship
                if (entityMetadata.TryGetRelationship(fieldLogicalName, out var realName))
                {
                    return realName;
                }
            }

            throw new InvalidOperationException($"Can't resolve {tableLogicalName}.{fieldLogicalName}");
        }

        // if fieldLogicalName, then we're taking a dependency on entire record.
        private void AddField(Dictionary<string, HashSet<string>> list, string tableLogicalName, string fieldLogicalName)
        {
            if (tableLogicalName == null)
            {
                return;
            }

            if (!list.TryGetValue(tableLogicalName, out var fieldReads))
            {
                fieldReads = new HashSet<string>();
                list[tableLogicalName] = fieldReads;
            }

            if (fieldLogicalName != null)
            {
                var name = Translate(tableLogicalName, fieldLogicalName);
                fieldReads.Add(name);
            }
        }

        public void AddFieldRead(string tableLogicalName, string fieldLogicalName)
        {
            if (Info.FieldReads == null)
            {
                Info.FieldReads = new Dictionary<string, HashSet<string>>();
            }

            AddField(Info.FieldReads, tableLogicalName, fieldLogicalName);
        }

        public void AddFieldWrite(string tableLogicalName, string fieldLogicalName)
        {
            if (Info.FieldWrites == null)
            {
                Info.FieldWrites = new Dictionary<string, HashSet<string>>();
            }

            AddField(Info.FieldWrites, tableLogicalName, fieldLogicalName);
        }
    }
}
