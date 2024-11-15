// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation
{
    /// <summary>
    /// metadata for FieldInforecord. {fieldName: "fieldToComapre", fieldFunctions: ["enum"]}. <see cref="FieldFunction"/> }.
    /// </summary>
    internal static class FieldInfoRecord
    {
        public const string FieldNameColumnName = "fieldName";
        public const string FieldFunctionColumnName = "fieldFunctions";
        
        public static string SingleColumnTableColumnName => TableValue.ValueName;

        public static DName SingleColumnTableColumnDName => TableValue.ValueDName;
    }
}
