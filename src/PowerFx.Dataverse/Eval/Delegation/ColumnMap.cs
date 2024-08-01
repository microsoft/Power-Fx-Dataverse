using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse
{
    public class ColumnMap
    {
        // For now, even if we could store any IntermediateNode, we only manage TexlLiteralNode for simplicity
        // Key represents the new column name, Value represents the value of that column (logical name)
        private readonly Dictionary<DName, IntermediateNode> _dic;

        // When defined, this is the column named used for Distinct function
        private readonly string _distinctColumn = null;

        // Public constructor (doesn't support renames or distinct)
        public ColumnMap(IEnumerable<string> map)
        {
            _dic = map.ToDictionary(str => new DName(str), str => new TextLiteralNode(IRContext.NotInSource(FormulaType.String), str) as IntermediateNode);
        }

        // Constructor used for ForAll (renames are possible)
        internal ColumnMap(IReadOnlyDictionary<DName, TextLiteralNode> dic)
        {
            _dic = dic.ToDictionary(kvp => kvp.Key, kvp => kvp.Value as IntermediateNode);
        }

        // Constructor used for ShowColumns (no rename)
        internal ColumnMap(IEnumerable<TextLiteralNode> list)
        {
            _dic = list.ToDictionary(tln => new DName(tln.LiteralValue), tln => tln as IntermediateNode);
        }

        // Constructor used by Combine static method
        private ColumnMap(Dictionary<DName, IntermediateNode> dic, string distinctColumn)
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

        internal static ColumnSet GetColumnSet(ColumnMap map) => map == null ? new ColumnSet(true) : new ColumnSet(map.Columns);

        internal static ColumnSet GetColumnSet(IEnumerable<string> columns) => columns == null ? new ColumnSet(true) : new ColumnSet(columns.ToArray());

        internal static ColumnMap GetColumnMap(IEnumerable<string> columns) => columns == null ? null : new ColumnMap(columns);

        internal static bool HasDistinct(ColumnMap map) => map != null && !string.IsNullOrEmpty(map._distinctColumn);

        // returns the string contained in TextLiteralNode
        private static string GetString(IntermediateNode i)
            => i is TextLiteralNode tln
               ? tln.LiteralValue
               : throw new InvalidOperationException($"Invalid {nameof(IntermediateNode)}, expexting {nameof(TextLiteralNode)} and received {i.GetType().Name}");

        internal static ColumnMap Combine(ColumnMap first, ColumnMap second)
        {
            if (first == null)
            {
                return second;
            }

            string distinctColumn = null;
            Dictionary<DName, IntermediateNode> newDic = new Dictionary<DName, IntermediateNode>();

            foreach (KeyValuePair<DName, IntermediateNode> kvp2 in second._dic)
            {
                string secondValue = GetString(kvp2.Value);

                if (first._dic.TryGetValue(new DName(secondValue), out IntermediateNode firstNode))
                {
                    string firstValue = GetString(firstNode);
                    newDic.Add(kvp2.Key, new TextLiteralNode(IRContext.NotInSource(FormulaType.String), firstValue));                    
                }
                else if (first._distinctColumn == secondValue)
                {
                    newDic.Add(kvp2.Key, new TextLiteralNode(IRContext.NotInSource(FormulaType.String), first._distinctColumn));
                    distinctColumn = first._distinctColumn;
                }
                else
                {
                    throw new InvalidOperationException("Missing element in first ColumnMap");
                }
            }

            if (!string.IsNullOrEmpty(second._distinctColumn))
            {
                if (first._dic.TryGetValue(new DName(second._distinctColumn), out IntermediateNode firstNode))
                {
                    distinctColumn = GetString(firstNode);
                }
            }

            if (!string.IsNullOrEmpty(first._distinctColumn) && string.IsNullOrEmpty(distinctColumn))
            {
                distinctColumn = first._distinctColumn;
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
               : string.Join(", ", this.AsStringDictionary().Select(kvp => $"{kvp.Key}:{kvp.Value}{(_distinctColumn != default && kvp.Value == _distinctColumn ? "*" : string.Empty)}"));
    }
}
