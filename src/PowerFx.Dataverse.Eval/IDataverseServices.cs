//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Interpreter;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse
{
    public interface IDataverseServices : IDataverseCreator, IDataverseReader, IDataverseUpdater, IDataverseDeleter, IDataverseExecute
    {
    }

    // Channel for IOrganizationService.Execute()  
    public interface IDataverseExecute
    {
        Task<DataverseResponse<OrganizationResponse>> ExecuteAsync(OrganizationRequest request, CancellationToken cancellationToken = default);
    }

    public interface IDataverseCreator
    {
        Task<DataverseResponse<Guid>> CreateAsync(Entity entity, CancellationToken cancellationToken = default);
    }

    public interface IDataverseReader
    {        
        Task<DataverseResponse<Entity>> RetrieveAsync(string entityName, Guid id, IEnumerable<string> columns, CancellationToken cancellationToken = default);

        Task<DataverseResponse<EntityCollection>> RetrieveMultipleAsync(QueryBase query, CancellationToken cancellationToken = default);        
    }

    // Optional interface to enable clearing any caches.
    public interface IDataverseRefresh
    {
        void Refresh(string logicalTableName);
    }

    public interface IDataverseUpdater
    {
        // Entity can contain just the fields to update.
        // Return fully updated entity 
        Task<DataverseResponse> UpdateAsync(Entity entity, CancellationToken cancellationToken = default);
    }
    public interface IDataverseDeleter
    {
        Task<DataverseResponse> DeleteAsync(string entityName, Guid id, CancellationToken cancellationToken = default);
    }

    public class DataverseNotPresent : IDataverseExecute
    {
        public Task<DataverseResponse<OrganizationResponse>> ExecuteAsync(OrganizationRequest request, CancellationToken cancellationToken = default)
        {
            throw new CustomFunctionErrorException("AI functions require a connection to Dataverse. Connect and try again.", ErrorKind.ServiceUnavailable);
        }
    }
}
