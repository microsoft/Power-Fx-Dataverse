﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse
{
    public class DataverseService : IDataverseServices, IDataverseRefresh
    {
        private IOrganizationService OrganizationService { get; }

        public DataverseService(IOrganizationService service)
        {
            OrganizationService = service ?? throw new ArgumentNullException(nameof(service));

            // Patch of Decimal values does not work properly with Microsoft.PowerPlatform.Dataverse.Client and UseWebApi=true.
            // Version 1.0.23 and higher changed the default to false, but for those who have an older version
            // or have set it to true we catch it here.  We don't need to check this for NumberIsFloat operation, however,
            // we should start enforcing it there too for the day that those hosts enable Decimal.
            // https://www.nuget.org/packages/Microsoft.PowerPlatform.Dataverse.Client#release-body-tab
            if ((bool?)service.GetType().GetProperty("UseWebApi")?.GetValue(service, null) == true)
            {
                throw new ArgumentException("Use of ServiceClient with UseWebApi=true is not supported. Upgrade to a newer version of ServiceClient or set UseWebApi to false.");
            }
        }

        public virtual async Task<DataverseResponse<Entity>> RetrieveAsync(string logicalName, Guid id, IEnumerable<string> columns, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var columnSet = columns.ToXRMColumnSet();
            return DataverseExtensions.DataverseCall(() => OrganizationService.Retrieve(logicalName, id, columnSet), $"Retrieve '{logicalName}':{id}");
        }

        public virtual async Task<DataverseResponse<Guid>> CreateAsync(Entity entity, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => OrganizationService.Create(entity), $"Create '{entity.LogicalName}'");
        }

        public virtual async Task<DataverseResponse> UpdateAsync(Entity entity, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return DataverseExtensions.DataverseCall(
                () =>
                {
                    this.OrganizationService.Update(entity);
                    return true;
                },
                $"Update '{entity.LogicalName}':{entity.Id}");
        }

        public virtual async Task<DataverseResponse<EntityCollection>> RetrieveMultipleAsync(QueryBase query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => OrganizationService.RetrieveMultiple(query), $"Query {query} returned nothing");
        }

        public virtual async Task<DataverseResponse> DeleteAsync(string entityName, Guid id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(
                () =>
                {
                    this.OrganizationService.Delete(entityName, id);
                    return true;
                },
                $"Delete '{entityName}':{id}");
        }

        internal virtual HttpResponseMessage ExecuteWebRequest(HttpMethod method, string queryString, string body, Dictionary<string, List<string>> customHeaders, string contentType = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotImplementedException();
        }

        public void Refresh(string logicalTableName)
        {
        }

        public virtual async Task<DataverseResponse<OrganizationResponse>> ExecuteAsync(OrganizationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => OrganizationService.Execute(request), $"Execute '{request.RequestName}'");
        }
    }
}
