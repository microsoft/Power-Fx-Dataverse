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
using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;

#pragma warning disable SA1118, SA1117

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class TestTableValue : TableValue, IDelegatableTableValue
    {
        private readonly RecordValue _recordValue;

        public DelegationParameters DelegationParameters = null;        

        public TestTableValue(string tableName, RecordType recordType, RecordValue record, List<DelegationOperator> allColumnFilters)
            : base(new TestRecordType(tableName, recordType, allColumnFilters))
        {
            _recordValue = record;
        }

        public override IEnumerable<DValue<RecordValue>> Rows
        {
            get
            {
                return GetRowsAsync(null, null, CancellationToken.None).Result;
            }
        }

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

        public TestRecordType(string tableName, RecordType recordType, List<DelegationOperator> allColumnFilters)
            : base(GetDisplayNameProvider(recordType), GetDelegationInfo(tableName, recordType, allColumnFilters))
        {
            _recordType = recordType;            
        }       

        public override bool TryGetFieldType(string fieldName, out FormulaType type)
        {
            return _recordType.TryGetFieldType(fieldName, out type);
        }

        private static DisplayNameProvider GetDisplayNameProvider(RecordType recordType)
        {
            return DisplayNameProvider.New(recordType.GetFieldTypes().Select(fType => new KeyValuePair<DName, DName>(new DName(fType.Name), fType.DisplayName != default ? new DName(fType.DisplayName) : new DName(fType.Name))));
        }

        private static TableDelegationInfo GetDelegationInfo(string tableName, RecordType recordType, List<DelegationOperator> allColumnFilters)
        {
            return new TestDelegationInfo(recordType, allColumnFilters)
            {
                TableName = tableName,
                SelectionRestriction = new SelectionRestrictions() { IsSelectable = true }
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

    public class TestDelegationInfo : TableDelegationInfo
    {
        private readonly List<DelegationOperator> _allColumnFilters;
        private readonly RecordType _recordType;

        public TestDelegationInfo(RecordType recordType, List<DelegationOperator> allColumnFilters) 
        { 
            _recordType = recordType;
            _allColumnFilters = allColumnFilters;
            
            FilterSupportedFunctions = allColumnFilters;

            // Makes the table sortable (= First, OrderBy... can be delegated)
            SortRestriction = new SortRestrictions();            
        }

        public override bool IsDelegable => true;        
                
        public override ColumnCapabilitiesDefinition GetColumnCapability(string fieldName)
        {
            // Same list for all columns
            if (_recordType.TryGetFieldType(fieldName, out FormulaType formulaType))
            {
                return new ColumnCapabilitiesDefinition()
                {
                    FilterFunctions = _allColumnFilters
                };
            }

            return null;
        }
    }
}
