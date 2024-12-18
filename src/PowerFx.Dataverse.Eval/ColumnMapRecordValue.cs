// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Core.Utils;
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
            NamedValue[] fields = Fields.ToArray();

            foreach (KeyValuePair<string, string> kvp in this._columnMap)
            {
                string newName = kvp.Key;
                string oldName = kvp.Value;

                int i = Array.FindIndex(fields, fld => fld.Name == newName);

                FormulaType fieldType = Type.GetFieldType(newName);
                FormulaValue fv = _recordValue.GetField(oldName);

                fields[i] = new NamedValue(newName, fv);
            }

            RecordValue newRecordValue = RecordValue.NewRecordFromFields(fields);

            newRecordValue.ToExpression(sb, settings);
        }

        public override void ShallowCopyFieldInPlace(string fieldName)
        {
            throw new InvalidOperationException($"ShallowCopyFieldInPlace not supported on {nameof(DataverseRecordValue)} with column map");
        }
    }
}
