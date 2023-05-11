//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.AppMagic.Authoring.Importers.DataDescription;
using Microsoft.AppMagic.Authoring.Importers.ServiceConfig;
using Microsoft.PowerFx.Core.Binding;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Logging;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.DataSource;
using Microsoft.PowerFx.Syntax;

namespace Microsoft.PowerFx.Dataverse
{
    internal class NameSanitizer : ISanitizedNameProvider
    {
        private DataverseType _scopeType;        

        public NameSanitizer(TexlBinding binding)
        {            
            _scopeType = new DataverseType { Type = binding.ContextScope };
        }

        /// <summary>
        /// Attempt to sanitize a first name node using a custom sanitization scheme.
        /// </summary>
        /// <param name="identifier">The identifier.</param>
        /// <param name="sanitizedName">The sanitized name output.</param>
        /// <returns>Whether the custom sanitization should be used.</returns>
        public bool TrySanitizeIdentifier(Identifier identifier, out string sanitizedName, DottedNameNode dottedNameNode = null)
        {
            CdsColumnDefinition column;
            if (dottedNameNode == null)
            {
                var path = GetPathFromIdentifier(_scopeType, identifier);
                column = _scopeType.GetColumn(path);
            }
            else
            {
                column = GetColumn(_scopeType, dottedNameNode);
            }

            if (column != default)
            {
                sanitizedName = $"#$Field{column.TypeCode}$#";
                return true;
            }

            sanitizedName = default;
            return false;
        }

        private static (CdsColumnDefinition, DPath) GetColumnAndPath(DataverseType scopeType, TexlNode node)
        {
            if (node is FirstNameNode firstName)
            {
                var path = GetPathFromIdentifier(scopeType, firstName.Ident);
                return (scopeType.GetColumn(path), path);
            }
            else if (node is DottedNameNode dottedName)
            {
                var lookupPath = GetPath(scopeType, dottedName.Left);
                var lookup = scopeType.GetColumn(lookupPath);
                if (lookup != null && lookup.IsNavigation && lookup.TypeDefinition is CdsNavigationTypeDefinition navType)
                {
                    var path = lookupPath.Append(GetNameFromIdentifier(scopeType.Type.GetType(lookupPath), dottedName.Right));
                    var column = scopeType.GetColumn(path, navType);
                    return (column, path);
                }
            }
            return (null,DPath.Root);
        }  

        private static DPath GetPath(DataverseType scopeType, TexlNode node)
        {
            var (_, path) = GetColumnAndPath(scopeType, node);
            return path;
        }

        internal static CdsColumnDefinition GetColumn(DataverseType scopeType, TexlNode node)
        {
            var (column, _) = GetColumnAndPath(scopeType, node);
            return column;
        }      

        internal static DPath GetPathFromIdentifier(DataverseType scopeType, Identifier identifier)
        {
            var path = new DPath();
            return path.Append(GetNameFromIdentifier(scopeType.Type, identifier));
        }

        private static DName GetNameFromIdentifier(DType type, Identifier identifier)
        {
            if (DType.TryGetLogicalNameForColumn(type, identifier.Name.Value, out var logicalName))
            {
                return new DName(logicalName);
            }
            else
            {
                return identifier.Name;
            }
        }

        private DPath GetPath(TexlNode node)
        {
            if (node is FirstNameNode firstName)
            {
                var path = new DPath().Append(firstName.Ident.Name);
                return path;
            }
            else if (node is DottedNameNode dottedName)
            {
                var lookupPath = GetPath(dottedName.Left);
                var lookup = _scopeType.GetColumn(lookupPath);
                if (lookup.IsNavigation && lookup.TypeDefinition is CdsNavigationTypeDefinition navType)
                {
                    var path = lookupPath.Append(dottedName.Right.Name);
                    _scopeType.GetColumn(path, navigation: navType);
                    return path;
                }
            }
            return DPath.Root;
        }
    }
}
