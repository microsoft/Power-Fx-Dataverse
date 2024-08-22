// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;

namespace Microsoft.PowerFx.Dataverse.DataSource
{
    internal class DataverseRecordType : RecordType
    {
        private readonly RecordType _innerRecordType;

        // implement below all the virtuals from RecordType
        public override IEnumerable<string> FieldNames => _innerRecordType.FieldNames;

        public override string UserVisibleTypeName => _innerRecordType.UserVisibleTypeName;

        public string PrimaryKeyFieldName()
        {
            if (DelegationUtility.TryGetEntityMetadata(_innerRecordType, out var entityMetadata))
            {
                return entityMetadata.PrimaryIdAttribute;
            }

            return null;
        }

        public override RecordType Add(NamedFormulaType field)
        {
            return _innerRecordType.Add(field);
        }

        internal override void DefaultExpressionValue(StringBuilder sb)
        {
            _innerRecordType.DefaultExpressionValue(sb);
        }

        public override string TableSymbolName => _innerRecordType.TableSymbolName;

        private readonly Func<string, EntityMetadata> _metadataProvider;

        public override string ToString()
        {
            return _innerRecordType.ToString();
        }

        public override bool TryGetFieldType(string name, out FormulaType type)
        {
            if (_innerRecordType.TryGetFieldType(name, out var fieldType))
            {
                if (fieldType is RecordType recordFieldType && fieldType is not DataverseRecordType)
                {
                    type = new DataverseRecordType(recordFieldType, this._metadataProvider);
                }
                else
                {
                    type = fieldType;
                }

                return true;
            }

            type = default;
            return false;
        }

        public override void Visit(ITypeVisitor vistor)
        {
            _innerRecordType.Visit(vistor);
        }

        public DataverseRecordType(RecordType innerRecordType, Func<string, EntityMetadata> metadataProvider)
            : base(innerRecordType._type)
        {
            _innerRecordType = innerRecordType ?? throw new ArgumentNullException(nameof(innerRecordType));
            _metadataProvider = metadataProvider;
        }

        public override bool Equals(object other)
        {
            if (other is DataverseRecordType dvRecordType)
            {
                return _innerRecordType.Equals(dvRecordType._innerRecordType);
            }

            var result = _innerRecordType.Equals(other);
            return result;
        }

        public override int GetHashCode()
        {
            return _innerRecordType.GetHashCode();
        }

        public override bool TryGetPrimaryKeyFieldName(out string primaryKeyColumn)
        {
            primaryKeyColumn = PrimaryKeyFieldName();
            if (primaryKeyColumn != null)
            {
                return true;
            }

            return false;
        }

        public override TableType ToTable()
        {
            return new DataverseTableType(this);
        }
    }
}
