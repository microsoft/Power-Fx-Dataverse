//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Veneer of <see cref="DataverseConnection"/> for <see cref="DataverseRecordValue"/> and <see cref="DataverseTableValue"/>.
    /// These values need a connection back to the org. 
    /// </summary>
    internal interface IConnectionValueContext
    {
        IDataverseServices Services { get; }

        // NEed metadata when looking up fields. 
        EntityMetadata GetMetadataOrThrow(string tableLogicalName);

        // Get the name of a table to use in serialization. 
        public string GetSerializationName(string tableLogicalName);
    }

    /// <summary>
    /// Create around connection to Dataverse. 
    /// Provides both symbols for compilation and runtime values via <see cref="DataverseTableValue"/>.
    /// </summary>
    public sealed class DataverseConnection : IConnectionValueContext
    {
        private readonly CdsEntityMetadataProvider _metadataCache;

        // Mapping of Table variable names (what's used in expression) to values. 
        private readonly Dictionary<string, DataverseTableValue> _tables = new Dictionary<string, DataverseTableValue>();

        // Mapping of logical name back to variable name.
        private readonly Dictionary<string, string> _logical2Variable = new Dictionary<string, string>();

        private readonly IDataverseServices _dvServices;

        /// <summary>
        /// Globals populated by calling <see cref="AddTable(string, string)"/>.
        /// </summary>
        public SymbolTable Symbols { get; private set; }

        /// <summary>
        /// Runtime values for Globals from calling <see cref="AddTable(string, string)"/>.
        /// </summary>
        internal IReadOnlyDictionary<string, DataverseTableValue> KnownTables => _tables;

        IDataverseServices IConnectionValueContext.Services => _dvServices;

        /// <summary>
        /// DataverseConnection constructor.
        /// </summary>
        /// <param name="service"></param>
        public DataverseConnection(IOrganizationService service)
            : this(new DataverseService(service), new XrmMetadataProvider(service))
        {
        }

        internal DataverseConnection(IDataverseServices dvServices, IXrmMetadataProvider xrmMetadataProvider)
            : this(dvServices, new CdsEntityMetadataProvider(xrmMetadataProvider))
        {
        }

        /// <summary>
        /// DataverseConnection constructor.        
        /// </summary>
        /// <param name="service">IOrganizationService</param>
        /// <param name="metadataProvider">Metadata provider that can be cached. 
        /// IMPORTANT: There is NO security management in this code, so the caller is responsible for not calling AddTable later for a table
        /// for which the user authenticated for <paramref name="service"/> parameter doesn't have permissions.</param>
        public DataverseConnection(IOrganizationService service, CdsEntityMetadataProvider metadataProvider)
            : this(new DataverseService(service), metadataProvider)
        {
        }

        public DataverseConnection(IDataverseServices dvServices, CdsEntityMetadataProvider cdsEntityMetadataProvider)
        {
            _dvServices = dvServices;
            _metadataCache = cdsEntityMetadataProvider;
            Symbols = new DVSymbolTable(_metadataCache);
        }

        public SymbolTable GetRowScopeSymbols(string tableLogicalName)
        {
            var recordType = this.GetRecordType(tableLogicalName);

            var s = ReadOnlySymbolTable.NewFromRecord(recordType, this.Symbols);
            var s2 = new SymbolTable() { Parent = s };

            s2.AddVariable("ThisRecord", recordType);
            return s2;
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
            if (_logical2Variable.TryGetValue(tableLogicalName, out var existingVariableName))
            {
                throw new InvalidOperationException($"Table with logical name '{tableLogicalName}' was already added as {existingVariableName}.");
            }
            if (_tables.TryGetValue(variableName, out var existingTable))
            {
                throw new InvalidOperationException(
                    $"Table with variable name '{variableName}' was already added as {existingTable._entityMetadata.LogicalName}.");
            }

            EntityMetadata entityMetadata = GetMetadataOrThrow(tableLogicalName);

            _logical2Variable.Add(tableLogicalName, variableName);

            RecordType recordType = GetRecordType(entityMetadata);
            DataverseTableValue tableValue = new DataverseTableValue(recordType, this, entityMetadata);

            Symbols.AddVariable(variableName, tableValue.Type);

            _tables[variableName] = tableValue;
            return tableValue;
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
            return _logical2Variable.TryGetValue(logicalName, out variableName);
        }

        string IConnectionValueContext.GetSerializationName(string tableLogicalName)
        {
            if (this.TryGetVariableName(tableLogicalName, out var variableName))
            {
                return variableName;
            }
            return tableLogicalName;
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
        public async Task<FormulaValue> RetrieveAsync(string logicalName, Guid id, CancellationToken cancellationToken = default)
        {
            if (!_metadataCache.TryGetXrmEntityMetadata(logicalName, out EntityMetadata metadata))
                throw new InvalidOperationException($"No metadata for {logicalName}");

            if (cancellationToken.IsCancellationRequested)
                throw new TaskCanceledException();

            DataverseResponse<Entity> response = await _dvServices.RetrieveAsync(metadata.LogicalName, id, cancellationToken);

            if (response.HasError)
                return RecordValue.NewError(new ExpressionError() { Kind = ErrorKind.Unknown, Severity = ErrorSeverity.Critical, Message = response.Error });

            return new DataverseRecordValue(response.Response, metadata, GetRecordType(metadata.LogicalName), this);
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
            if (!_metadataCache.TryGetXrmEntityMetadata(logicalName, out EntityMetadata metadata))
                throw new InvalidOperationException($"No metadata for {logicalName}");

            Task<DataverseResponse<Entity>>[] tasks = new Task<DataverseResponse<Entity>>[ids.Length];

            for (int i = 0; i < ids.Length; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new TaskCanceledException();

                tasks[i] = _dvServices.RetrieveAsync(metadata.LogicalName, ids[i], cancellationToken);
            }

            Task.WaitAll(tasks, cancellationToken);

            return tasks.Select<Task<DataverseResponse<Entity>>, FormulaValue>(t =>
            {
                if (t.IsFaulted)
                    return RecordValue.NewError(new ExpressionError() { Kind = ErrorKind.Unknown, Severity = ErrorSeverity.Severe, Message = t.Exception.Message });
                else if (t.Result.HasError)
                    return RecordValue.NewError(new ExpressionError() { Kind = ErrorKind.Unknown, Severity = ErrorSeverity.Critical, Message = t.Result.Error });
                else
                    return new DataverseRecordValue(t.Result.Response, metadata, GetRecordType(metadata.LogicalName), this);
            }).ToArray();
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
            if (!_logical2Variable.TryGetValue(tableLogicalName, out string variableName))
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

        private EntityMetadata GetMetadataOrThrow(string tableLogicalName)
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

        public ReadOnlySymbolValues GetSymbolValues()
        {
            return ReadOnlySymbolValues.New(KnownTables).SetDebugName("KnownTables");
        }

        /// <summary>
        /// Get symbols for executing within a row scope. 
        /// </summary>
        /// <param name="dv"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public ReadOnlySymbolValues GetRowScopeSymbolValues(RecordValue parameters)
        {
            var s1 = GetSymbolValues();
            var s2 = ReadOnlySymbolValues.NewRowScope(parameters, s1, debugName: "RowScope");

            return s2;
        }
    }

    // $$$ Temporary hack since DebugName can't be set. Move into Fx core and then remove this. 
    internal static class ReadOnlySymbolValuesExtensions
    {
        public static ReadOnlySymbolValues SetDebugName(this ReadOnlySymbolValues table, string name)
        {
            var prop = table.GetType().GetProperty("DebugName", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            prop.SetValue(table, name);

            return table;
        }
    }
}
