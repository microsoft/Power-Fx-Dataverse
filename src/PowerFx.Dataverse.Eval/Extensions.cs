// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse
{
    public static class Extensions
    {
        public static RecordValue GetEnvironmentVariables(this IDataverseReader reader)
        {
            return new DataverseEnvironmentVariablesRecordValue(reader);
        }
    }
}
