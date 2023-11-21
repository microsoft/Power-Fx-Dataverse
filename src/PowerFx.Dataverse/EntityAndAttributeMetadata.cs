using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.PowerFx.Dataverse
{
    public sealed class CDSEntityMetadata
    {
        private string _logicalName;
        private string _baseTableName;
        private bool _isInheritsFromNull;
        private string _primaryIdAttribute;

        public string LogicalName
        {
            get => _logicalName;
            set => _logicalName = value;
        }

        public string BaseTableName
        {
            get => _baseTableName;
            set => _baseTableName = value;
        }

        public bool IsInheritsFromNull
        {
            get => _isInheritsFromNull;
            set => _isInheritsFromNull = value;
        }

        public string PrimaryIdAttribute
        {
            get => _primaryIdAttribute;
            set => _primaryIdAttribute = value;
        }
    }

    public sealed class CDSAttributeMetadata
    {
        private string _logicalName;
        private bool _isStoredOnPrimaryTable;

        public string LogicalName
        {
            get => _logicalName;
            set => _logicalName = value;
        }

        public bool IsStoredOnPrimaryTable
        {
            get => _isStoredOnPrimaryTable;
            set => _isStoredOnPrimaryTable = value;
        }
    }
}
