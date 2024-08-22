// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse.DataSource
{
    internal class DataverseTableType : TableType
    {
        private readonly DataverseRecordType _dataverseRecordType;

        public DataverseTableType(DataverseRecordType dataverseRecordType)
            : base(dataverseRecordType._type.ToTable())
        {
            _dataverseRecordType = dataverseRecordType;
        }

        public override bool TryGetFieldType(string name, out FormulaType type)
        {
            return _dataverseRecordType.TryGetFieldType(name, out type);
        }

        public override IEnumerable<string> FieldNames => _dataverseRecordType.FieldNames;

        public override bool TryGetPrimaryKeyFieldName(out string primaryKeyFieldName)
        {
            return _dataverseRecordType.TryGetPrimaryKeyFieldName(out primaryKeyFieldName);
        }

        public override bool Equals(object other)
        {
            if (other is DataverseTableType otherTableType)
            {
                return _dataverseRecordType.Equals(otherTableType._dataverseRecordType);
            }

            return _type.Equals(other);
        }

        public override int GetHashCode()
        {
            return _type.GetHashCode();
        }
    }
}
