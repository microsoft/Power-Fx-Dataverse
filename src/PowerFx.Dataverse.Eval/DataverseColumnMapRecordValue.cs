// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.PowerFx.Dataverse
{
    internal class DataverseColumnMapRecordValue : ColumnMapRecordValue
    {
        internal DataverseRecordValue RecordValue => (DataverseRecordValue)_recordValue;

        public DataverseColumnMapRecordValue(DataverseRecordValue recordValue, IReadOnlyDictionary<string, string> columnMap = null)
            : base(recordValue, columnMap)
        {
        }

        public override void ToExpression(StringBuilder sb, FormulaValueSerializerSettings settings)
        {
            if (_columnMap == null)
            {
                RecordValue.ToExpression(sb, settings);
                return;
            }

            throw new InvalidOperationException($"ToExpression not supported on {nameof(DataverseColumnMapRecordValue)} with column map");
        }

        public override object ToObject()
        {
            return RecordValue.ToObject();
        }
    }
}
