// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;

#pragma warning disable SA1118, SA1117

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class TestTableValue : TableValue, IDelegatableTableValue
    {
        private readonly RecordValue _record1;

        public DelegationParameters DelegationParameters = null;

        public TestTableValue(string tableName, RecordType recordType, RecordValue record, Func<TableParameters, TableParameters> tableParametersModifier = null)
            : base(new TestRecordType(tableName, recordType, tableParametersModifier))
        {
            _record1 = record;
        }

        public override IEnumerable<DValue<RecordValue>> Rows => throw new NotImplementedException();

        public Task<IReadOnlyCollection<DValue<RecordValue>>> GetRowsAsync(IServiceProvider services, DelegationParameters parameters, CancellationToken cancel)
        {
            DelegationParameters = parameters;
            IReadOnlyCollection<DValue<RecordValue>> result = new DValue<RecordValue>[] { DValue<RecordValue>.Of(_record1) };
            return Task.FromResult(result);
        }
    }

    public class TestRecordType : RecordType
    {
        public TestRecordType(string tableName, RecordType recordType, Func<TableParameters, TableParameters> tableParametersModifier = null)
            : this(GetDisplayNameProvider(recordType), GetTableParameters(tableName, recordType, tableParametersModifier))
        {
        }

        internal TestRecordType(DisplayNameProvider displayNameProvider, TableParameters tableParameters)
            : base(displayNameProvider, tableParameters)
        {
        }

        private static DisplayNameProvider GetDisplayNameProvider(RecordType recordType)
        {
            return DisplayNameProvider.New(recordType.FieldNames.Select(f => new KeyValuePair<DName, DName>(new DName(f), new DName(f))));
        }

        private static TableParameters GetTableParameters(string tableName, RecordType recordType, Func<TableParameters, TableParameters> tableParametersModifier = null)
        {
            TableParameters tableParameters = TableParameters.Default(tableName, false, recordType, "dataset", recordType.FieldNames);

            if (tableParametersModifier != null)
            {
                tableParameters = tableParametersModifier(tableParameters);
            }

            return tableParameters;
        }

        public override bool Equals(object other)
        {
            throw new NotImplementedException();
        }

        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }
    }
}
