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

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Create around connection to Dataverse. 
    /// Provides both symbols for compilation and runtime values via <see cref="DataverseTableValue"/>.
    /// </summary>
    public sealed class DataverseConnection
    {
        private readonly CdsEntityMetadataProvider _metadataCache;

        // Mapping of Table display names (what's used in expression) to values. 
        private readonly Dictionary<string, DataverseTableValue> _tables = new Dictionary<string, DataverseTableValue>();

        private readonly IDataverseServices _dvServices;        

        /// <summary>
        /// Globals populated by calling <see cref="AddTable(string, string)"/>.
        /// </summary>
        public SymbolTable Symbols { get; private set; }

        /// <summary>
        /// Runtime values for Globals from calling <see cref="AddTable(string, string)"/>.
        /// </summary>
        internal IReadOnlyDictionary<string, DataverseTableValue> KnownTables => _tables;

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

            // $$$ This isn't capturing display names! 
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
            EntityMetadata entityMetadata = GetMetadataOrThrow(tableLogicalName);
            RecordType recordType = GetRecordType(entityMetadata);
            DataverseTableValue tableValue = new DataverseTableValue(recordType, _dvServices, entityMetadata);

            Symbols.AddVariable(variableName, tableValue.Type);

            _tables[variableName] = tableValue;
            return tableValue;
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

            RecordType type = _metadataCache.GetRecordType(entity.LogicalName);
            DataverseRecordValue value = new DataverseRecordValue(entity, metadata, type, _dvServices);
            return value;
        }

        /// <summary>
        /// Given the logical name of a table, get a RecordType for it,. 
        /// </summary>
        /// <param name="tableLogicalName"></param>
        /// <returns></returns>
        public RecordType GetRecordType(string tableLogicalName)
        {
            var recordType = _metadataCache.GetRecordType(tableLogicalName);
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
            var s2 = ReadOnlySymbolValues.NewRowScope(parameters, s1).SetDebugName("RowScope");

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
