// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse
{
    internal static class Extensions
    {
        public static async Task<RecordValue> GetEnvironmentVariablesAsync(this IDataverseReader reader)
        {
            var filter = new FilterExpression();

            // Data source and Secret types are not supported.
            filter.FilterOperator = LogicalOperator.Or;
            filter.AddCondition("type", ConditionOperator.Equal, (int)EnvironmentVariableType.Decimal);
            filter.AddCondition("type", ConditionOperator.Equal, (int)EnvironmentVariableType.String);
            filter.AddCondition("type", ConditionOperator.Equal, (int)EnvironmentVariableType.JSON);
            filter.AddCondition("type", ConditionOperator.Equal, (int)EnvironmentVariableType.Boolean);

            var definitions = await reader.RetrieveMultipleAsync<EnvironmentVariableDefinitionEntity>(filter, CancellationToken.None);
            var logicalToDisplayNames = new List<KeyValuePair<string, string>>();

            foreach (var definition in definitions)
            {
                logicalToDisplayNames.Add(new KeyValuePair<string, string>(definition.schemaname, definition.displayname));
            }

            return new DataverseEnvironmentVariablesRecordValue(
                new DataverseEnvironmentVariablesRecordType(DisplayNameUtility.MakeUnique(logicalToDisplayNames), definitions), reader);
        }
    }
}
