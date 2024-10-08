// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;

#pragma warning disable SA1118, SA1117

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class TestTableValue : TableValue, IDelegatableTableValue
    {
        private readonly RecordValue _recordValue;

        public DelegationParameters DelegationParameters = null;

        public TestTableValue(string tableName, RecordType recordType, RecordValue record, List<string> allowedFilters)
            : base(new TestRecordType(tableName, recordType, allowedFilters))
        {
            _recordValue = record;
        }

        public override IEnumerable<DValue<RecordValue>> Rows => throw new NotImplementedException();

        public Task<IReadOnlyCollection<DValue<RecordValue>>> GetRowsAsync(IServiceProvider services, DelegationParameters parameters, CancellationToken cancel)
        {
            DelegationParameters = parameters;
            IReadOnlyCollection<DValue<RecordValue>> result = new DValue<RecordValue>[] { DValue<RecordValue>.Of(_recordValue) };
            return Task.FromResult(result);
        }
    }

    public class TestRecordType : RecordType
    {
        private readonly RecordType _recordType;        

        public TestRecordType(string tableName, RecordType recordType, List<string> allowedFilters)
            : base(GetDisplayNameProvider(recordType), GetTableParameters(tableName, recordType, allowedFilters))
        {
            _recordType = recordType;            
        }       

        public override bool TryGetFieldType(string fieldName, out FormulaType type)
        {
            return _recordType.TryGetFieldType(fieldName, out type);
        }

        private static DisplayNameProvider GetDisplayNameProvider(RecordType recordType)
        {
            return DisplayNameProvider.New(recordType.FieldNames.Select(f => new KeyValuePair<DName, DName>(new DName(f), new DName(f))));
        }

        private static TableParameters GetTableParameters(string tableName, RecordType recordType, List<string> allowedFilters)
        {
            return new TestTableParameters(recordType, allowedFilters)
            {
                TableName = tableName
            };
        }

        public override bool Equals(object other)
        {
            if (other == null || other is not TestRecordType other2)
            {
                return false;
            }

            return _recordType == other2._recordType;
        }

        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }
    }

    public class TestTableParameters : TableParameters
    {
        private readonly List<string> _allowedFilters;
        private readonly RecordType _recordType;

        public TestTableParameters(RecordType recordType, List<string> allowedFilters) 
        { 
            _recordType = recordType;
            _allowedFilters = allowedFilters;
        }

        public override ColumnCapabilitiesDefinition GetColumnCapability(string fieldName)
        {
            // Same list for all columns
            if (_recordType.TryGetFieldType(fieldName, out FormulaType formulaType))
            {
                return new ColumnCapabilitiesDefinition()
                {
                    FilterFunctions = _allowedFilters
                };
            }

            return null;
        }
    }
}
