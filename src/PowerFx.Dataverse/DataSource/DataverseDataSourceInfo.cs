//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.AppMagic;
using Microsoft.AppMagic.Authoring;
using Microsoft.AppMagic.Authoring.Importers.DataDescription;
using Microsoft.AppMagic.Common;
using Microsoft.PowerFx.Core.App;
using Microsoft.PowerFx.Core.Entities.Delegation;
using Microsoft.PowerFx.Core.Entities.QueryOptions;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.PowerFx.Core.App.Controls;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Core.Errors;
using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.Functions.Delegation.DelegationMetadata;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.UtilityDataStructures;
using Microsoft.PowerFx.Core.Utils;
using System.Text.Json;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Repository for all information about an entity that will be needed to bind or generate SQL
    /// </summary>
    internal class DataverseDataSourceInfo : IExternalCdsDataSource, IDataEntityMetadata
    {
        public CdsTableDefinition CdsTableDefinition { get; }

        private BidirectionalDictionary<string, string> _columnDisplayNameMapping;
        private CdsEntityMetadataProvider _provider;
        private DelegationMetadata _delegationMetadata;

        public DataverseDataSourceInfo(CdsTableDefinition tableDefinition, CdsEntityMetadataProvider provider)
        {
            CdsTableDefinition = tableDefinition;
            _columnDisplayNameMapping = tableDefinition.RegisterDisplayNameMapping();
            _provider = provider;
            Document = provider.Document;

            // TODO: modeled from CdsDataSourceInfo.SetClientSemantics - is it worth breaking out?

            // only go 1 level for now
            Schema = tableDefinition.GetRuntimeType().ComputeDType(1).DType;

            // Update parent of all related entities to this entity.
            // During parsing of navigation metadata, parser creates EntityInfo when creating DType.DataEntity.
            // EntityInfo always need to have valid parent DataSourceInfo. Document uses this information to get metadata information for this entity later in delayed fashion.
            // As there is no CdsDataSourceInfo created during parsing, we need to update parent information later when there is an instance of CdsDataSourceInfo available.
            // That's what we are doing below.
            foreach (var info in Schema.GetExpands())
                info.UpdateEntityInfo(this, string.Empty);

            Schema = DType.AttachDataSourceInfo(Schema, this);

            QueryOptions = new TabularDataQueryOptions(this);

            // If delegable then set delegation metadata and delegatable attribute.
            var delegationMetadataDef = JsonSerializer.Serialize(tableDefinition.ServiceCapabilities);
            Contracts.AssertValue(delegationMetadataDef);

            _delegationMetadata = new DelegationMetadata(Schema, delegationMetadataDef);

            if (IsSelectable)
            {
                // Ensure that primary keys are included in datasource selects.
                QueryOptions.AddSelectMultiple(GetKeyColumns());
            }
        }

        public string PrimaryNameField => CdsTableDefinition.PrimaryNameColumn;

        public string DatasetName => CdsTableDefinition.DatasetName;

        public IExternalDocument Document { get; }

        public IExternalTableDefinition TableDefinition => CdsTableDefinition;

        public TabularDataQueryOptions QueryOptions { get; }

        public DType Schema { get; }

        public DType Type => Schema;

        public string Name => CdsTableDefinition.Name;

        public bool IsSelectable => true;

        public bool IsDelegatable => true;

        public bool RequiresAsync => throw new NotImplementedException();

        public IExternalDataEntityMetadataProvider DataEntityMetadataProvider => _provider;

        public bool IsPageable => throw new NotImplementedException();

        public DataSourceKind Kind => DataSourceKind.CdsNative;

        public IExternalTableMetadata TableMetadata => CdsTableDefinition.ToMetadata();

        public IDelegationMetadata DelegationMetadata => _delegationMetadata;

        public string ScopeId => throw new NotImplementedException();

        public bool IsComponentScoped => throw new NotImplementedException();

        public DName EntityName => new DName(CdsTableDefinition.Name);

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
            return IsSelectable
                && CdsTableDefinition.CanIncludeSelect(selectColumnName); throw new NotImplementedException();
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

        #region IDataEntityMetadata implementation
        string IDataEntityMetadata.EntityName => throw new NotImplementedException();

        bool IDataEntityMetadata.IsConvertingDisplayNameMapping { get => false; set => throw new NotImplementedException(); }

        IExternalTableDefinition IDataEntityMetadata.EntityDefinition => TableDefinition;

        bool IDataEntityMetadata.IsValid => throw new NotImplementedException();

        string IDataEntityMetadata.OriginalDataDescriptionJson => throw new NotImplementedException();

        string IDataEntityMetadata.InternalRepresentationJson => throw new NotImplementedException();
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
        #endregion
    }
}
