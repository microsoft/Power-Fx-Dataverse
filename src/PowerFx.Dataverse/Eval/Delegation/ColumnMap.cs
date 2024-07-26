using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse
{
    // ColumnMap is an IReadOnlyDictionary<DName, IntermediateNode> to be used in RecordNode constructor
    public class ColumnMap : IReadOnlyDictionary<DName, IntermediateNode>
    {
        // For now, even if we could store any IntermediateNode, we only manage TexlLiteralNode for simplicity
        // Key represents the new column name, Value represents the value of that column (logical name)
        private readonly Dictionary<DName, IntermediateNode> _dic;

        // When defined, this is the column named used for Distinct function
        private readonly string _distinctColumn = null;

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
        internal ColumnMap(RecordValue recordValue)
        {
            _dic = recordValue.Fields.ToDictionary(
                f => new DName(f.Name),
                f => f.Value is StringValue sv
                     ? new TextLiteralNode(IRContext.NotInSource(FormulaType.String), sv.Value) as IntermediateNode
                     : throw new InvalidOperationException($"Invalid type in column map, got {f.Value.GetType().Name}"));
        }

        // Constructor used for Distinct
        internal ColumnMap(string distinctColumn)
        {
            _distinctColumn = distinctColumn;

            // Distinct implies a rename to Value column
            _dic = new Dictionary<DName, IntermediateNode>() { { new DName("Value"), new TextLiteralNode(IRContext.NotInSource(FormulaType.String), _distinctColumn) } };
        }

        internal static ColumnSet GetColumnSet(ColumnMap map) => map == null ? new ColumnSet(true) : new ColumnSet(map.Columns);

        internal static bool HasDistinct(ColumnMap map) => map != null && !string.IsNullOrEmpty(map._distinctColumn);

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
            }

            if (!string.IsNullOrEmpty(second._distinctColumn))
            {
                if (first._dic.TryGetValue(new DName(second._distinctColumn), out IntermediateNode firstNode))
                {
                    distinctColumn = GetString(firstNode);
                }
            }

            return new ColumnMap(newDic, distinctColumn);
        }

        internal string Distinct => !string.IsNullOrEmpty(_distinctColumn) ? _distinctColumn : throw new InvalidOperationException("Cannot access Distinct property without checking ColumnMap.HasDistinct() first");

        public int Count => throw new NotImplementedException();

        public IReadOnlyDictionary<string, string> AsStringDictionary() => _dic.ToDictionary(kvp => kvp.Key.Value, kvp => GetString(kvp.Value));

        internal string[] Columns => _dic.Values.Select(node => GetString(node)).ToArray();

        public IEnumerable<DName> Keys => throw new NotImplementedException();

        IEnumerable<IntermediateNode> IReadOnlyDictionary<DName, IntermediateNode>.Values => throw new NotImplementedException();

        IntermediateNode IReadOnlyDictionary<DName, IntermediateNode>.this[DName key] => throw new NotImplementedException();

        internal IEnumerator<KeyValuePair<DName, IntermediateNode>> GetEnum() => _dic.GetEnumerator();

        public IEnumerator GetEnumerator() => this.GetEnum();

        IEnumerator<KeyValuePair<DName, IntermediateNode>> IEnumerable<KeyValuePair<DName, IntermediateNode>>.GetEnumerator() => this.GetEnum();

        public bool ContainsKey(DName key) => _dic.ContainsKey(key);

        bool IReadOnlyDictionary<DName, IntermediateNode>.TryGetValue(DName key, out IntermediateNode value) => _dic.TryGetValue(key, out value);

        // Used for debugging
        public override string ToString()
            => _dic == null
               ? "<null>"
               : string.Join(", ", this.AsStringDictionary().Select(kvp => $"{kvp.Key}:{kvp.Value}{(_distinctColumn != default && kvp.Value == _distinctColumn ? "*" : string.Empty)}"));
    }
}
