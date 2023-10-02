//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel;
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

        /// <summary>
        /// Checks whether attribute is a read-only attribute.
        /// </summary>
        /// <param name="attributeMetadata">XRM SDK attribute metadata</param>
        /// <remarks>...\Cloud\DocumentServer.Core\XrmDataProvider\CdsPatchDatasourceHelper.cs</remarks>
        /// <returns>True on success, false otherwise</returns>
        public static bool IsReadOnly(this AttributeMetadata attributeMetadata)
        {
            return attributeMetadata.IsPrimaryId == true
                || attributeMetadata.SourceType == (int)AttributeDataSourceType.Calculated
                || attributeMetadata.SourceType == (int)AttributeDataSourceType.Rollup
                || attributeMetadata.AttributeType == AttributeTypeCode.State
                || (attributeMetadata.AttributeType == AttributeTypeCode.Money
                    && attributeMetadata is MoneyAttributeMetadata { IsBaseCurrency: true })
                || ReadOnlyAttributes.Contains(attributeMetadata.LogicalName);
        }

        /// <summary>
        /// Indicates the source type for a calculated or rollup attribute.
        /// </summary>
        /// <remarks>...\Cloud\DocumentServer.Core\XrmDataProvider\CdsPatchDatasourceHelper.cs</remarks>
        internal enum AttributeDataSourceType
        {
            Persistent = 0,
            Calculated = 1,
            Rollup = 2
        }

        private static readonly ICollection<string> ReadOnlyAttributes = new HashSet<string>()
        {
            "createdby",
            "createdon",
            "fullname",
            "modifiedon",
            "modifiedby",
            "ownerid",
            "ticketnumber"
        };
    }
}
