﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AppMagic.Authoring.Importers.DataDescription;
using Microsoft.PowerFx.Core.App;
using Microsoft.PowerFx.Core.App.Controls;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Core.Entities.Delegation;
using Microsoft.PowerFx.Core.Entities.QueryOptions;
using Microsoft.PowerFx.Core.Errors;
using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.Functions.Delegation.DelegationMetadata;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.UtilityDataStructures;
using Microsoft.PowerFx.Core.Utils;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Repository for all information about an entity that will be needed to bind or generate SQL.
    /// </summary>
    internal class DataverseDataSourceInfo : IExternalCdsDataSource, IDataEntityMetadata
    {
        public CdsTableDefinition CdsTableDefinition { get; }

        private BidirectionalDictionary<string, string> _columnDisplayNameMapping;

        private CdsEntityMetadataProvider _provider;

        private DelegationMetadata _delegationMetadata;

        public DataverseDataSourceInfo(CdsTableDefinition tableDefinition, CdsEntityMetadataProvider provider, string variableName = null)
        {
            CdsTableDefinition = tableDefinition;
            _columnDisplayNameMapping = tableDefinition.RegisterDisplayNameMapping();
            _provider = provider;
            Document = provider.Document;

            // TODO: modeled from CdsDataSourceInfo.SetClientSemantics - is it worth breaking out?

            // Go 2 level for now to also compute multi-select field types (table type).
            Schema = tableDefinition.GetRuntimeType().ComputeDType(2).DType;

            // Update parent of all related entities to this entity.
            // During parsing of navigation metadata, parser creates EntityInfo when creating DType.DataEntity.
            // EntityInfo always need to have valid parent DataSourceInfo. Document uses this information to get metadata information for this entity later in delayed fashion.
            // As there is no CdsDataSourceInfo created during parsing, we need to update parent information later when there is an instance of CdsDataSourceInfo available.
            // That's what we are doing below.
            foreach (var info in Schema.GetExpands())
            {
                info.UpdateEntityInfo(this, string.Empty);
            }

            Schema = DType.AttachDataSourceInfo(Schema, this);
            QueryOptions = new TabularDataQueryOptions(this);

            // Ensure "distinct" capability is added, as well as "joininner", "joinleft", "joinfull" but not "joinright" which isn't supported by DV
            ServiceCapabilities updatedServiceCapabilities = EnsureCapability(
                tableDefinition.ServiceCapabilities, 
                DelegationMetadataOperatorConstants.Distinct,
                DelegationMetadataOperatorConstants.JoinInner, 
                DelegationMetadataOperatorConstants.JoinLeft, 
                DelegationMetadataOperatorConstants.JoinFull);

            // If delegable then set delegation metadata and delegatable attribute.
            var delegationMetadataDef = JsonSerializer.Serialize(updatedServiceCapabilities);

            Contracts.AssertValue(delegationMetadataDef);

            _delegationMetadata = new DelegationMetadata(Schema, delegationMetadataDef);

            if (IsSelectable)
            {
                // Ensure that primary keys are included in datasource selects.
                QueryOptions.AddSelectMultiple(GetKeyColumns());
            }

            // Default values
            this.EntityName = new DName(variableName ?? CdsTableDefinition.Name);
        }

        private static ServiceCapabilities EnsureCapability(ServiceCapabilities serviceCapabilities, params string[] operationCapabilities)
        {
            List<string> filterFunction = serviceCapabilities.FilterFunctions.ToList();
            List<string> filterSupportedFunctions = serviceCapabilities.FilterSupportedFunctions.ToList();

            foreach (string capability in operationCapabilities)
            {
                if (!filterFunction.Contains(capability))
                {
                    filterFunction.Add(capability);
                }

                if (!filterSupportedFunctions.Contains(capability))
                {
                    filterSupportedFunctions.Add(capability);
                }
            }

            return new ServiceCapabilities(
                serviceCapabilities.SortRestriction,
                serviceCapabilities.FilterRestriction,
                serviceCapabilities.SelectionRestriction,
                serviceCapabilities.GroupRestriction,
                filterFunction.ToArray(),
                filterSupportedFunctions.ToArray(),
                serviceCapabilities.PagingCapabilities,
                serviceCapabilities.SupportsRecordPermission,
                serviceCapabilities.ODataVersion,
                serviceCapabilities.SupportsDataverseOffline);
        }

        public string PrimaryNameField => CdsTableDefinition.PrimaryNameColumn;

        public string PrimaryKeyName => CdsTableDefinition.PrimaryKeyColumn;

        public string DatasetName => CdsTableDefinition.DatasetName;

        public IExternalDocument Document { get; }

        public IExternalTableDefinition TableDefinition => CdsTableDefinition;

        public TabularDataQueryOptions QueryOptions { get; }

        public DType Schema { get; }

        public DType Type => Schema;

        public string Name => CdsTableDefinition.Name;

        public bool IsSelectable => true;

        public bool IsDelegatable => _isDelegable;

        internal bool _isDelegable = true;

        public bool RequiresAsync => throw new NotImplementedException();

        public IExternalDataEntityMetadataProvider DataEntityMetadataProvider => _provider;

        public bool IsPageable => throw new NotImplementedException();

        public DataSourceKind Kind => DataSourceKind.CdsNative;

        public IExternalTableMetadata TableMetadata => CdsTableDefinition.ToMetadata();

        public IDelegationMetadata DelegationMetadata => _delegationMetadata;

        public string ScopeId => throw new NotImplementedException();

        public bool IsComponentScoped => throw new NotImplementedException();

        public DName EntityName { get; private set; }

        public string InvariantName => CdsTableDefinition.Name;

        public bool IsControl => throw new NotImplementedException();

        public IExternalEntityScope EntityScope => throw new NotImplementedException();

        public IEnumerable<IDocumentError> Errors => throw new NotImplementedException();

        public bool IsConvertingDisplayNameMapping => false;

        public BidirectionalDictionary<string, string> DisplayNameMapping => _columnDisplayNameMapping;

        public BidirectionalDictionary<string, string> PreviousDisplayNameMapping => null;

        public bool CanIncludeExpand(IExpandInfo expandToAdd)
        {
            return CdsTableDefinition.CanIncludeExpand(expandToAdd);
        }

        public bool CanIncludeExpand(IExpandInfo parentExpandInfo, IExpandInfo expandToAdd)
        {
            throw new NotImplementedException();
        }

        public bool CanIncludeSelect(string selectColumnName)
        {
            return IsSelectable && CdsTableDefinition.CanIncludeSelect(selectColumnName);
            throw new NotImplementedException();
        }

        public bool CanIncludeSelect(IExpandInfo expandInfo, string selectColumnName)
        {
            throw new NotImplementedException();
        }

        public IReadOnlyList<string> GetKeyColumns()
        {
            return CdsTableDefinition?.KeyColumns ?? new List<string>();
        }

        public IEnumerable<string> GetKeyColumns(IExpandInfo expandInfo)
        {
            throw new NotImplementedException();
        }

        public bool TryGetRelatedColumn(string selectColumnName, out string additionalColumnName, IExternalTableDefinition expandsTableDefinition = null)
        {
            throw new NotImplementedException();
        }

        public bool TryGetRule(DName propertyName, out IExternalRule rule)
        {
            throw new NotImplementedException();
        }

        public bool IsArgTypeValidForMutation(DType type, out IEnumerable<string> invalidFieldName)
        {
            var isValid = true;
            var invalidNames = new List<string>();

            foreach (var name in type.GetAllNames(DPath.Root))
            {
                var columnDefinition = CdsTableDefinition.CdsColumnDefinitionOrDefault(name.Name.Value);

                if (columnDefinition != null && columnDefinition.IsReadOnly)
                {
                    invalidNames.Add(name.Name.Value);
                    isValid = false;
                }
            }

            invalidFieldName = invalidNames;

            return isValid;
        }

        #region IDataEntityMetadata implementation

        string IDataEntityMetadata.EntityName => this.EntityName;

        bool IDataEntityMetadata.IsConvertingDisplayNameMapping { get => false; set => throw new NotImplementedException(); }

        IExternalTableDefinition IDataEntityMetadata.EntityDefinition => TableDefinition;

        bool IDataEntityMetadata.IsValid => throw new NotImplementedException();

        string IDataEntityMetadata.OriginalDataDescriptionJson => throw new NotImplementedException();

        string IDataEntityMetadata.InternalRepresentationJson => throw new NotImplementedException();

        public bool IsRefreshable => true;

        public bool IsWritable => throw new NotImplementedException();

        public bool IsClearable => false;

        public bool HasCachedCountRows => true;

        void IDataEntityMetadata.LoadClientSemantics(bool isPrimaryTable)
        {
            throw new NotImplementedException();
        }

        void IDataEntityMetadata.SetClientSemantics(IExternalTableDefinition tableDefinition)
        {
            throw new NotImplementedException();
        }

        void IDataEntityMetadata.ActualizeTemplate(string datasetName)
        {
            throw new NotImplementedException();
        }

        void IDataEntityMetadata.ActualizeTemplate(string datasetName, string entityName)
        {
            throw new NotImplementedException();
        }

        string IDataEntityMetadata.ToJsonDefinition()
        {
            throw new NotImplementedException();
        }

        #endregion IDataEntityMetadata implementation
    }
}
