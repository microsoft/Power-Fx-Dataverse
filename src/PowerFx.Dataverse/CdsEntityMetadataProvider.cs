//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.AppMagic.Authoring;
using Microsoft.AppMagic.Authoring.CdsService;
using Microsoft.AppMagic.Authoring.Importers.DataDescription;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.App;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Core.Entities.Delegation;
using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.Parser.Importers.DataDescription;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Metadata cache for the compiler. Allows sharing metadata cross compiler instances. 
    /// </summary>
    public class CdsEntityMetadataProvider : IDisplayNameProvider, IExternalDataEntityMetadataProvider, IExternalEntityScope
    {
        private readonly IXrmMetadataProvider _innerProvider;
        private readonly DataverseDocument _document;

        /// <summary>
        /// All option sets, indexed by both display and logical name
        /// </summary>
        private readonly ConcurrentDictionary<string, DataverseOptionSet> _optionSets = new ConcurrentDictionary<string, DataverseOptionSet>(StringComparer.Ordinal);


        /// <summary>
        /// Cache of XRM entity metadata already retrieved, indexed by logical name
        /// </summary>
        private readonly ConcurrentDictionary<string, EntityMetadata> _xrmCache = new ConcurrentDictionary<string, EntityMetadata>(StringComparer.Ordinal);

        /// <summary>
        /// Cache of processed CDS table definitions, indexed by logical name
        /// TODO: rationalize with TabularDataManager
        /// </summary>
        private readonly ConcurrentDictionary<string, DataverseDataSourceInfo> _cdsCache = new ConcurrentDictionary<string, DataverseDataSourceInfo>(StringComparer.Ordinal);

        private readonly List<OptionSetMetadata> _globalOptionSets = new List<OptionSetMetadata>();

        internal IExternalDocument Document => _document;

        // Optimized lookup for IDisplayNameProvider that lets us avoid metadata lookups. 
        // Map logical name to display name
        private readonly Func<string,string> _displayNameLookup;

        public CdsEntityMetadataProvider(IXrmMetadataProvider provider, List<OptionSetMetadata> globalOpsets, IReadOnlyDictionary<string, string> displayNameLookup = null)
        {
            _innerProvider = provider;
            _globalOptionSets = globalOpsets;
            if (displayNameLookup != null)
            {
                _displayNameLookup = (logicalName) => displayNameLookup.TryGetValue(logicalName, out var displayName) ? displayName : null;
            }
            _document = new DataverseDocument(this);
        }

        public CdsEntityMetadataProvider(IXrmMetadataProvider provider, IReadOnlyDictionary<string, string> displayNameLookup = null)
        {
            // Flip Metadata parser into a mode where Hyperlink parses as String, Money parses as Number. 
            // https://msazure.visualstudio.com/OneAgile/_git/PowerApps-Client/pullrequest/7953377
            Microsoft.AppMagic.Authoring.Importers.ServiceConfig.WadlExtensions.PFxV1Semantics = true;

            _innerProvider = provider;
            if (displayNameLookup != null)
            {
                _displayNameLookup = (logicalName) => displayNameLookup.TryGetValue(logicalName, out var displayName) ? displayName : null;
            }
            _document = new DataverseDocument(this);
        }

        public CdsEntityMetadataProvider(IXrmMetadataProvider provider, DisplayNameProvider displayNameLookup)
        {
            _innerProvider = provider;
            _displayNameLookup = (logicalName) => displayNameLookup.TryGetDisplayName(new DName(logicalName), out var displayName) ? displayName.Value : null;
            _document = new DataverseDocument(this);
        }

        private CdsEntityMetadataProvider(IXrmMetadataProvider provider, CdsEntityMetadataProvider original, Func<string, string> displayNameLookup = null)
        {
            this._innerProvider = provider;
            this._document = new DataverseDocument(this);

            // Share all caches
            this._optionSets = original._optionSets;
            this._xrmCache = original._xrmCache;
            this._cdsCache = original._cdsCache;
            this._displayNameLookup = displayNameLookup ?? original._displayNameLookup;            
        }

        /// <summary>
        /// Create a metadata provider that shares the cache, but is accessed via a new provider. 
        /// This is important when the IXrmMetadataProvider are based on short lived IOranizationService objects,
        /// but we want to share the cached results longer. 
        /// </summary>
        /// <param name="provider"></param>
        /// <returns></returns>
        public CdsEntityMetadataProvider Clone(IXrmMetadataProvider provider, Func<string, string> displayNameLookup = null)
        {
            return new CdsEntityMetadataProvider(provider, this, displayNameLookup);
        }

        // Called by operations that just want to get the metadata. 
        internal bool TryGetDataSource(string logicalName, out DataverseDataSourceInfo dataSource)
        {
            return TryGetDataSource(logicalName, null, out dataSource);
        }

        internal bool TryGetDataSource(string logicalName, string variableName, out DataverseDataSourceInfo dataSource)
        {
            if (_cdsCache.TryGetValue(logicalName, out dataSource))
            {
                return true;
            }
            if (TryGetXrmEntityMetadata(logicalName, out var xrmEntity))
            {
                dataSource = FromXrm(xrmEntity, variableName);
                return true;
            }
            else
            {
                dataSource = default;
                return false;
            }
        }

        internal bool TryGetEntityMetadata(string logicalName, out CdsTableDefinition entity)
        {
            if (TryGetDataSource(logicalName, out var dataSource))
            {
                entity = dataSource.TableDefinition as CdsTableDefinition;
                return true;
            }
            else
            {
                entity = default;
                return false;
            }
        }

        internal string GetTableSchemaName(string logicalName)
        {
            if (TryGetEntityMetadata(logicalName, out var entity))
            {
                return entity.SchemaName;
            }
            throw new Exception($"Unrecognized table {logicalName}");
        }

        internal string GetColumnSchemaName(string entityLogicalName, string attributeLogicalName)
        {
            if (TryGetEntityMetadata(entityLogicalName, out var entity))
            {
                return entity.CdsColumnDefinition(attributeLogicalName).SchemaName;
            }
            throw new Exception($"Unrecognized table {entityLogicalName}");
        }

        public bool TryGetXrmEntityMetadata(string logicalName, out EntityMetadata xrmEntity)
        {
            if (_xrmCache.TryGetValue(logicalName, out xrmEntity))
            {
                return true;
            }
            if (_innerProvider != null && _innerProvider.TryGetEntityMetadata(logicalName, out xrmEntity))
            {
                _xrmCache[xrmEntity.LogicalName] = xrmEntity;
                return true;
            }
            return false;
        }

        internal static string GetOptionSetLogicalName(DataverseOptionSet optionSet)
        {
            return (optionSet.IsGlobal ? "global" : optionSet.RelatedEntityName) + "_" + optionSet.InvariantName;
        }

        internal static string GetOptionSetDisplayName(DataverseOptionSet optionSet, string displayCollectionName)
        {
            var uniqueName = optionSet.Name;
            if (!optionSet.IsGlobal)
            {
                uniqueName += " " + TexlLexer.PunctuatorParenOpen + displayCollectionName + TexlLexer.PunctuatorParenClose;
            }

            // add the new option set with a unique name
            optionSet.DisplayName = uniqueName;

            return uniqueName;
        }

        internal void RegisterOptionSet(string name, DataverseOptionSet optionSet)
        {
            // register the option set.  Global option sets may be added multiple times
            Contracts.Assert(!_optionSets.ContainsKey(name) || optionSet.IsGlobal);
            _optionSets[name] = optionSet;
        }

        /// <summary>
        /// Convert a dataverse entity metadata into a Power Fx type. 
        /// </summary>
        /// <param name="logicalName">Logical name of the entity. 
        /// Metadata will be resolved via the <see cref="IXrmMetadataProvider"/> provided to ctor. </param>
        /// <returns></returns>
        public RecordType GetRecordType(string logicalName, string variableName = null)
        {
            if (logicalName == null)
            {
                throw new ArgumentNullException(nameof(logicalName));
            }
            if (TryGetDataSource(logicalName, variableName, out DataverseDataSourceInfo dataSource))
            {
                var dtype = dataSource.Schema.ToRecord();

                return (RecordType)FormulaType.Build(dtype);
            }

            throw new InvalidOperationException($"Entity {logicalName} not present");
        }

        // This constructs a DataverseDataSourceInfo around the entity. 
        // If the entity has option sets or relationships, this may need to call back on the _document object to resolve types. 
        internal DataverseDataSourceInfo FromXrm(EntityMetadata entity, string variableName = null)
        {
            if (_xrmCache.ContainsKey(entity.LogicalName) && _cdsCache.TryGetValue(entity.LogicalName, out var cachedDs))
            {
                return cachedDs;
            }

            // add the XRM entity to the cache
            _xrmCache[entity.LogicalName] = entity;

            var options = new CdsTableOptions(
                    isReadOnly: false,
                    displayFormat: null, // Display format information for child attributes doesn't seem necessary for SQL scenarios
                    nameProvider: this);

            var dataSetName = "dataSet?";

            // pre-process all option sets
            var optionSets = new Dictionary<string, IExternalOptionSet>();
            foreach (var attribute in entity.Attributes)
            {
                string columnName = string.Empty;
                bool parsed = false;
                IExternalOptionSet optionSet = null;
                switch (attribute.AttributeType.Value)
                {
                    case AttributeTypeCode.Picklist:
                    case AttributeTypeCode.State:
                    case AttributeTypeCode.Status:
                        parsed = CdsOptionSetRegisterer.TryRegisterParsedOptionSet(_document, (EnumAttributeMetadata)attribute, entity.LogicalName, dataSetName, out columnName, out optionSet);
                        break;
                    case AttributeTypeCode.Boolean:
                        parsed = CdsOptionSetRegisterer.TryRegisterParsedBooleanOptionSet(_document, (BooleanAttributeMetadata)attribute, entity.LogicalName, dataSetName, out columnName, out optionSet);
                        break;
                    default:
                        break;
                }

                if (parsed) 
                {
                    var dataverseOptionSet = optionSet as DataverseOptionSet;
                    Contracts.Assert(dataverseOptionSet != null);

                    var entityDisplayName = entity.DisplayCollectionName?.UserLocalizedLabel?.Label ?? entity.LogicalName;
                    var uniqueName = GetOptionSetDisplayName(dataverseOptionSet, entityDisplayName);
                    if (dataverseOptionSet.IsGlobal && _optionSets.TryGetValue(uniqueName, out var globalOptionSet))
                    {
                        // if the global option set is already registered, re-use the original, since binding assumes object equality
                        dataverseOptionSet = globalOptionSet;
                    }
                    else
                    {
                        if (!dataverseOptionSet.IsGlobal)
                        {
                            // tag non-global option sets with the entity display name
                            dataverseOptionSet.EntityDisplayCollectionName = entityDisplayName;
                        }

                        // register the option set with the document for global access using the display name
                        RegisterOptionSet(uniqueName, dataverseOptionSet);

                        // also register them with an invariant name
                        var logicalName = GetOptionSetLogicalName(dataverseOptionSet);
                        RegisterOptionSet(logicalName, dataverseOptionSet);
                    }
                    optionSets[columnName] = dataverseOptionSet;
                }
            }

            foreach (var opset in _globalOptionSets)
            {
                string columnName = string.Empty;
                bool parsed = false;
                IExternalOptionSet optionSet = null;
                var attribute = new PicklistAttributeMetadata(string.Empty)
                {
                    OptionSet = opset,
                    LogicalName = opset.Name,
                    MetadataId = Guid.Empty,
                };
                parsed = CdsOptionSetRegisterer.TryRegisterParsedOptionSet(_document, (EnumAttributeMetadata)attribute, entity.LogicalName, dataSetName, out columnName, out optionSet);

                if (parsed)
                {
                    var dataverseOptionSet = optionSet as DataverseOptionSet;
                    Contracts.Assert(dataverseOptionSet != null);

                    var entityDisplayName = entity.DisplayCollectionName?.UserLocalizedLabel?.Label ?? entity.LogicalName;
                    var uniqueName = GetOptionSetDisplayName(dataverseOptionSet, entityDisplayName);
                    if (dataverseOptionSet.IsGlobal && _optionSets.TryGetValue(uniqueName, out var globalOptionSet))
                    {
                        // if the global option set is already registered, re-use the original, since binding assumes object equality
                        dataverseOptionSet = globalOptionSet;
                    }
                    else
                    {
                        // register the option set with the document for global access using the display name
                        RegisterOptionSet(uniqueName, dataverseOptionSet);

                        // also register them with an invariant name
                        var logicalName = GetOptionSetLogicalName(dataverseOptionSet);
                        RegisterOptionSet(logicalName, dataverseOptionSet);
                    }
                    optionSets[columnName] = dataverseOptionSet;
                }
            }

             var dataverseParserErrors = new List<string>();

            var externalEntity = DataverseEntityDefinitionParser.ParseTable(
                dataSetName,
                "dataSource?",
                ToCdsEntityMetadata(entity),
                options,
                optionSets,
                dataverseParserErrors);

            // TODO: Dataverse should provide a method for non-fatal logs
#if DEBUG
            if (dataverseParserErrors.Any())
            {
                Console.Out.WriteLine("Dataverse Parse Errors:");
                Console.Out.WriteLine(JsonSerializer.Serialize(dataverseParserErrors, new JsonSerializerOptions() { WriteIndented = true }));
            }
#endif

            var dataSource = new DataverseDataSourceInfo(externalEntity, this, variableName);

            // add the external entity to the cache
            _cdsCache[dataSource.Name] = dataSource;

            return dataSource;
        }

        private static CdsEntityMetadata ToCdsEntityMetadata(EntityMetadata entity)
        {
            return new CdsEntityMetadata
            {
                LogicalName = entity.LogicalName,
                EntitySetName = entity.EntitySetName,
                DisplayCollectionName = entity.DisplayCollectionName,
                SchemaName = entity.SchemaName,
                PrimaryIdAttribute = entity.PrimaryIdAttribute,
                PrimaryNameAttribute = entity.PrimaryNameAttribute,

                IsActivity = entity.IsActivity,
                IsCustomEntity = entity.IsCustomEntity,
                IsCustomizable = entity.IsCustomizable,
                IsIntersect = entity.IsIntersect,
                IsLogicalEntity = entity.IsLogicalEntity,
                IsManaged = entity.IsManaged,
                IsPrivate = entity.IsPrivate,
                HasNotes = entity.HasNotes,
                ObjectTypeCode = entity.ObjectTypeCode,
                OwnershipType = entity.OwnershipType,

                Attributes = entity.Attributes,
                OneToManyRelationships = entity.OneToManyRelationships,
                ManyToOneRelationships = entity.ManyToOneRelationships,
                ManyToManyRelationships = entity.ManyToManyRelationships,

                DataProviderId = entity.DataProviderId
            };
        }

        internal bool TryGetOptionSet(DName name, out DataverseOptionSet optionSet)
        {
            return _optionSets.TryGetValue(name.Value, out optionSet);
        }
        internal IEnumerable<DataverseOptionSet> OptionSets => _optionSets.Values.Distinct();

        #region IDisplayNameProvider implementation
        /// <summary>
        /// Get the DisplayCollectionName for an entity, based
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        string IDisplayNameProvider.this[string key]
        {
            get
            {
                if (_cdsCache.TryGetValue(key, out var entity))
                {
                    return entity.CdsTableDefinition.DisplayName;
                }
                if (_xrmCache.TryGetValue(key, out var xrmEntity))
                {
                    return GetDisplayName(xrmEntity);
                }
                else
                {
                    // Try to check against lookup map. This is fast. 
                    if (_displayNameLookup != null)
                    {
                        var displayName = _displayNameLookup(key);
                        return displayName;
                    }

                    // if not found in either cache, get the raw entity from CDS
                    if (TryGetXrmEntityMetadata(key, out xrmEntity))
                    {
                        return GetDisplayName(xrmEntity);
                    }
                }

                return null;
            }
        }

        bool IDisplayNameProvider.ContainsKey(string key)
        {
            if (_cdsCache.ContainsKey(key) || _xrmCache.ContainsKey(key))
            {
                return true;
            }

            // if not found in either cache, get the raw entity from CDS
            if (_displayNameLookup != null)
            {
                var displayName = _displayNameLookup(key);
                return displayName != null;
            }

            // Fallback to metadata lookup - this can be slow. 
            if (TryGetXrmEntityMetadata(key, out _))
            {
                return true;
            }

            // if not found in either, return false
            return false;
        }

        private string GetDisplayName(EntityMetadata entity)
        {
            return entity.DisplayCollectionName?.UserLocalizedLabel?.Label ?? entity.LogicalName;
        }
        #endregion

        #region IExternalDataEntityMetadataProvider implementation
        bool IExternalDataEntityMetadataProvider.TryGetEntityMetadata(string expandInfoIdentity, out IDataEntityMetadata entityMetadata)
        {
            DataverseDataSourceInfo dsInfo;
            if (TryGetDataSource(expandInfoIdentity, out dsInfo))
            {
                entityMetadata = dsInfo;
                return true;
            }
            else
            {
                entityMetadata = default;
                return false;
            }
        }
        #endregion

        #region IExternalEntityScope implementation
        bool IExternalEntityScope.TryGetNamedEnum(DName identName, out DType enumType)
        {
            throw new NotImplementedException();
        }

        bool IExternalEntityScope.TryGetCdsDataSourceWithLogicalName(string datasetName, string expandInfoIdentity, out IExternalCdsDataSource dataSource)
        {
            DataverseDataSourceInfo dsInfo;
            if (TryGetDataSource(expandInfoIdentity, out dsInfo))
            {
                dataSource = dsInfo;
                return true;
            }
            else
            {
                dataSource = default;
                return false;
            }
        }

        IExternalTabularDataSource IExternalEntityScope.GetTabularDataSource(string identName)
        {
            throw new NotImplementedException();
        }

        bool IExternalEntityScope.TryGetEntity<T>(DName currentEntityEntityName, out T externalEntity)
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
