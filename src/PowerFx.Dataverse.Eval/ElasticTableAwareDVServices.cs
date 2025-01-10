// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// This wraps the IDataverseServices and provides additional functionality for elastic tables.
    /// Based on metadata, it will decide whether to use the elastic table API or the regular table API for <see cref="RetrieveAsync"/>.
    /// </summary>
    internal class ElasticTableAwareDVServices
    {
        private readonly IDataverseServices _dataverseServices;

        public IDataverseServices DataverseServices => _dataverseServices;

        private readonly Func<string, EntityMetadata> _metadataResolver;

        public ElasticTableAwareDVServices(IDataverseServices dataverseServices, Func<string, EntityMetadata> metadataResolver)
        {
            _dataverseServices = dataverseServices ?? throw new ArgumentNullException(nameof(dataverseServices));
            _metadataResolver = metadataResolver ?? throw new ArgumentNullException(nameof(metadataResolver));
        }

        public Task<DataverseResponse<Guid>> CreateAsync(Entity entity, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _dataverseServices.CreateAsync(entity, cancellationToken);
        }

        public Task<DataverseResponse> DeleteAsync(string entityName, Guid id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _dataverseServices.DeleteAsync(entityName, id, cancellationToken);
        }

        public Task<DataverseResponse<OrganizationResponse>> ExecuteAsync(OrganizationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _dataverseServices.ExecuteAsync(request, cancellationToken);
        }

        public Task<DataverseResponse<Entity>> RetrieveAsync(string entityName, Guid id, IEnumerable<string> columns, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entityMetadata = _metadataResolver(entityName);

            if (entityMetadata.IsElasticTable())
            {
                return RetrieveEntityFromElasticTableAsync(entityName, id, columns, cancellationToken);
            }
            else
            {
                return _dataverseServices.RetrieveAsync(entityName, id, columns, cancellationToken);
            }
        }

        private async Task<DataverseResponse<Entity>> RetrieveEntityFromElasticTableAsync(string entityName, Guid id, IEnumerable<string> columns, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reference = new EntityReference(entityName, id);
            var filter = new FxFilterExpression();
            filter.AddCondition(_metadataResolver(reference.LogicalName).PrimaryIdAttribute, FxConditionOperator.Equal, reference.Id);
            var columnMap = columns != null ? new FxColumnMap(columns) : null;
#pragma warning disable CS0618 // Type or member is obsolete
            var query = DataverseTableValue.CreateQueryExpression(reference.LogicalName, new DataverseDelegationParameters(null) { FxFilter = filter, Top = 1, ColumnMap = columnMap });
#pragma warning restore CS0618 // Type or member is obsolete
            var rows = await _dataverseServices.RetrieveMultipleAsync(query, cancellationToken);

            if (rows.HasError)
            {
                return DataverseResponse<Entity>.NewError(rows.Error);
            }

            var row = rows.Response.Entities.FirstOrDefault();
            return new DataverseResponse<Entity>(row);
        }

        public Task<DataverseResponse<EntityCollection>> RetrieveMultipleAsync(QueryBase query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _dataverseServices.RetrieveMultipleAsync(query, cancellationToken);
        }

        public Task<DataverseResponse> UpdateAsync(Entity entity, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _dataverseServices.UpdateAsync(entity, cancellationToken);
        }
    }
}
