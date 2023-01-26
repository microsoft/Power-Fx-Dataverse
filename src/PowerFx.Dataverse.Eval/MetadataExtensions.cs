//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Connectors;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Security;
using static Microsoft.PowerFx.Dataverse.DataverseHelpers;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Extensions on various Dataverse SDK classes. 
    /// </summary>
    public static class MetadataExtensions
    {
        /// <summary>
        /// Helper to get all Logical 2 Display name map for the entire org. 
        /// This efficiently fetches the table names, but not the metadata. 
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        public static DisplayNameProvider GetTableDisplayNames(this IOrganizationService client)
        {
            RetrieveAllEntitiesRequest req = new RetrieveAllEntitiesRequest
            {
                EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters.Entity
            };
            var resp = (RetrieveAllEntitiesResponse)client.Execute(req);

            var map = new AllTablesDisplayNameProvider();
            foreach (var entity in resp.EntityMetadata)
            {
                var displayName = GetDisplayName(entity);
                map.Add(entity.LogicalName, displayName);
            }

            return map;
        }

        public static string GetDisplayName(EntityMetadata entity)
        {
            return entity.DisplayCollectionName?.UserLocalizedLabel?.Label ?? entity.LogicalName;
        }
    }
}