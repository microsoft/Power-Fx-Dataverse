// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation
{
    internal class ColumnMapFormulaValue : ValidFormulaValue
    {
        private readonly FxColumnMap _columnMap;

        /// <summary>
        /// null means select All Columns. Empty means select No Columns.
        /// </summary>
        internal FxColumnMap ColumnMap => _columnMap;

        public ColumnMapFormulaValue(FxColumnMap columnMap)
            : base(IRContext.NotInSource(columnMap?.SourceTableRecordType ?? RecordType.Empty()))
        {
            _columnMap = columnMap;
        }

        public override void ToExpression(StringBuilder sb, FormulaValueSerializerSettings settings)
        {
            sb.Append(ToString());
        }

        public override object ToObject()
        {
            return _columnMap;
        }

        public override void Visit(IValueVisitor visitor)
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            if (_columnMap == null)
            {
                return "__allColumns()";
            }

            return _columnMap.ToString();
        }
    }
}
