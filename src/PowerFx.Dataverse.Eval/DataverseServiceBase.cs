//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Connectors;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse
{
    internal static class QueryExtensions
    { 
        public static async Task<DataverseResponse<EntityCollection>> QueryAsync(this IDataverseReader reader, string tableName, ODataParameters odataParameters, int maxRows = 1000, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!odataParameters.IsSupported())
                throw new NotSupportedException($"Unsupported OData query");

            QueryExpression query = new(tableName);
            query.ColumnSet.AllColumns = true;

            if (maxRows > 0)
            {
                query.PageInfo = new PagingInfo();

                // use one more row to determine if the table has more rows than expected
                query.PageInfo.Count = maxRows + 1;
                query.PageInfo.PageNumber = 1;
                query.PageInfo.PagingCookie = null;
            }

            if (odataParameters.Top > 0)
                query.TopCount = odataParameters.Top;

            cancellationToken.ThrowIfCancellationRequested();

            DataverseResponse<EntityCollection> response = await reader.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false);

            //if (maxRows > 0 && response.Response.Entities.Count > maxRows)
            //{
            //    throw new TooManyEntitiesException(tableName, maxRows);
            //}

            return response;
        }
    }
}
