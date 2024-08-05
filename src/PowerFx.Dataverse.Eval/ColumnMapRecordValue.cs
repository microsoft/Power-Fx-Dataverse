// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse
{
    internal sealed class ColumnMapRecordValue : RecordValue
    {
        // when a column map is used, we use a subset of columns and potentially rename them
        // the map contains (new column name, Entity column name) entries
        // the recordType will be using the new set of column names
        // updates are not supported when a column map exists
        private readonly IReadOnlyDictionary<string, string> _columnMap;

        private readonly RecordValue _recordValue;

        public ColumnMapRecordValue(RecordValue recordValue, RecordType recordType, IReadOnlyDictionary<string, string> columnMap)
            : base(recordType)
        {
            _columnMap = columnMap;
            _recordValue = recordValue;
        }

        public static RecordType ApplyMap(RecordType recordType, IReadOnlyDictionary<string, string> columnMap = null)
        {
            if (columnMap != null)
            {
                RecordType rt = RecordType.Empty();

                foreach (KeyValuePair<string, string> kvp in columnMap)
                {
                    string newName = kvp.Key;
                    string oldName = kvp.Value;

                    rt = rt.Add(newName, recordType.GetFieldType(oldName));
                }

                recordType = rt;
            }

            return recordType;
        }

        protected override bool TryGetField(FormulaType fieldType, string fieldName, out FormulaValue result)
        {
            (bool isSuccess, FormulaValue value) = TryGetFieldAsync(fieldType, fieldName, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();

            result = value;
            return isSuccess;
        }

        protected override async Task<(bool Result, FormulaValue Value)> TryGetFieldAsync(FormulaType fieldType, string fieldName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string innerFieldName = _columnMap == null ? fieldName : _columnMap[fieldName];

            var value = await _recordValue.GetFieldAsync(innerFieldName, cancellationToken).ConfigureAwait(false);
            return (true, value);
        }

        public override Task<DValue<RecordValue>> UpdateFieldsAsync(RecordValue changeRecord, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotImplementedException("Cannot update a RecordValue with a column map");
        }

        public override void ToExpression(StringBuilder sb, FormulaValueSerializerSettings settings)
        {
            throw new InvalidOperationException($"ToExpression not supported on {nameof(DataverseRecordValue)} with column map");
        }

        public override void ShallowCopyFieldInPlace(string fieldName)
        {
            throw new InvalidOperationException($"ShallowCopyFieldInPlace not supported on {nameof(DataverseRecordValue)} with column map");
        }
    }
}
