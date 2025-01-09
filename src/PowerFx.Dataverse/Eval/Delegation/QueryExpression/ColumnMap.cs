// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Microsoft;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.Texl.Builtins;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression
{
    public class ColumnMap
    {
        // For now, even if we could store any IntermediateNode, we only manage TexlLiteralNode for simplicity
        // Key represents the new column name, Value represents the value of that column (logical name)
        private readonly Dictionary<DName, IntermediateNode> _dic;

        private bool _existsAliasing = false;

        // Call this method when you need to set it, should be never set false manually.
        public void MarkAliasingExists()
        {
            _existsAliasing = true;
        }

        internal bool ExistsAliasing => _existsAliasing;

        // When defined, this is the column named used for Distinct function
        private readonly string _distinctColumn = null;

        //$$$ Does this needs to be ConcurrentDictionary?

        /// <summary>
        /// Key represents alias column name if present, else real column name.
        /// </summary>
        private readonly IDictionary<string, FxColumnInfo> _columns = new Dictionary<string, FxColumnInfo>();

        private readonly RecordType _sourceTableRecordType;

        internal RecordType SourceTableRecordType => _sourceTableRecordType;

        /// <summary>
        /// Initializes a new instance of the <see cref="ColumnMap"/> class.
        /// </summary>
        /// <param name="logicalColumns">logical name of column is Datasource.</param>
        internal ColumnMap(IEnumerable<string> logicalColumns)
        {
            _columns = logicalColumns.Select(c => new FxColumnInfo(c, c)).ToDictionary(c => c.AliasColumnName ?? c.RealColumnName);
        }

        internal ColumnMap(TableType sourceTableType) 
            : this(sourceTableType.ToRecord())
        {
        }

        internal ColumnMap(RecordType sourceTableRecordType)
        {
            _sourceTableRecordType = sourceTableRecordType;
        }

        private string GenerateColumnInfoKey(FxColumnInfo columnInfo)
        {
            return columnInfo.AliasColumnName ?? columnInfo.RealColumnName;
        }

        internal bool HasDistinct()
        {
            if (_columns.Count() == 1)
            {
                return _columns.First().Value.IsDistinct;
            }

            return false;
        }

        internal ColumnMap AddColumn(string logicalColumnName, string aliasColumnName = null)
        {
            var columnInfo = new FxColumnInfo(logicalColumnName, aliasColumnName);

            if (columnInfo.AliasColumnName != null && !ExistsAliasing)
            {
                MarkAliasingExists();
            }

            AddColumn(columnInfo);
            return this;
        }

        internal void AddColumn(FxColumnInfo fxColumnInfo)
        {
            var columnsMapKey = GenerateColumnInfoKey(fxColumnInfo);

            if (_columns.ContainsKey(columnsMapKey))
            {
                throw new InvalidOperationException($"Column {fxColumnInfo.RealColumnName} already exists in the column map");
            }

            if (!_sourceTableRecordType.TryGetBackingDType(fxColumnInfo.RealColumnName, out _))
            {
                throw new InvalidOperationException($"Column {fxColumnInfo.RealColumnName} does not exist in the table's type.");
            }

            _columns.Add(columnsMapKey, fxColumnInfo);
        }

        /// <summary>
        /// Removes the column from the column map.
        /// </summary>
        /// <param name="aliasOrLogicalName">Alias name if Column was previosuly aliased in expression, else logical name of Column.</param>
        /// <param name="columnInfo"></param>
        /// <returns></returns>
        internal bool TryGetColumnInfo(string aliasOrLogicalName, out FxColumnInfo columnInfo)
        {
            if (string.IsNullOrEmpty(aliasOrLogicalName))
            {
                throw new InvalidOperationException($"{nameof(aliasOrLogicalName)} cannot be null or empty");
            }

            if (!_columns.TryGetValue(aliasOrLogicalName, out columnInfo))
            {
                return false;
            }

            return true;
        }

        private FxColumnInfo RemoveColumn(string aliasOrLogicalName)
        {
            if (string.IsNullOrEmpty(aliasOrLogicalName))
            {
                throw new InvalidOperationException("Alias name cannot be null or empty");
            }

            if (!_columns.TryGetValue(aliasOrLogicalName, out var columnInfo))
            {
                throw new InvalidOperationException($"Column {aliasOrLogicalName} does not exist in the {nameof(ColumnMap)} and is not a logicalName.");
            }

            _columns.Remove(aliasOrLogicalName);
            return columnInfo;
        }

        /// <summary>
        /// Updates the alias name of the column based on <paramref name="previosAliasOrLogicalName"/> "/>.
        /// </summary>
        /// <param name="previosAliasOrLogicalName">Previous alias or logical name.</param>
        /// <param name="newAliasName"></param>
        internal void UpdateAlias(string previosAliasOrLogicalName, string newAliasName)
        {
            if (string.IsNullOrEmpty(previosAliasOrLogicalName) || string.IsNullOrEmpty(newAliasName))
            {
                throw new InvalidOperationException("Alias names cannot be null or empty");
            }

            if (_columns.ContainsKey(newAliasName))
            {
                throw new InvalidOperationException($"Column {newAliasName} already exists in the column map");
            }

            if (!_columns.TryGetValue(previosAliasOrLogicalName, out var columnInfo))
            {
                if (_sourceTableRecordType.TryGetBackingDType(previosAliasOrLogicalName, out _))
                {
                    var colmnInfo = new FxColumnInfo(previosAliasOrLogicalName, previosAliasOrLogicalName);
                    _columns.Add(previosAliasOrLogicalName, colmnInfo);
                    return;
                }
                else
                {
                    throw new InvalidOperationException($"Column {previosAliasOrLogicalName} does not exist in the {nameof(ColumnMap)} and is not a logicalName.");
                }
            }

            RemoveColumn(previosAliasOrLogicalName);
            AddColumn(columnInfo.RealColumnName, newAliasName);
        }

        // Constructor used by Combine static method
        internal ColumnMap(Dictionary<DName, IntermediateNode> dic, string distinctColumn)
        {
            _dic = dic;
            _distinctColumn = distinctColumn;
        }

        // Constructor used by delegate functions
        internal ColumnMap(RecordValue recordValue, string distinctColumn)
        {
            _dic = recordValue.Fields.ToDictionary(
                f => new DName(f.Name),
                f => f.Value is StringValue sv
                     ? new TextLiteralNode(IRContext.NotInSource(FormulaType.String), sv.Value) as IntermediateNode
                     : throw new InvalidOperationException($"Invalid type in column map, got {f.Value.GetType().Name}"));

            _distinctColumn = string.IsNullOrEmpty(distinctColumn) ? null : distinctColumn;
        }

        // Constructor used by DelegateFunction.IsUsingColumnMap
        internal ColumnMap(RecordNode recordNode, TextLiteralNode textLiteralNode)
        {
            _dic = recordNode.Fields.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            _distinctColumn = textLiteralNode.LiteralValue;
        }

        // Constructor used for Distinct
        internal ColumnMap(string distinctColumn)
        {
            _distinctColumn = distinctColumn;

            // Distinct implies a rename to Value column
            _dic = new Dictionary<DName, IntermediateNode>() { { new DName("Value"), new TextLiteralNode(IRContext.NotInSource(FormulaType.String), _distinctColumn) } };
        }

        internal static ColumnSet GetColumnSet(ColumnMap map) => map == null ? new ColumnSet(true) : map.GetColumnSet();

        private ColumnSet GetColumnSet()
        {
            var columnSet = new ColumnSet();

            foreach (KeyValuePair<DName, IntermediateNode> kvp in _dic)
            {
                columnSet.AttributeExpressions.Add(new XrmAttributeExpression(GetString(kvp.Value)) { Alias = kvp.Key.Value });
            }

            return columnSet;
        }

        internal static ColumnSet GetColumnSet(IEnumerable<string> columns) => columns == null ? new ColumnSet(true) : new ColumnSet(columns.ToArray());

        internal static ColumnMap GetColumnMap(IEnumerable<string> columns) => columns == null ? null : new ColumnMap(columns);

        internal static bool HasDistinct(ColumnMap map) => map != null && !string.IsNullOrEmpty(map._distinctColumn);

        // returns the string contained in TextLiteralNode
        internal static string GetString(IntermediateNode i)
            => i is TextLiteralNode tln
               ? tln.LiteralValue
               : throw new InvalidOperationException($"Invalid {nameof(IntermediateNode)}, expexting {nameof(TextLiteralNode)} and received {i.GetType().Name}");

        internal static ColumnMap Combine(ColumnMap first, ColumnMap second, TableType tableType)
        {
            if (first == null || !first.Map.Any())
            {
                return second;
            }

            string distinctColumn = null;
            var newDic = new Dictionary<DName, IntermediateNode>();

            foreach (KeyValuePair<DName, IntermediateNode> kvp2 in second._dic)
            {
                var secondValue = GetString(kvp2.Value);

                if (first._dic.TryGetValue(new DName(secondValue), out IntermediateNode firstNode))
                {
                    var firstValue = GetString(firstNode);
                    newDic.Add(kvp2.Key, new TextLiteralNode(IRContext.NotInSource(FormulaType.String), firstValue));

                    if (secondValue == second._distinctColumn)
                    {
                        distinctColumn = firstValue;
                    }
                }
                else if (tableType.FieldNames.Contains(secondValue))
                {
                    newDic.Add(kvp2.Key, new TextLiteralNode(IRContext.NotInSource(FormulaType.String), secondValue));
                }
                else
                {
                    throw new InvalidOperationException("Missing element in first ColumnMap");
                }
            }

            foreach (var kvp in first._dic)
            {
                // Use the same string extraction as before
                var firstValue = GetString(kvp.Value);

                // Only add if no existing entry in 'newDic' has the same text value
                // (Alternatively, you might check for matching keys instead.)
                var alreadyPresent =
                    newDic.Values.Any(i => GetString(i).Equals(firstValue, StringComparison.OrdinalIgnoreCase));

                if (!alreadyPresent)
                {
                    newDic.Add(kvp.Key, new TextLiteralNode(IRContext.NotInSource(FormulaType.String), firstValue));
                }
            }

            if (string.IsNullOrEmpty(distinctColumn))
            {
                if (!string.IsNullOrEmpty(second._distinctColumn) && string.IsNullOrEmpty(first._distinctColumn))
                {
                    distinctColumn = second._distinctColumn;
                }
                else if (!string.IsNullOrEmpty(first._distinctColumn) && string.IsNullOrEmpty(second._distinctColumn))
                {
                    distinctColumn = first._distinctColumn;
                }
                else if (first._distinctColumn == second._distinctColumn)
                {
                    distinctColumn = first._distinctColumn;
                }
                else
                {
                    throw new InvalidOperationException("Distinct column is present in both ColumnMaps");
                }
            }

            // verify that distinct column name is present in the new dictionary
            if (!string.IsNullOrEmpty(distinctColumn) && !newDic.Values.Any(i => GetString(i) == distinctColumn))
            {
                throw new InvalidOperationException($"Invalid distinct column name {distinctColumn}");
            }

            return new ColumnMap(newDic, distinctColumn);
        }

        internal IReadOnlyDictionary<DName, IntermediateNode> Map => _dic;

        internal string Distinct => !string.IsNullOrEmpty(_distinctColumn) ? _distinctColumn : throw new InvalidOperationException("Cannot access Distinct property without checking ColumnMap.HasDistinct() first");

        public IReadOnlyDictionary<string, string> AsStringDictionary() => _dic.ToDictionary(kvp => kvp.Key.Value, kvp => GetString(kvp.Value));

        internal string[] Columns => _dic.Values.Select(node => GetString(node)).ToArray();

        // Used for debugging
        public override string ToString()
            => _dic == null
               ? "<null>"
               : !_dic.Any()
               ? "\x2205" // ∅
               : string.Join(", ", AsStringDictionary().Select(kvp => $"{kvp.Key}:{kvp.Value}{(_distinctColumn != default && kvp.Value == _distinctColumn ? "*" : string.Empty)}"));

    }
}
