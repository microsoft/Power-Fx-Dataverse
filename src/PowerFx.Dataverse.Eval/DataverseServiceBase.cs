//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Connectors;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse
{
    internal static class QueryExtensions
    { 
        public static async Task<DataverseResponse<EntityCollection>> QueryAsync(this IDataverseReader reader, string tableName, ODataParameters odataParameters, CancellationToken ct = default(CancellationToken))
        {
            if (!odataParameters.IsSupported())
                throw new NotSupportedException($"Unsupported OData query");

            QueryExpression qe = new(tableName);
            qe.ColumnSet.AllColumns = true;

            if (odataParameters.Top > 0)
                qe.TopCount = odataParameters.Top;

            return await reader.RetrieveMultipleAsync(qe, ct);
        }
    }
}
