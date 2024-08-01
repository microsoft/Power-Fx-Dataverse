// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using Microsoft.PowerFx.Core;
using Microsoft.Xrm.Sdk;
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
        /// Returns true if the field is a polymorphic lookup field.
        /// </summary>
        /// <param name="fieldName">Logical name of the field.</param>
        /// <returns></returns>
        public static bool IsFieldPolymorphic(this EntityMetadata entityMetadata, string fieldName)
        {
            if (entityMetadata.TryGetAttribute(fieldName, out var fieldMetadata) ||
                (AttributeUtility.TryGetLogicalNameFromOdataName(fieldName, out var realFieldName) && entityMetadata.TryGetAttribute(realFieldName, out fieldMetadata)))
            {
                return fieldMetadata.IsFieldPolymorphic();
            }

            return false;
        }

        /// <summary>
        /// Returns true if the field is a polymorphic lookup field.
        /// </summary>
        /// <param name="fieldName">Logical name of the field.</param>
        /// <returns></returns>
        public static bool IsFieldPolymorphic(this AttributeMetadata attributeMetadata)
        {
            if (attributeMetadata is LookupAttributeMetadata lookupAttributeMetadata && lookupAttributeMetadata.Targets.Length > 1)
            {
                return true;
            }

            return false;
        }
    }
}
