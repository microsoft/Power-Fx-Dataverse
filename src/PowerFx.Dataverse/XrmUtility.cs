//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------


using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace Microsoft.PowerFx.Dataverse
{
    public static class XrmUtility
    {
        /// <summary>
        /// Object type codes for blacklisted entities.
        /// Todo: PA to share this list.
        /// </summary>
        private enum BlackListEntities
        {
            TopicModel = 9944,
            UserMapping = 2016,
            MobileOfflineProfileItem = 9867,
            MobileOfflineProfileItemAssociation = 9868,
            AdvancedSimilarityRule = 9949,
            TopicModelConfiguration = 9942,
            KnowledgeSearchModel = 9947,
            ProcessTrigger = 4712,
            SimilarityRule = 9951,
            MobileOfflineProfile = 9866,
            // ProcessStage = 4724, // Removing from blacklist due to customer ask
            TextAnalyticsEntityMapping = 9945,
            RecommendationModel = 9933,
            RecommendationModelMapping = 9934,
            RecommendationModelVersion = 9935,
            Owner = 7,
            ActivityParty = 135
        }

        public static int[] BlackListedEntities()
        {
            return Enum.GetValues(typeof(BlackListEntities)) as int[];
        }

        public static bool IsValid(this EntityMetadata entityMetadata)
        {
            var isIntersect = entityMetadata.IsIntersect ?? false;
            var isLogicalEntity = entityMetadata.IsLogicalEntity ?? false;
            var objectTypeCode = entityMetadata.ObjectTypeCode ?? 0;
            var isPrivate = entityMetadata.IsPrivate ?? false;

            // PA filters out some entities by a pre-defined lists of entities.
            // Pre-defined lists: BlackListEntities SalesEntity (not implemented), ServiceEntity (not implemented), MarketingEntity (not implemented).
            // PA returns a total of ~ 501 entities. PFx Dataverse is returning ~517 due to the not implemented lists.
            var isInvalidEntity = Array.IndexOf(array: XrmUtility.BlackListedEntities(), objectTypeCode) != -1;

            return !(isIntersect || isLogicalEntity || isPrivate || objectTypeCode == 0 || isInvalidEntity);
        }

        public static IEnumerable<EntityMetadata> GetAllValidEntityMetadata(this IOrganizationService client, EntityFilters entityFilters = EntityFilters.Entity)
        {
            RetrieveAllEntitiesRequest req = new RetrieveAllEntitiesRequest
            {
                EntityFilters = entityFilters
            };

            var resp = (RetrieveAllEntitiesResponse) client.Execute(req);

            foreach (var entity in resp.EntityMetadata.Where(entity => entity.IsValid()))
            {
                yield return entity;
            }
        }

        public static bool TryGetValidEntityMetadata(this IOrganizationService client, string logicalName, out EntityMetadata entityMetadata)
        {
            var request = new RetrieveEntityRequest
            {
                EntityFilters = EntityFilters.All, // retrieve all possible properties
                LogicalName = logicalName
            };

            var response = (RetrieveEntityResponse)client.Execute(request);

            if (response.EntityMetadata != null && response.EntityMetadata.IsValid())
            {
                entityMetadata = response.EntityMetadata;
                return true;
            }

            entityMetadata = null;
            return false;
        }
    }
}
