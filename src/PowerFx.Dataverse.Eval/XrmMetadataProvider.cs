﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse
{
    public class XrmMetadataProvider : IXrmMetadataProvider
    {
        private readonly IOrganizationService _serviceClient;

        public XrmMetadataProvider(IOrganizationService svcClient)
        {
            _serviceClient = svcClient;
        }

        public bool TryGetLogicalName(string displayName, out string logicalName)
        {
            var ce = new ConditionExpression();
            ce.AttributeName = "originallocalizedcollectionname";
            ce.Operator = ConditionOperator.Equal;
            ce.Values.Add(displayName);

            var fe = new FilterExpression();
            fe.Conditions.Add(ce);

            var qe = new QueryExpression("entity");
            qe.ColumnSet.AddColumn("logicalname");
            qe.Criteria.AddFilter(fe);

            var resp = DataverseExtensions.DataverseCall<EntityCollection>(
                () => _serviceClient.RetrieveMultiple(qe), $"Get logical name for '{displayName}'");

            if (!resp.HasError)
            {
                var result = resp.Response.Entities.FirstOrDefault();
                if (result == null)
                {
                    logicalName = null;
                    return false;
                }

                logicalName = result.Attributes["logicalname"].ToString();
                return true;
            }

            logicalName = null;
            return false;
        }

        public bool TryGetEntityMetadataFromDisplayName(string displayName, out EntityMetadata entityMetadata)
        {
            if (TryGetLogicalName(displayName, out var logicalName))
            {
                return TryGetEntityMetadata(logicalName, out entityMetadata);
            }

            entityMetadata = null;
            return false;
        }

        public bool TryGetEntityMetadata(string logicalName, out EntityMetadata entityMetadata)
        {
            return _serviceClient.TryGetValidEntityMetadata(logicalName, out entityMetadata);
        }

        // We should *never* call this API. Too expensive.
        // 236 mb working set , 196.28412 mb allocations ,  9104ms , 517 entities
        [Obsolete("Bad perf API - avoid this. Use SingleOrgPolicy instead for lazy loading.")]
        public bool GetEntitiesMetadata(out EntityMetadata[] entities)
        {
            return _serviceClient.Execute<RetrieveAllEntitiesRequest, RetrieveAllEntitiesResponse, EntityMetadata[]>(
                new RetrieveAllEntitiesRequest() { EntityFilters = EntityFilters.All },
                raer => raer.EntityMetadata,
                out entities);
        }
    }
}
