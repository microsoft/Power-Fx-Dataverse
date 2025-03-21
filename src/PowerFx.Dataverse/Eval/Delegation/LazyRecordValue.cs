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
    internal sealed class LazyRecordValue : RecordValue
    {        
        private readonly RecordValue _recordValue;

        public LazyRecordValue(RecordValue recordValue, RecordType recordType) 
            : base(recordType)
        {            
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

            var value = await _recordValue.GetFieldAsync(fieldName, cancellationToken).ConfigureAwait(false);
            return (true, value);
        }

        public override Task<DValue<RecordValue>> UpdateFieldsAsync(RecordValue changeRecord, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotImplementedException($"Cannot update a {nameof(LazyRecordValue)}");
        }

        public override void ShallowCopyFieldInPlace(string fieldName)
        {
            throw new InvalidOperationException($"ShallowCopyFieldInPlace not supported on {nameof(LazyRecordValue)}");
        }
    }
}
