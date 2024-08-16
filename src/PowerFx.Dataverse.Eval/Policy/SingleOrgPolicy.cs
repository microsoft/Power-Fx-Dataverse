// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Include all elements in the entire org, but lazily load them.
    /// Tables are added with their DisplayName and get localized.
    /// This is condusive to a single org.
    /// </summary>
    public class SingleOrgPolicy : Policy
    {
        internal const string SymTableName = "DataverseLazyGlobals";

        private ReadOnlySymbolTable _symbols;

        // Mapping of Table logical names to values.
        // Since this is a single-org case, the "Variable Name" is just the display name.
        private protected readonly Dictionary<string, DataverseTableValue> _tablesLogical2Value = new Dictionary<string, DataverseTableValue>();

        public DisplayNameProvider AllTables { get; }

        public SingleOrgPolicy(DisplayNameProvider displayNameLookup)
        {
            AllTables = DisplayNameUtility.MakeUnique(displayNameLookup.LogicalToDisplayPairs.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value.Value));           
        }

        // HElper to create a DV connection over the given service client.
        public static DataverseConnection New(IOrganizationService client, bool numberIsFloat = false)
        {
            var displayNameMap = client.GetTableDisplayNames();

            var services = new DataverseService(client);
            var rawProvider = new XrmMetadataProvider(client);
            var metadataProvider = new CdsEntityMetadataProvider(rawProvider, displayNameMap) { NumberIsFloat = numberIsFloat };

            var policy = new SingleOrgPolicy(displayNameMap);

            var dvConnection = new DataverseConnection(policy, services, metadataProvider);
            return dvConnection;
        }

        public override bool TryGetVariableName(string logicalName, out string variableName)
        {
            if (AllTables.TryGetDisplayName(new DName(logicalName), out var variable))
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

        private ReadOnlySymbolTable _allEntitieSymbols;        

        internal override ReadOnlySymbolTable CreateSymbols(CdsEntityMetadataProvider metadataCache)
        {
            _allEntitieSymbols = ReadOnlySymbolTable.NewFromDeferred(AllTables, LazyAddTable, TableType.Empty(), SymTableName);

            var optionSetSymbols = new DVSymbolTable(metadataCache);

            _symbols = ReadOnlySymbolTable.Compose(_allEntitieSymbols, optionSetSymbols);
            return _symbols;
        }

        internal virtual DataverseTableValue NewDataverseTableValue(RecordType recordType, DataverseConnection dataverseConnection, EntityMetadata entityMetadata) => new DataverseTableValue(recordType, dataverseConnection, entityMetadata);

        // This can be called on multiple threads, and multiple times.
        // Called lazily when we encounter a new name and add the table.
        // Only called on explicit access by resolved; not called when we pickup via relationships.
        private DeferredSymbolPlaceholder LazyAddTable(string logicalName, string displayName)
        {
            lock (_tablesLogical2Value)
            {
                if (_tablesLogical2Value.TryGetValue(logicalName, out var table))
                {
                    // Already present.
                    return new DeferredSymbolPlaceholder(table.Type);
                }
            }

            // Can't be under lock - this may invoke metadata callback interfaces and network requests.
            // Safe to call this multiple times since we don't update any state.
            EntityMetadata entityMetadata = _parent.GetMetadataOrThrow(logicalName);
            RecordType recordType = _parent._metadataCache.GetRecordType(logicalName);

            DataverseTableValue tableValue = NewDataverseTableValue(recordType, _parent, entityMetadata);

            // This is critical for dependency finder.
            Contract.Assert(logicalName == tableValue.Type.TableSymbolName);

            lock (_tablesLogical2Value)
            {
                // Race - somebody else added.
                if (_tablesLogical2Value.TryGetValue(logicalName, out var table))
                {
                    // Already present.
                    return new DeferredSymbolPlaceholder(table.Type);
                }

                _tablesLogical2Value.Add(logicalName, tableValue);

                return new DeferredSymbolPlaceholder(tableValue.Type, (slot) =>
                {
                    // Invoked when we get the slot.
                    _parent.SetInternal(slot, tableValue);
                });
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
                if (type is AggregateType t)
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
