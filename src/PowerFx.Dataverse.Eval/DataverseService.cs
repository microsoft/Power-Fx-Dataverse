//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse
{
    internal class DataverseService : IDataverseServices
    {
        private readonly IOrganizationService _organizationService;

        internal DataverseService(IOrganizationService service)
        {            
            _organizationService = service;
        }

        public async Task<DataverseResponse<Entity>> RetrieveAsync(string logicalName, Guid id, CancellationToken ct = default(CancellationToken))
        {
            return DataverseExtensions.DataverseCall(() => _organizationService.Retrieve(logicalName, id, new ColumnSet(true)), $"Entity {logicalName}:{id} not found");            
        }       

        public async Task<DataverseResponse<Guid>> CreateAsync(Entity entity, CancellationToken ct = default(CancellationToken))
        {
            return DataverseExtensions.DataverseCall(() => _organizationService.Create(entity), $"Entity creation failed");            
        }

        public async Task<DataverseResponse<Entity>> UpdateAsync(Entity entity, CancellationToken ct = default(CancellationToken))
        {
            return DataverseExtensions.DataverseCall(() => { _organizationService.Update(entity); return entity; });           
        }

        public async Task<DataverseResponse<EntityCollection>> RetrieveMultipleAsync(QueryBase query, CancellationToken ct = default(CancellationToken))
        {
            return DataverseExtensions.DataverseCall(() => _organizationService.RetrieveMultiple(query), $"Query {query.ToString()} returned nothing");            
        }

        public HttpResponseMessage ExecuteWebRequest(HttpMethod method, string queryString, string body, Dictionary<string, List<string>> customHeaders, string contentType = null, CancellationToken cancellationToken = default(CancellationToken))
        {            
            throw new NotImplementedException();
        }

        public async Task<DataverseResponse> DeleteAsync(string entityName, Guid id, CancellationToken ct = default(CancellationToken))
        {
            return DataverseExtensions.DataverseCall(() => _organizationService.Delete(entityName, id));            
        }      
    }
}
