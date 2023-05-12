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
    public class DataverseService : IDataverseServices
    {        
        private IOrganizationService _organizationService { get; }

        public DataverseService(IOrganizationService service)
        {
            _organizationService = service ?? throw new ArgumentNullException(nameof(service));
        }

        public virtual async Task<DataverseResponse<Entity>> RetrieveAsync(string logicalName, Guid id, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => _organizationService.Retrieve(logicalName, id, new ColumnSet(true)), $"Retrieve '{logicalName}':{id}");
        }

        public virtual async Task<DataverseResponse<Guid>> CreateAsync(Entity entity, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => _organizationService.Create(entity), $"Create '{entity.LogicalName}'");
        }

        public virtual async Task<DataverseResponse> UpdateAsync(Entity entity, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall((Func<bool>)(() => { this._organizationService.Update(entity); return true; }), $"Update '{entity.LogicalName}':{entity.Id}");
        }

        public virtual async Task<DataverseResponse<EntityCollection>> RetrieveMultipleAsync(QueryBase query, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => _organizationService.RetrieveMultiple(query), $"Query {query} returned nothing");
        }

        public virtual async Task<DataverseResponse> DeleteAsync(string entityName, Guid id, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();            
            return DataverseExtensions.DataverseCall((Func<bool>)(() => { this._organizationService.Delete(entityName, id); return true; }), $"Delete '{entityName}':{id}");
        }

        internal virtual HttpResponseMessage ExecuteWebRequest(HttpMethod method, string queryString, string body, Dictionary<string, List<string>> customHeaders, string contentType = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public Task RefreshAsync(string logicalTableName, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
