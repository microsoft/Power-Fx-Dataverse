//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    internal class DataverseAsyncClient : IDataverseServices, IDisposable
    {
        private readonly IOrganizationServiceAsync2 _svcClient;
        private bool disposedValue;

        public DataverseAsyncClient(IOrganizationServiceAsync2 client)
        {
            _svcClient = client ?? throw new ArgumentNullException(nameof(client));
        }

        public virtual async Task<DataverseResponse<Guid>> CreateAsync(Entity entity, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => _svcClient.CreateAsync(entity, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult(), "Create");
        }

        public virtual async Task<DataverseResponse> DeleteAsync(string entityName, Guid id, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => _svcClient.DeleteAsync(entityName, id, cancellationToken).ConfigureAwait(false), "Delete");
        }

        public virtual async Task<DataverseResponse<Entity>> RetrieveAsync(string entityName, Guid id, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => _svcClient.RetrieveAsync(entityName, id, new ColumnSet(true), cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult(), "Retrieve");
        }

        public virtual async Task<DataverseResponse<EntityCollection>> RetrieveMultipleAsync(QueryBase query, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => _svcClient.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult(), "RetrieveMultiple");
        }

        public virtual async Task<DataverseResponse> UpdateAsync(Entity entity, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => { _svcClient.UpdateAsync(entity, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult(); return entity; }, "Update");
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (_svcClient is IDisposable disposableClient)
                    {
                        disposableClient.Dispose();
                    }
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public void Refresh(string logicalTableName)
        {            
        }
    }
}
