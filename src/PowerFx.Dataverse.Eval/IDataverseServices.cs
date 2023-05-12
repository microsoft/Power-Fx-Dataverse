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
    public interface IDataverseServices : IDataverseCreator, IDataverseReader, IDataverseUpdater, IDataverseDeleter
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
        Task RefreshAsync(string logicalTableName, CancellationToken cancellationToken = default(CancellationToken));
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
}
