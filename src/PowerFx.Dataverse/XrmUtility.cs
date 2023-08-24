//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------


using System;
using System.Collections.Generic;
using System.Text;

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
    }
}
