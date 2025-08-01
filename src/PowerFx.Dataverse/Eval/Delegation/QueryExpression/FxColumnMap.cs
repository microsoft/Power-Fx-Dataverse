﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Text;
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
    [Obsolete("preview")]
    public class FxColumnMap : IEnumerable<FxColumnInfo>
    {
        /// <summary>
        /// Key represents alias column name if column was ever aliased in expression, else logical column name;
        /// </summary>
        private readonly IDictionary<string, FxColumnInfo> _columnInfoMap = new Dictionary<string, FxColumnInfo>();

        /// <summary>
        /// Key represents alias column name if column was ever aliased in expression, else logical column name; This is readonly version for internal use.
        /// </summary>
        internal IReadOnlyDictionary<string, FxColumnInfo> ColumnInfoMap => new ReadOnlyDictionary<string, FxColumnInfo>(_columnInfoMap);

        internal bool IsEmpty => _columnInfoMap.Count == 0;

        private readonly RecordType _sourceTableRecordType;

        internal RecordType SourceTableRecordType => _sourceTableRecordType;

        private bool _existsAliasing = false;

        private bool _returnTotalRowCount;

        /// <summary>
        /// Gets a value indicating whether to return total row count.
        /// </summary>
        internal bool ReturnTotalRowCount => _returnTotalRowCount;

        // Call this method when you need to set it, should be never set false manually.
        public void MarkAliasingExists()
        {
            _existsAliasing = true;
        }

        internal bool ExistsAliasing => _existsAliasing;

        public static FxColumnMap New(IEnumerable<string> logicalColumns)
        {
            return new FxColumnMap(logicalColumns);
        }

        public FxColumnMap(TableType sourceTableType, bool returnTotalRowCount = false)
            : this(sourceTableType.ToRecord(), returnTotalRowCount)
        {
        }

        internal FxColumnMap(RecordType sourceTableRecordType, bool returnTotalRowCount = false)
        {
            _sourceTableRecordType = sourceTableRecordType ?? throw new ArgumentNullException(nameof(sourceTableRecordType));
            _returnTotalRowCount = returnTotalRowCount;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FxColumnMap"/> class.
        /// </summary>
        /// <param name="logicalColumns">logical name of column is Datasource.</param>
        private FxColumnMap(IEnumerable<string> logicalColumns)
        {
            _columnInfoMap = logicalColumns.Select(c => new FxColumnInfo(c, c)).ToDictionary(c => c.AliasColumnName ?? c.RealColumnName);
            _returnTotalRowCount = false;
        }

        internal static string GenerateColumnInfoKey(FxColumnInfo columnInfo)
        {
            return columnInfo.AliasColumnName ?? columnInfo.RealColumnName;
        }

        public bool HasDistinct()
        {
            if (_columnInfoMap.Count() == 1)
            {
                return _columnInfoMap.First().Value.IsDistinct;
            }

            return false;
        }

        internal FxColumnInfo GetDistinctColumn()
        {
            if (!HasDistinct())
            {
                return null;
            }

            return _columnInfoMap.Values.FirstOrDefault();
        }

        public FxColumnMap AddColumn(string logicalColumnName, string aliasColumnName = null)
        {
            var columnInfo = new FxColumnInfo(logicalColumnName, aliasColumnName);
            AddColumn(columnInfo);
            return this;
        }

        internal void MarkReturnTotalRowCount()
        {
            _returnTotalRowCount = true;
        }

        internal void AddColumn(FxColumnInfo fxColumnInfo)
        {
            if (fxColumnInfo.AliasColumnName != null && !ExistsAliasing)
            {
                MarkAliasingExists();
            }

            var columnsMapKey = GenerateColumnInfoKey(fxColumnInfo);

            if (_columnInfoMap.ContainsKey(columnsMapKey))
            {
                throw new InvalidOperationException($"Column {fxColumnInfo.RealColumnName} already exists in the column map");
            }

            // RealColumn can be null for CountRows().
            if (fxColumnInfo.RealColumnName != null && !_sourceTableRecordType.TryGetBackingDType(fxColumnInfo.RealColumnName, out _))
            {
                throw new InvalidOperationException($"Column {fxColumnInfo.RealColumnName} does not exist in the table's type.");
            }

            _columnInfoMap.Add(columnsMapKey, fxColumnInfo);
        }

        /// <summary>
        /// Gets <see cref="FxColumnInfo"/> that matches <paramref name="aliasOrLogicalName"/>.
        /// </summary>
        /// <param name="aliasOrLogicalName">Alias name if Column was previously aliased in expression, else logical name of Column.</param>
        /// <param name="columnInfo"></param>
        /// <returns></returns>
        public bool TryGetColumnInfo(string aliasOrLogicalName, out FxColumnInfo columnInfo)
        {
            if (string.IsNullOrEmpty(aliasOrLogicalName))
            {
                throw new InvalidOperationException($"{nameof(aliasOrLogicalName)} cannot be null or empty");
            }

            if (!_columnInfoMap.TryGetValue(aliasOrLogicalName, out columnInfo))
            {
                return false;
            }

            return true;
        }

        internal FxColumnInfo RemoveColumnInfo(string aliasOrLogicalName)
        {
            if (TryRemoveColumnInfo(aliasOrLogicalName, out var fxColumnInfo))
            {
                return fxColumnInfo;
            }

            throw new InvalidOperationException($"Column {aliasOrLogicalName} does not exist in the {nameof(FxColumnMap)} and is not a logicalName.");
        }

        internal bool TryRemoveColumnInfo(string aliasOrRealName, out FxColumnInfo columnInfo)
        {
            if (string.IsNullOrEmpty(aliasOrRealName))
            {
                throw new InvalidOperationException($"{nameof(aliasOrRealName)} cannot be null or empty");
            }

            if (!_columnInfoMap.TryGetValue(aliasOrRealName, out columnInfo))
            {
                return false;
            }

            _columnInfoMap.Remove(aliasOrRealName);
            return true;
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

            if (_columnInfoMap.ContainsKey(newAliasName))
            {
                throw new InvalidOperationException($"Column {newAliasName} already exists in the column map");
            }

            if (!_columnInfoMap.TryGetValue(previosAliasOrLogicalName, out var columnInfo))
            {
                if (_sourceTableRecordType.TryGetBackingDType(previosAliasOrLogicalName, out _))
                {
                    var colmnInfo = new FxColumnInfo(previosAliasOrLogicalName, previosAliasOrLogicalName);
                    _columnInfoMap.Add(previosAliasOrLogicalName, colmnInfo);
                    return;
                }
                else
                {
                    throw new InvalidOperationException($"Column {previosAliasOrLogicalName} does not exist in the {nameof(FxColumnMap)} and is not a logicalName.");
                }
            }

            RemoveColumnInfo(previosAliasOrLogicalName);
            AddColumn(columnInfo.RealColumnName, newAliasName);
        }

        /// <summary>
        /// Gets non aggregate column names that matches the DataSource.
        /// </summary>
        internal IEnumerable<string> RealColumnNames => _columnInfoMap.Values.Where(ci => ci.AggregateMethod != PowerFx.Core.Entities.SummarizeMethod.None).Select(c => c.RealColumnName);

        public override string ToString()
        {
            var sb = new StringBuilder();
            var isFirst = true;

            foreach (var columnInfo in _columnInfoMap.Values)
            {
                if (!isFirst)
                {
                    sb.Append(",");
                }
                else
                {
                    isFirst = false;
                    sb.Append("{");
                }

                sb.Append(columnInfo.ToString());
            }

            if (ReturnTotalRowCount)
            {
                if (!isFirst)
                {
                    sb.Append(",");
                }
                else
                {
                    isFirst = false;
                    sb.Append("{");
                }

                sb.Append("Count(*)");
            }

            if (sb.Length > 0)
            {
                sb.Append("}");
            }

            return sb.ToString();
        }

        public IEnumerator<FxColumnInfo> GetEnumerator()
        {
            return _columnInfoMap.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
