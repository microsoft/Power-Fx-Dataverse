// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Linq;
using Microsoft.AppMagic.Authoring.Importers.DataDescription;
using Microsoft.AppMagic.Authoring.Importers.ServiceConfig;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;
using XrmAttributeTypeCode = Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode;

namespace Microsoft.PowerFx.Dataverse
{
    internal static class CdsColumnDefinitionExtensions
    {
        internal static bool RequiresReference(this CdsColumnDefinition column)
        {
            // calculated and logical (e.g. address) fields can not be used in the calculated field definition in SQL, but can only be referenced via select against the Dataverse view inside the function definition
            // * calculated fields are not allowed by SQL, to limit circular references, etc.
            // * logical fields are not stored in the raw base table
            return column.IsCalculated || column.IsLogical;
        }

        internal static FormulaType FormulaType(this XrmAttributeTypeCode typeCode)
        {
            if (typeCode.TryGetFormulaType(out var type))
            {
                return type;
            }

            throw new Exception($"Cannot convert {typeCode} to formula type");
        }

        /// <summary>
        /// Tries to convert an attribute type to a formula type.
        /// Does not address the difference between date time behaviors or other metadata that may affect the formula type
        /// </summary>
        /// <param name="typeCode">The type code</param>
        /// <param name="type">The output formula type</param>
        /// <returns>A boolean that indicates whether the conversion is supported</returns>
        internal static bool TryGetFormulaType(this XrmAttributeTypeCode typeCode, out FormulaType type)
        {
            switch (typeCode)
            {
                case XrmAttributeTypeCode.Integer:
                case XrmAttributeTypeCode.Money:
                case XrmAttributeTypeCode.Decimal:
                    type = Types.FormulaType.Decimal;
                    return true;

                case XrmAttributeTypeCode.Double:
                    type = Types.FormulaType.Number;
                    return true;

                case XrmAttributeTypeCode.String:
                    type = Types.FormulaType.String;
                    return true;

                case XrmAttributeTypeCode.Uniqueidentifier:
                case XrmAttributeTypeCode.Lookup:
                    type = new GuidType();
                    return true;

                case XrmAttributeTypeCode.Boolean:
                    type = Types.FormulaType.Boolean;
                    return true;

                case XrmAttributeTypeCode.Picklist:
                case XrmAttributeTypeCode.State:
                case XrmAttributeTypeCode.Status:
                    type = Types.FormulaType.OptionSetValue;
                    return true;

                case XrmAttributeTypeCode.DateTime:

                    // Return the default formula type
                    type = Types.FormulaType.DateTime;
                    return true;

                default:
                    type = default;
                    return false;
            }
        }

        internal static CdsTableDefinition CdsTableDefinition(this DType type)
        {
            Contracts.AssertNonEmptyOrNull(type.AssociatedDataSources);
            if (type.AssociatedDataSources.First() is DataverseDataSourceInfo dsInfo)
            {
                return dsInfo.CdsTableDefinition;
            }

            throw new Exception("Unsupported data source");
        }

        internal static bool TryGetAssociateDataSource(this FormulaType type, out IExternalDataSource ads)
        {
            return type._type.TryGetAssociateDataSource(out ads);
        }

        internal static bool TryGetAssociateDataSource(this DType type, out IExternalDataSource ads)
        {
            if (type.AssociatedDataSources?.FirstOrDefault() is IExternalDataSource dsInfo)
            {
                ads = dsInfo;
                return true;
            }

            // $$$ Throw here after all the source has been updated with associated data sources.
            ads = default;
            return false;
        }

        internal static CdsTableDefinition CdsTableDefinitionOrDefault(this DType type)
        {
            if (type.AssociatedDataSources.FirstOrDefault() is DataverseDataSourceInfo dsInfo)
            {
                return dsInfo.CdsTableDefinition;
            }

            return default;
        }

        internal static CdsColumnDefinition CdsColumnDefinition(this DType type, string name)
        {
            return type.CdsTableDefinition().CdsColumnDefinition(name);
        }

        internal static CdsColumnDefinition CdsColumnDefinitionOrDefault(this DType type, string name)
        {
            return type.CdsTableDefinition().CdsColumnDefinitionOrDefault(name);
        }

        internal static CdsColumnDefinition CdsColumnDefinitionOrDefault(this CdsTableDefinition table, string name)
        {
            return table.Columns.Cast<CdsColumnDefinition>().FirstOrDefault(
                col => col.Name == name ||
                (col.IsNavigation && col.TypeDefinition is CdsNavigationTypeDefinition navType &&
                 navType.ReferencingFieldName == name));
        }

        internal static CdsColumnDefinition CdsColumnDefinition(this CdsTableDefinition table, string name)
        {
            var column = table.CdsColumnDefinitionOrDefault(name);
            if (column == null)
            {
                throw new Exception($"{name} not found on {table.Name}");
            }

            return column;
        }
    }
}
