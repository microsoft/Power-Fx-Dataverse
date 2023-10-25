//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse
{
    public interface IDataverseServices : IDataverseCreator, IDataverseReader, IDataverseUpdater, IDataverseDeleter
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
        Task<DataverseResponse<Entity>> RetrieveAsync(string entityName, Guid id, CancellationToken cancellationToken = default);
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

    public interface IDataversePlugInContext : IDataverseReader, IDataverseExecute
    {
        Task<CustomApiSignature> GetPlugInAsync(string name, CancellationToken cancellationToken = default);

        void AddPlugIn(CustomApiSignature signature);

        Task<FormulaValue> ExecutePlugInAsync(RuntimeConfig config, string name, RecordValue arguments, CancellationToken cancellationToken = default);
    }
}
