//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Include all elements in the entire org, but lazily load them.
    /// Tables are added with their DisplayName and get localized. 
    /// This is condusive to a single org. 
    /// </summary>
    public class SingleOrgPolicy : Policy
    {
        private readonly DisplayNameProvider _displayNameLookup;

        private ReadOnlySymbolTable _symbols;

        // Mapping of Table logical names to values. 
        // Since this is a single-org case, the "Variable Name" is just the display name.
        private protected readonly Dictionary<string, DataverseTableValue> _tablesLogical2Value = new Dictionary<string, DataverseTableValue>();

        public DisplayNameProvider AllTables => _displayNameLookup;

        public SingleOrgPolicy(DisplayNameProvider displayNameLookup)
        {
            _displayNameLookup = displayNameLookup;
        }

        // HElper to create a DV connection over the given service client. 
        public static DataverseConnection New(IOrganizationService client)
        {
            var displayNameMap = client.GetTableDisplayNames();

            var services = new DataverseService(client);
            var rawProvider = new XrmMetadataProvider(client);
            var metadataProvider = new CdsEntityMetadataProvider(rawProvider, displayNameMap);

            var policy = new SingleOrgPolicy(displayNameMap);

            var dvConnection = new DataverseConnection(policy, services, metadataProvider);
            return dvConnection;
        }

        public override bool TryGetVariableName(string logicalName, out string variableName)
        {
            if (_displayNameLookup.TryGetDisplayName(new DName(logicalName), out var variable))
            {
                // For whole-org, we don't have fixed-name variables. (aren't converted to invariant)
                // We use display namaes, which are converted to their logical name.  
                variableName = logicalName;
                return true;
            }

            variableName = null;
            return false;
        }

        internal override TableValue AddTable(string variableName, string tableLogicalName)
        {
            throw new NotSupportedException($"Only explicit policy supports AddTable");
        }

        public override void RefreshCache()
        {
            foreach (var dataverseTableValue in _tablesLogical2Value.Values)
            {
                dataverseTableValue.RefreshCache();
            }
        }

        ReadOnlySymbolTable _allEntitieSymbols;

        internal override ReadOnlySymbolTable CreateSymbols(CdsEntityMetadataProvider metadataCache)
        {
            _allEntitieSymbols = ReadOnlySymbolTable.NewFromDeferred(_displayNameLookup, LazyAddTable, "DataverseLazyGlobals");

            var optionSetSymbols = new DVSymbolTable(metadataCache);

            _symbols = ReadOnlySymbolTable.Compose(_allEntitieSymbols, optionSetSymbols);
            return _symbols;
        }

        // This can be called on multiple threads, and multiple times. 
        // Called lazily when we encounter a new name and add the table.
        // Only called on explicit access by resolved; not called when we pickup via relationships. 
        private FormulaType LazyAddTable(string logicalName, string displayName)
        {
            lock (_tablesLogical2Value)
            {
                if (_tablesLogical2Value.TryGetValue(logicalName, out var table))
                {
                    // Already present.
                    return table.Type;
                }
            }

            // Can't be under lock - this may invoke metadata callback interfaces and network requests.
            // Safe to call this multiple times since we don't update any state. 
            EntityMetadata entityMetadata = _parent.GetMetadataOrThrow(logicalName);
            RecordType recordType = _parent._metadataCache.GetRecordType(logicalName);

            DataverseTableValue tableValue = new DataverseTableValue(recordType, _parent, entityMetadata);

            // This is critical for dependency finder. 
            Contract.Assert(logicalName == tableValue.Type.TableSymbolName);

            lock (_tablesLogical2Value)
            {
                // Race - somebody else added.
                if (_tablesLogical2Value.TryGetValue(logicalName, out var table))
                {
                    // Already present.
                    return table.Type;
                }

                _tablesLogical2Value.Add(logicalName, tableValue);

                // The slot doesn't exist yet, so we can't populate the symbol values. 
                // Add to derred list and DataverseConnection will handle. 
                _pendingTables.Add(Tuple.Create(logicalName, tableValue));

                return tableValue.Type;
            } 
        }

        // Protected under lock. 
        List<Tuple<string, DataverseTableValue>> _pendingTables = new List<Tuple<string, DataverseTableValue>>();

        internal override void AddPendingTables()
        {
            // Copy to local for thread safety
            Tuple<string, DataverseTableValue>[] list;

            lock (_tablesLogical2Value)
            {
                list = _pendingTables.ToArray();
                _pendingTables.Clear();
            }

            var slots = new Dictionary<ISymbolSlot, DataverseTableValue>();

            foreach (var kv in list)
            {
                // Can't call TryLookup under a lock, 
                // so create the list outside the lock. 
                if (_allEntitieSymbols.TryLookupSlot(kv.Item1, out var slot))
                {
                    // _parent.Set(slot, kv.Item2);
                    slots.Add(slot, kv.Item2);
                }
            }

            // Now process the items again under the lock. 
            lock (_tablesLogical2Value)
            {
                foreach (var kv in slots)
                {
                    _parent.SetInternal(kv.Key, kv.Value);
                }
            }
        }


        // Get logical names of tables that this specific expression depends on. 
        // This will be a subset of all known tables. 
        public HashSet<string> GetDependencies(CheckResult check)
        {
            check.ApplyBinding();
            check.ThrowOnErrors();

            var tf = new TableFinder(check);
            check.Parse.Root.Accept(tf);

            return tf._tableNames;
        }


        // Given an expression with Table References, get a list of all the tables that are actually used. 
        internal class TableFinder : IdentityTexlVisitor
        {
            private readonly CheckResult _check;
            public readonly HashSet<string> _tableNames = new HashSet<string>();

            public TableFinder(CheckResult check)
            {
                _check = check;
            }

            public override void PostVisit(DottedNameNode node)
            {
                var type = _check.GetNodeType(node);
                if (type is RecordType t)
                {
                    var name = t.TableSymbolName;
                    if (name != null)
                    {
                        _tableNames.Add(name);
                    }
                }

                base.PostVisit(node);
            }

            public override void Visit(FirstNameNode node)
            {
                var type = _check.GetNodeType(node);
                if (type is TableType t)
                {
                    var name = t.TableSymbolName;
                    if (name != null)
                    {
                        _tableNames.Add(name);
                    }
                }
                base.Visit(node);
            }
        }
    }
}
