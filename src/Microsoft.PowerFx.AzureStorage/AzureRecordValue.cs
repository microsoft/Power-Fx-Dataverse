// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.PowerFx.Dataverse;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.AzureStorage
{
    internal class AzureRecordValue : ColumnMapRecordValue
    {
        public AzureRecordValue(RecordValue recordValue, IReadOnlyDictionary<string, string> columnMap = null)
            : base(recordValue, columnMap)
        {
        }
    }
}
