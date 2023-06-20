//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk;
using System;
using System.Linq;
using System.Threading;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    [TestClass]
    public class RefreshTests
    {
        internal static readonly EntityMetadataModel Accounts = new EntityMetadataModel
        {
            LogicalName = "Accounts",
            DisplayCollectionName = "Accounts",
            PrimaryIdAttribute = "accountid",
            Attributes = new AttributeMetadataModel[]
            {
                AttributeMetadataModel.NewString("accountname", "Account Name"),
                AttributeMetadataModel.NewGuid("accountid", "Account Id"),
            }
        };

        [TestMethod]
        public void RefreshTest()
        {
            string logicalName = "Accounts";
            string tableName = "Accounts";

            Entity entity1 = new Entity("Accounts", Guid.NewGuid());
            entity1.Attributes.Add("Account Name", "Account0");

            MockXrmMetadataProvider xrmMetadataProvider = new MockXrmMetadataProvider(RefreshTests.Accounts);
            EntityLookup entityLookup = new EntityLookup(xrmMetadataProvider);
            entityLookup.Add(CancellationToken.None, entity1);

            DataverseEntityCache2 ds = new DataverseEntityCache2(entityLookup);
            CdsEntityMetadataProvider metadataCache = new CdsEntityMetadataProvider(xrmMetadataProvider);
            DataverseConnection dv = new DataverseConnection(null, ds, metadataCache, maxRows: 999);

            TableValue tableValue = dv.AddTable(variableName: tableName, tableLogicalName: logicalName);
            ReadOnlySymbolTable symbols = ReadOnlySymbolTable.Compose(dv.GetRowScopeSymbols(tableLogicalName: logicalName), dv.Symbols); ;

            PowerFxConfig config = new PowerFxConfig(Features.PowerFxV1);
            config.SymbolTable.EnableMutationFunctions();

            RecalcEngine engine = new RecalcEngine(config);
            ReadOnlySymbolValues runtimeConfig = dv.SymbolValues;

            _ = Evaluate("Collect(Accounts, {'Account Name': \"Account1\" })", dv, engine);
            Assert.AreEqual(1, ds.RefreshCount); // Collect calls refresh
            Assert.AreEqual(0, ds.CacheSize);    // No cache

            _ = Evaluate("First(Accounts)", dv, engine);
            Assert.AreEqual(1, ds.RefreshCount); // No change
            Assert.AreEqual(2, ds.CacheSize);    // We have 2 rows: Account0 and Account1

            _ = Evaluate("Refresh(Accounts)", dv, engine);
            Assert.AreEqual(2, ds.RefreshCount); // Refresh called
            Assert.AreEqual(0, ds.CacheSize);    // Cache cleared
        }

        private FormulaValue Evaluate(string formula, DataverseConnection dv, RecalcEngine engine)
        {
            CheckResult check = engine.Check(formula, symbolTable: dv.Symbols, options: new ParserOptions() { AllowsSideEffects = true, NumberIsFloat = PowerFx2SqlEngine.NumberIsFloat });
            Assert.IsTrue(check.IsSuccess, string.Join("\r\n", check.Errors.Select(ee => ee.Message)));

            FormulaValue result = check.GetEvaluator().EvalAsync(CancellationToken.None, dv.SymbolValues).Result;
            Assert.IsNotNull(result);
            Assert.IsFalse(result is ErrorValue);

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
