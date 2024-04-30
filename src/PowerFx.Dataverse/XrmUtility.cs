// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

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
            Owner = 7,
            ActivityParty = 135,
            UserMapping = 2016,
            ProcessTrigger = 4712,
            // ProcessStage = 4724, // Removing from blacklist due to customer ask
            MobileOfflineProfile = 9866,
            MobileOfflineProfileItem = 9867,
            MobileOfflineProfileItemAssociation = 9868,
            RecommendationModel = 9933,
            RecommendationModelMapping = 9934,
            RecommendationModelVersion = 9935,
            TopicModelConfiguration = 9942,
            TopicModel = 9944,
            TextAnalyticsEntityMapping = 9945,
            KnowledgeSearchModel = 9947,
            AdvancedSimilarityRule = 9949,
            SimilarityRule = 9951
        }

        internal static bool IsValid(this EntityMetadata entityMetadata)
        {
            var isValidIntersect = (entityMetadata.IsIntersect ?? false) == false;
            var isValueLogicalEntity = (entityMetadata.IsLogicalEntity ?? false) == false;
            var isValidPrimaryNameAttribute = !string.IsNullOrEmpty(entityMetadata.PrimaryNameAttribute);

            //// PA filters out some entities by a pre-defined lists of entities.
            //// Pre-defined lists: BlackListEntities SalesEntity (not implemented), ServiceEntity (not implemented), MarketingEntity (not implemented).
            //// PA returns a total of ~ 501 entities. PFx Dataverse is returning ~517 due to the not implemented lists.
            var objectTypeCode = entityMetadata.ObjectTypeCode ?? 0;
            var isValueObjectTypeCode = objectTypeCode > 0 && Enum.IsDefined(typeof(BlackListEntities), objectTypeCode) == false;

            var isValidCustomizable = (entityMetadata.IsCustomizable?.Value ?? false) == false;
            var isValidManaged = (entityMetadata.IsManaged ?? false) == false;
            var isValidMappable = (entityMetadata.IsMappable?.Value ?? false) == false;
            var isValidRenameable = (entityMetadata.IsRenameable?.Value ?? false) == false;
            var isValidPrivate = (entityMetadata.IsPrivate ?? false) == false;
            var isValidCustomEntity = (entityMetadata.IsCustomEntity ?? false) == false;

            return isValidIntersect && isValueLogicalEntity && isValidPrimaryNameAttribute && isValueObjectTypeCode && isValidPrivate &&
                   isValidCustomizable && isValidManaged && isValidMappable && isValidRenameable && isValidCustomEntity;
        }

        public static IEnumerable<EntityMetadata> GetAllValidEntityMetadata(this IOrganizationService client, EntityFilters entityFilters = EntityFilters.Entity)
        {
            RetrieveAllEntitiesRequest req = new RetrieveAllEntitiesRequest
            {
                EntityFilters = entityFilters
            };

            var resp = (RetrieveAllEntitiesResponse)client.Execute(req);

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
