//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.AppMagic.Authoring;
using Microsoft.AppMagic.Authoring.Importers.DataDescription;
using Microsoft.AppMagic.Authoring.Importers.ServiceConfig;
using System;
using System.Linq;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;

namespace Microsoft.PowerFx.Dataverse.DataSource
{
    /// <summary>
    /// Wraps a DType and updates it as necessary with referenced CDS types
    /// </summary>
    internal class DataverseType
    {
        /// <summary>
        /// The wrapped DType
        /// </summary>
        public DType Type;

        /// <summary>
        /// Gets the CDS column definition for a path, with an optional navigation
        /// Has the side effect of adding the navigated entity to the wrapped type
        /// </summary>
        /// <param name="path">The path to the column</param>
        /// <param name="navigation">The navigation type definition.  Optional</param>
        /// <returns></returns>
        public CdsColumnDefinition GetColumn(DPath path, CdsNavigationTypeDefinition navigation = null)
        {
            var type = Type;
            if (navigation != null)
            {
                var navPath = path.Parent;
                type = type.GetType(navPath);
                if (type.Kind == DKind.DataEntity)
                {
                    if (type.AssociatedDataSources.First().DataEntityMetadataProvider.TryGetEntityMetadata(navigation.TargetTableNames[0], out var entity))
                    {
                        var fError = false;
                        type = entity.Schema;
                        Type = Type.SetType(ref fError, navPath, entity.Schema);
                    }
                    else
                    {
                        throw new NotImplementedException($"Unsupported navigation");
                    }
                }
            }
            else if (path.Length > 1)
            {
                var parent = path.Parent;
                type = type.GetType(parent);
            }
            if (!DType.TryGetLogicalNameForColumn(type, path.Name, out var logicalName))
            {
                logicalName = path.Name;
            }
            return type.CdsColumnDefinitionOrDefault(logicalName);
        }

        public CdsTableDefinition GetTable(string logicalName)
        {
            if (Type.AssociatedDataSources.First().DataEntityMetadataProvider.TryGetEntityMetadata(logicalName, out var entity) &&
                entity is DataverseDataSourceInfo dsInfo)
            {
                return dsInfo.CdsTableDefinition;
            }

            throw new NotImplementedException($"Unknown table ${logicalName}");
        }
    }
}
