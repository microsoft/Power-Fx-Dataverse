//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse
{
    internal static class QueryExtensions
    { 
        public static async Task<DataverseResponse<EntityCollection>> QueryAsync(this IDataverseReader reader, string tableName, int maxRows, CancellationToken cancellationToken = default(CancellationToken))
        {            
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
           
            cancellationToken.ThrowIfCancellationRequested();

            return await reader.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false);
        }
    }
}
