//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse
{
    public interface IDataverseServices : IDataverseCreator, IDataverseReader, IDataverseUpdater, IDataverseDeleter, IDataverseLookup
    {
    }

    public interface IDataverseCreator
    {
        Task<DataverseResponse<Guid>> CreateAsync(Entity entity, CancellationToken cancellationToken = default(CancellationToken));
    }

    public interface IDataverseReader
    {
        Task<DataverseResponse<Entity>> RetrieveAsync(string entityName, Guid id, CancellationToken cancellationToken = default(CancellationToken));
        Task<DataverseResponse<EntityCollection>> RetrieveMultipleAsync(QueryBase query, CancellationToken cancellationToken = default(CancellationToken));        
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
        Task<DataverseResponse> UpdateAsync(Entity entity, CancellationToken cancellationToken = default(CancellationToken));
    }
    public interface IDataverseDeleter
    {
        Task<DataverseResponse> DeleteAsync(string entityName, Guid id, CancellationToken cancellationToken = default(CancellationToken));
    }

    public interface IDataverseLookup
    {
        /// <summary>
        /// Resolve an entity reference to an Entity instance. 
        /// This returns a new copy of the entity. 
        /// </summary>
        /// <param name="reference"></param>
        /// <returns></returns>
        Task<DataverseResponse<Entity>> LookupReferenceAsync(EntityReference reference, CancellationToken cancellationToken = default);
    }
}
