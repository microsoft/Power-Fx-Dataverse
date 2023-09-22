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
    /// Only include things that are explicitly added by <see cref="DataverseConnection.AddTable(string, string)"/>.
    /// Tables are explicitly added with their variable name and don't get localized. 
    /// This is condusive to allowing multiple orgs.
    /// </summary>
    public class MultiOrgPolicy : Policy
    {
        private SymbolTable _symbols;

        // Mapping of logical name back to variable name.
        internal readonly Dictionary<string, string> _logical2Variable = new Dictionary<string, string>();

        // Mapping of Table variable names (what's used in expression) to values. 
        private protected readonly Dictionary<string, DataverseTableValue> _tablesDisplay2Value = new Dictionary<string, DataverseTableValue>();

        internal override ReadOnlySymbolTable CreateSymbols(CdsEntityMetadataProvider metadataCache)
        {
            _symbols = new DVSymbolTable(metadataCache);
            return _symbols;
        }

        // Helper to create a DV connection over the given service client. 
        public static DataverseConnection New(IOrganizationService client, bool numberIsFloat = false)
        {
            var displayNameMap = client.GetTableDisplayNames();

            var services = new DataverseService(client);
            var rawProvider = new XrmMetadataProvider(client);
            var metadataProvider = new CdsEntityMetadataProvider(rawProvider, displayNameMap) { NumberIsFloat = numberIsFloat };

            var policy = new MultiOrgPolicy();

            var dvConnection = new DataverseConnection(policy, services, metadataProvider);
            return dvConnection;
        }

        public override bool TryGetVariableName(string logicalName, out string variableName)
        {
            return _logical2Variable.TryGetValue(logicalName, out variableName);
        }

        /// <summary>
        /// Add a table from the connection. Must be present in the metadata. 
        /// </summary>
        /// <param name="variableName"> name to use in the expressions. This is often the table's display name, 
        /// but the host can adjust to disambiguiate (Accounts, Accounts_1).</param>
        /// <param name="tableLogicalName">The table logical name in dataverse.</param>
        /// <returns></returns>
        internal override TableValue AddTable(string variableName, string tableLogicalName)
        {
            if (_logical2Variable.TryGetValue(tableLogicalName, out var existingVariableName))
            {
                throw new InvalidOperationException($"Table with logical name '{tableLogicalName}' was already added as {existingVariableName}.");
            }
            if (_tablesDisplay2Value.TryGetValue(variableName, out var existingTable))
            {
                throw new InvalidOperationException($"Table with variable name '{variableName}' was already added as {existingTable._entityMetadata.LogicalName}.");
            }

            EntityMetadata entityMetadata = _parent.GetMetadataOrThrow(tableLogicalName);

            _logical2Variable.Add(tableLogicalName, variableName);

            RecordType recordType = _parent.GetRecordType(entityMetadata);
            DataverseTableValue tableValue = new DataverseTableValue(recordType, _parent, entityMetadata);

            var slot = _symbols.AddVariable(variableName, tableValue.Type, new SymbolProperties {
                 CanSet = false,
                 CanMutate = true
            });

            _tablesDisplay2Value[variableName] = tableValue;
            _parent.SymbolValues.Set(slot, tableValue);
            return tableValue;
        }

        public override void RefreshCache()
        {
            foreach (var dataverseTableValue in _tablesDisplay2Value.Values)
            {
                dataverseTableValue.RefreshCache();
            }
        }
    }
}
