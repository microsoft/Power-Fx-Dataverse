﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Linq;
using System.Threading;
using Microsoft.Dataverse.EntityMock;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class RefreshTests
    {
        internal static readonly EntityMetadataModel Accounts = new EntityMetadataModel
        {
            LogicalName = "Accounts",
            DisplayCollectionName = "Table containing accounts",
            PrimaryIdAttribute = "accountid",
            Attributes = new AttributeMetadataModel[]
            {
                AttributeMetadataModel.NewString("accountname", "Account Name"),
                AttributeMetadataModel.NewGuid("accountid", "Account Id"),
            }
        };

        [Fact]
        public void RefreshTest()
        {
            string logicalName = "Accounts";
            string tableName = "Accounts";

            Entity entity1 = new Entity("Accounts", Guid.NewGuid());
            entity1.Attributes.Add("Account Name", "Account0");

            var xrmMetadataProvider = new MockXrmMetadataProvider(RefreshTests.Accounts);
            EntityLookup entityLookup = new EntityLookup(xrmMetadataProvider);
            entityLookup.Add(CancellationToken.None, entity1);

            DataverseEntityCache2 ds = new DataverseEntityCache2(entityLookup);
            CdsEntityMetadataProvider metadataCache = new CdsEntityMetadataProvider(xrmMetadataProvider);
            DataverseConnection dv = new DataverseConnection(null, ds, metadataCache, maxRows: 999);

            TableValue tableValue = dv.AddTable(variableName: tableName, tableLogicalName: logicalName);
            ReadOnlySymbolTable symbols = ReadOnlySymbolTable.Compose(dv.GetRowScopeSymbols(tableLogicalName: logicalName), dv.Symbols);

            PowerFxConfig config = new PowerFxConfig(Features.PowerFxV1);
            config.SymbolTable.EnableMutationFunctions();

            RecalcEngine engine = new RecalcEngine(config);
            ReadOnlySymbolValues runtimeConfig = dv.SymbolValues;

            _ = Evaluate("Collect(Accounts, {'Account Name': \"Account1\" })", dv, engine);
            Assert.Equal(1, ds.RefreshCount); // Collect calls refresh
            Assert.Equal(0, ds.CacheSize);    // No cache

            _ = Evaluate("First(Accounts)", dv, engine);
            Assert.Equal(1, ds.RefreshCount); // No change
            Assert.Equal(2, ds.CacheSize);    // We have 2 rows: Account0 and Account1

            _ = Evaluate("Refresh(Accounts)", dv, engine);
            Assert.Equal(2, ds.RefreshCount); // Refresh called
            Assert.Equal(0, ds.CacheSize);    // Cache cleared

            _ = Evaluate("Patch(Accounts, First(Filter(Accounts, 'Account Name' = \"Account0\")), {'Account Name': \"Account2\" })", dv, engine);
            Assert.Equal(2, ds.RefreshCount); // No change here
            Assert.Equal(2, ds.CacheSize);    // Cache has been populated again (Account1 and Account2)

            _ = Evaluate("First(Accounts)", dv, engine);
            Assert.Equal(2, ds.RefreshCount); // No change
            Assert.Equal(2, ds.CacheSize);    // We still have 2 rows
        }

        private FormulaValue Evaluate(string formula, DataverseConnection dv, RecalcEngine engine)
        {
            CheckResult check = engine.Check(formula, symbolTable: dv.Symbols, options: new ParserOptions() { AllowsSideEffects = true, NumberIsFloat = PowerFx2SqlEngine.NumberIsFloat });
            Assert.True(check.IsSuccess, string.Join("\r\n", check.Errors.Select(ee => ee.Message)));

            FormulaValue result = check.GetEvaluator().EvalAsync(CancellationToken.None, dv.SymbolValues).Result;
            Assert.NotNull(result);
            Assert.False(result is ErrorValue);

            return result;
        }

        // DataverseEntityCache2 is only used to count refreshes
        public class DataverseEntityCache2 : DataverseEntityCache
        {
            public int RefreshCount = 0;

            public DataverseEntityCache2(IDataverseServices innerService, int maxEntries = 4096, TimeSpan cacheLifeTime = default(TimeSpan))
                : base(innerService, maxEntries, cacheLifeTime)
            {
            }

            public override void Refresh(string logicalTableName)
            {
                RefreshCount++;
                base.Refresh(logicalTableName);
            }
        }
    }
}
