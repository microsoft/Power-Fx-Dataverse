//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Core;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

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
            var map = new AllTablesDisplayNameProvider();
            foreach (var entity in client.GetAllValidEntityMetadata())
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
