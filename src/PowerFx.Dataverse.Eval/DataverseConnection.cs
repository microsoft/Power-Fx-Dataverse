//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QueryExpression = Microsoft.Xrm.Sdk.Query.QueryExpression;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Veneer of <see cref="DataverseConnection"/> for <see cref="DataverseRecordValue"/> and <see cref="DataverseTableValue"/>.
    /// These values need a connection back to the org. 
    /// </summary>
    internal interface IConnectionValueContext
    {
        ElasticTableAwareDVServices Services { get; }

        int MaxRows { get; }

        // Need metadata when looking up fields. 
        EntityMetadata GetMetadataOrThrow(string tableLogicalName);

        // Get the name of a table to use in serialization. 
        public string GetSerializationName(string tableLogicalName);

        public RecordType GetRecordType(string tableLogicalName);

        public RecordValue Marshal(Entity entity);
    }

    /// <summary>
    /// Create around connection to Dataverse. 
    /// Provides both symbols for compilation and runtime values via <see cref="DataverseTableValue"/>.
    /// </summary>
    public class DataverseConnection : IConnectionValueContext
    {
        internal readonly CdsEntityMetadataProvider _metadataCache;

        private readonly ElasticTableAwareDVServices _dvServices;

        /// <summary>
        /// Globals populated by calling <see cref="AddTable(string, string)"/>.
        /// </summary>
        protected readonly ReadOnlySymbolTable _symbols;

        private readonly SymbolTable _pluginFunctions = new SymbolTable { DebugName = "Plugins"};

        public ReadOnlySymbolTable Symbols => ReadOnlySymbolTable.Compose(_symbols, _pluginFunctions);

        private readonly Policy _policy;

        public CdsEntityMetadataProvider MetadataCache => _metadataCache;

        public int MaxRows => _maxRows;

        private readonly int _maxRows;

        public const int DefaultMaxRows = 1000;

        /// <summary>
        /// Values of global tables that we've added.
        /// </summary>
        public ReadOnlySymbolValues SymbolValues
        {
            get
            {
                _policy.AddPendingTables();
                return _symbolValues;
            }         
        }
                
        internal void SetInternal(ISymbolSlot slot, DataverseTableValue value)
        {
            _symbolValues.Set(slot, value);
        }

        private readonly ReadOnlySymbolValues _symbolValues;

        ElasticTableAwareDVServices IConnectionValueContext.Services => _dvServices;

        public Policy Policy => _policy;

        /// <summary>
        /// DataverseConnection constructor.
        /// </summary>
        /// <param name="service"></param>
        public DataverseConnection(IOrganizationService service, int maxRows = DefaultMaxRows, bool numberIsFloat = false)
            : this(new DataverseService(service), new XrmMetadataProvider(service), maxRows, numberIsFloat)
        {
        }

        internal DataverseConnection(IDataverseServices dvServices, IXrmMetadataProvider xrmMetadataProvider, int maxRows = DefaultMaxRows, bool numberIsFloat = false)
            : this(dvServices, new CdsEntityMetadataProvider(xrmMetadataProvider) { NumberIsFloat = numberIsFloat }, maxRows)
        {
        }

        /// <summary>
        /// DataverseConnection constructor.        
        /// </summary>
        /// <param name="service">IOrganizationService</param>
        /// <param name="metadataProvider">Metadata provider that can be cached. 
        /// IMPORTANT: There is NO security management in this code, so the caller is responsible for not calling AddTable later for a table
        /// for which the user authenticated for <paramref name="service"/> parameter doesn't have permissions.</param>
        public DataverseConnection(IOrganizationService service, CdsEntityMetadataProvider metadataProvider, int maxRows = DefaultMaxRows)
            : this(new DataverseService(service), metadataProvider, maxRows)
        {
        }

        public DataverseConnection(IDataverseServices dvServices, CdsEntityMetadataProvider cdsEntityMetadataProvider, int maxRows = DefaultMaxRows)
            : this(null, dvServices, cdsEntityMetadataProvider, maxRows)
        {            
        }

        public DataverseConnection(Policy policy, IDataverseServices dvServices, CdsEntityMetadataProvider cdsEntityMetadataProvider, int maxRows = DefaultMaxRows)
        {
            Func<string, EntityMetadata> getMetadata =
                (logicalName) =>
                {
                    if (cdsEntityMetadataProvider.TryGetXrmEntityMetadata(logicalName, out var entityMetadata))
                    {
                        return entityMetadata;
                    }
                    return null;
                };

            _dvServices = new ElasticTableAwareDVServices(dvServices, getMetadata) ;
            _metadataCache = cdsEntityMetadataProvider;

            this._policy = policy ?? new MultiOrgPolicy(); 

            this._symbols = _policy.CreateSymbols(this, _metadataCache);
            _symbolValues = this.Symbols.CreateValues();

            _maxRows = maxRows;
        }

        // Identity is important so that we can correlate bindings from Check and result. 
        // Logical Name --> Row Scope symbols for that table 
        Dictionary<string, ReadOnlySymbolTable> _rowScopeSymbols = new Dictionary<string, ReadOnlySymbolTable>();

        // cache for row scope symbols without implicit this record
        Dictionary<string, ReadOnlySymbolTable> _rowScopeSymbolsNoITR = new Dictionary<string, ReadOnlySymbolTable>();

        public ReadOnlySymbolTable GetRowScopeSymbols(string tableLogicalName)
        {
            return GetRowScopeSymbols(tableLogicalName, allowImplicitThisRecord: true);
        }

        public ReadOnlySymbolTable GetRowScopeSymbols(string tableLogicalName, bool allowImplicitThisRecord = false)
        {
            var recordType = this.GetRecordType(tableLogicalName);
            ReadOnlySymbolTable symTable;
            if (allowImplicitThisRecord)
            {
                lock (_rowScopeSymbols)
                {
                    if (!_rowScopeSymbols.TryGetValue(tableLogicalName, out symTable))
                    {
                        symTable = ReadOnlySymbolTable.NewFromRecord(recordType, allowThisRecord: true, allowMutable: true, debugName: $"RowScope:{tableLogicalName}");
                        _rowScopeSymbols[tableLogicalName] = symTable;
                    }
                }
            }
            else
            {
                lock (_rowScopeSymbolsNoITR)
                {
                    if(!_rowScopeSymbolsNoITR.TryGetValue(tableLogicalName, out symTable))
                    {
                        symTable = ReadOnlySymbolTable.NewFromRecordWithoutImplicitThisRecord(recordType, allowMutable: true, debugName: $"RowScopeNoITR:{tableLogicalName}");
                        _rowScopeSymbolsNoITR[tableLogicalName] = symTable;
                    }
                }
            }

            return symTable;
        }

        /// <summary>
        /// Add a table from the connection. Must be present in the metadata. 
        /// </summary>
        /// <param name="variableName"> name to use in the expressions. This is often the table's display name, 
        /// but the host can adjust to disambiguiate (Accounts, Accounts_1).</param>
        /// <param name="tableLogicalName">The table logical name in dataverse.</param>
        /// <returns></returns>
        public TableValue AddTable(string variableName, string tableLogicalName)
        {
            return this._policy.AddTable(variableName, tableLogicalName);
        }

        /// <summary>
        /// Given a table previously added via <see cref="AddTable(string, string)"/>, get the 
        /// variable name from a given logical name. 
        /// If the table wasn't added, return false. 
        /// </summary>
        /// <param name="logicalName"></param>
        /// <param name="variableName"></param>
        /// <returns></returns>
        public bool TryGetVariableName(string logicalName, out string variableName)
        {
            return _policy.TryGetVariableName(logicalName, out variableName);
        }

        string IConnectionValueContext.GetSerializationName(string tableLogicalName)
        {
            if (this.TryGetVariableName(tableLogicalName, out var variableName))
            {
                return variableName;
            }
            return tableLogicalName;
        }

        public async Task AddPluginAsync(string logicalName, CancellationToken cancel = default)
        {
            if (logicalName == null)
            {
                throw new ArgumentNullException(nameof(logicalName));
            }
            IDataverseReader reader = _dvServices.dataverseServices;
            var signature = await reader.GetApiSignatureAsync(logicalName, cancel)
                .ConfigureAwait(false);

            AddPlugin(signature);
        }

        /// <summary>
        /// Import custom API with the given signature. 
        /// API is invokable via the <see cref="IDataverseExecute"/> runtime service.
        /// </summary>
        /// <param name="signature"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public void AddPlugin(CustomApiSignature signature)
        {
            if (signature == null)
            {
                throw new ArgumentNullException(nameof(signature));
            }

            // IDataverseExecute invoker = this._dvServices;
            var x = new CustomApiRestore(this._metadataCache);

            var function = x.ToFunction(signature, this);
            _pluginFunctions.AddFunction(function);
        }

        /// <summary>
        /// Given an Entity, get a RecordValue for use in Fx expressions. 
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public RecordValue Marshal(Entity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (!_metadataCache.TryGetXrmEntityMetadata(entity.LogicalName, out EntityMetadata metadata))
            {
                throw new InvalidOperationException($"No metadata for {entity.LogicalName}");
            }

            RecordType type = this.GetRecordType(entity.LogicalName);
            DataverseRecordValue value = new DataverseRecordValue(entity, metadata, type, this);
            return value;
        }

        /// <summary>
        /// Retrieves an Entity by localname and Id
        /// </summary>
        /// <param name="logicalName">Table logical name</param>
        /// <param name="id">Entity Id</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        /// <returns>DataverseRecordValue or ErrorValue in case of error</returns>
        /// <exception cref="InvalidOperationException">When logicalName has no corresponding Metadata</exception>
        /// <exception cref="TaskCanceledException">When cancelaltion is requested</exception>
        public async Task<FormulaValue> RetrieveAsync(string logicalName, Guid id, IEnumerable<string> columns, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            EntityMetadata metadata = GetMetadataOrThrow(logicalName);
            cancellationToken.ThrowIfCancellationRequested();

            DataverseResponse<Entity> response = await _dvServices.RetrieveAsync(metadata.LogicalName, id, columns, cancellationToken).ConfigureAwait(false);
            RecordType type = GetRecordType(metadata.LogicalName);

            return response.HasError
                ? response.GetErrorValue(type)
                : new DataverseRecordValue(response.Response, metadata, type, this);
        }

        /// <summary>
        /// Retrieves a set of Entity by localname and Ids
        /// </summary>
        /// <param name="logicalName">Table logical name</param>
        /// <param name="ids">Set of Entity Ids</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        /// <returns>An array of DataverseRecordValue or ErrorValue in case of error</returns>
        /// <exception cref="InvalidOperationException">When logicalName has no corresponding Metadata</exception>
        /// <exception cref="TaskCanceledException">When cancelaltion is requested</exception>
        /// <exception cref="ArgumentException">When no Id is provided</exception>
        public async Task<FormulaValue[]> RetrieveMultipleAsync(string logicalName, Guid[] ids, CancellationToken cancellationToken = default)
        {
            if (ids.Length == 0)
                throw new ArgumentException("No Id provided", nameof(ids));

            cancellationToken.ThrowIfCancellationRequested();
            EntityMetadata metadata = GetMetadataOrThrow(logicalName);

            QueryExpression query = new(logicalName);
            query.ColumnSet.AllColumns = true;
            query.Criteria.AddCondition(new ConditionExpression(metadata.PrimaryIdAttribute, ConditionOperator.In, ids));

            if (_maxRows > 0)
            {
                query.PageInfo = new PagingInfo();

                // use one more row to determine if the table has more rows than expected
                query.PageInfo.Count = _maxRows + 1;
                query.PageInfo.PageNumber = 1;
                query.PageInfo.PagingCookie = null;
            }

            DataverseResponse<EntityCollection> response = await _dvServices.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false);
            RecordType type = GetRecordType(metadata.LogicalName);

            if (response.HasError)
            {
                return new FormulaValue[] { response.GetErrorValue(type) };
            }

            List<FormulaValue> records = response.Response.Entities.Select(e => new DataverseRecordValue(e, metadata, type, this)).Cast<FormulaValue>().ToList();

            if (_maxRows > 0 && records.Count > _maxRows)
            {
                records.Remove(records.Last());
                string message = $"Too many entities in table {logicalName}, more than {_maxRows} rows";
                records.Add(FormulaValue.NewError(DataverseHelpers.GetExpressionError(message, messageKey: nameof(RetrieveMultipleAsync)), type));
            }

            return records.ToArray();
        }

        /// <summary>
        /// Given the logical name of a table, get a RecordType for it,. 
        /// </summary>
        /// <param name="tableLogicalName"></param>
        /// <param name="variableName">Symbolic name assigned by the host, like "Accounts_2".</param>
        /// <exception cref="InvalidOperationException">If tableLogicalName was not previously added with <see cref="AddTable(string, string)>"/></exception>
        /// <returns></returns>
        public RecordType GetRecordType(string tableLogicalName)
        {
            if (!_policy.TryGetVariableName(tableLogicalName, out string variableName))
            {
                // Calling AddTable is important so that we have the variableName,
                // which is needed for the DataSourceInfo,
                // which is needed for the DType (via AssociatedDataSources),
                // which is needed for the FormulaType.
                throw new InvalidOperationException($"Missing table '{tableLogicalName}', call AddTable API first");
            }

            var recordType = _metadataCache.GetRecordType(tableLogicalName, variableName);
            return recordType;
        }

        public RecordType GetRecordType(EntityMetadata entityMetadata)
        {
            if (entityMetadata == null)
            {
                throw new ArgumentNullException(nameof(entityMetadata));
            }

            return GetRecordType(entityMetadata.LogicalName);
        }

        internal EntityMetadata GetMetadataOrThrow(string tableLogicalName)
        {
            if (!_metadataCache.TryGetXrmEntityMetadata(tableLogicalName, out var entityMetadata))
            {
                // Error - no table. 
                throw new InvalidOperationException($"No table metadata for {tableLogicalName}");
            }
            return entityMetadata;
        }

        EntityMetadata IConnectionValueContext.GetMetadataOrThrow(string tableLogicalName)
        {
            return this.GetMetadataOrThrow(tableLogicalName);
        }

        /// <summary>
        /// The tables in SymbolValues may cache the data they receive from Dataverse.
        /// These caches can become stale if dataverse is updated outside of this connection object.
        /// This clears any cached data so that subsequent operations will fetch current data.
        /// </summary>
        public void RefreshCache()
        {
            _policy.RefreshCache();

            if (_dvServices.dataverseServices is IDataverseEntityCacheCleaner decc)
            {
                decc.ClearCache();
            }
        }
    }
}
