//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Dataverse.Tests;
using Microsoft.PowerFx.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace Microsoft.PowerFx.Dataverse.Cached.Tests
{
    [TestClass]
    public class CachedTests
    {
        private const string CACHE_ROOT = @"C:\temp\";
        private static TestContext _testContext;

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            _testContext = context;
        }

        [TestMethod]
        public void TestWithCache()
        {
            string tableName = "Accounts";
            string expr = "First(Accounts)";
            List<IDisposable> disposableObjects = null;

            try
            {
                string folder = GetCachedData("CachedData01.zip");
                FormulaValue formulaValue = RunDataverseTest(tableName, expr, out disposableObjects, async: true, cached: true, folder: folder, noConnection: true);

                object result = formulaValue.ToObject();
                var entity = result as Entity;

                Assert.IsNotNull(entity, result is ErrorValue ev ? string.Join("\r\n", ev.Errors.Select(er => er.Message)) : "Unknown Error");
            }
            finally
            {
                DisposeObjects(disposableObjects);
            }

            Console.WriteLine(ObjectSerializer._ctr);
        }

        public static string GetCachedData(string zippedFileName, string path = null)
        {
            string cachedData = Path.Combine(path ?? _testContext.TestDeploymentDir, @$"Data\{zippedFileName}");
            string folder = Directory.GetParent(cachedData).FullName;

            ZipFile.ExtractToDirectory(cachedData, folder, overwriteFiles: true);

            return folder;
        }

        public FormulaValue RunDataverseTest(string tableName, string expr, out List<IDisposable> disposableObjects, bool async = false, bool cached = false, string folder = null, bool noConnection = false)
        {
            return RunDataverseTest(tableName, expr, out disposableObjects, out _, out _, out _, async, cached, folder, noConnection);
        }

        private CachedServiceClient GetClient(bool cached, string folder = null, bool noConnection = false)
        {
            var cx = Environment.GetEnvironmentVariable(LiveOrgExecutionTests.ConnectionStringVariable);

            // short-circuit if connection string is not set
            if (cx == null && !noConnection)
            {
                Assert.Inconclusive($"Skipping Live Dataverse tests. Set {cx} env var.");
                throw new NotImplementedException();
            }

            // https://docs.microsoft.com/en-us/power-apps/developer/data-platform/xrm-tooling/use-connection-strings-xrm-tooling-connect
            // For example:            
            // $"Url=https://aurorabapenv67c10.crm10.dynamics.com/; Username={username}; Password={password}; authtype=OAuth";

            string cacheFolder = folder ?? @$"{CACHE_ROOT}{DateTime.UtcNow.ToString("yyyy_MM_dd_HH_mm_ss_fff")}";
            CachedServiceClient svcClient = new CachedServiceClient(cx, cached, cacheFolder);
            svcClient.EnableAffinityCookie = true;

            return svcClient;
        }

        public FormulaValue RunDataverseTest(string tableName, string expr, out List<IDisposable> disposableObjects, out RecalcEngine engine, out ReadOnlySymbolTable symbols, out ReadOnlySymbolValues runtimeConfig,
           bool async = false, bool cached = false, string folder = null, bool noConnection = false)
        {
            CachedServiceClient svcClient = GetClient(cached, folder, noConnection);
            XrmMetadataProvider xrmMetadataProvider = new XrmMetadataProvider(svcClient);
            disposableObjects = new List<IDisposable>() { svcClient };

            if (!LiveOrgExecutionTests.PredefinedTables.TryGetValue(tableName, out string logicalName))
            {
                bool b1 = xrmMetadataProvider.TryGetLogicalName(tableName, out logicalName);
                Assert.IsTrue(b1);
            }

            DataverseConnection dv = null;

            if (async)
            {
                var asyncClient = new DataverseAsyncClient(svcClient);
                disposableObjects.Add(asyncClient);
                dv = new DataverseConnection(asyncClient, new XrmMetadataProvider(svcClient));
            }
            else
            {
                dv = new DataverseConnection(svcClient);
            }

            TableValue tableValue = dv.AddTable(variableName: tableName, tableLogicalName: logicalName);
            symbols = ReadOnlySymbolTable.Compose(dv.GetRowScopeSymbols(tableLogicalName: logicalName), dv.Symbols);

            Assert.IsNotNull(tableValue);
            Assert.IsNotNull(symbols);

            var config = new PowerFxConfig();
            config.SymbolTable.EnableMutationFunctions();
            engine = new RecalcEngine(config);
            runtimeConfig = dv.SymbolValues;

            if (string.IsNullOrEmpty(expr))
            {
                return null;
            }

            CheckResult check = engine.Check(expr, symbolTable: symbols, options: new ParserOptions() { AllowsSideEffects = true, NumberIsFloat = PowerFx2SqlEngine.NumberIsFloat });
            Assert.IsTrue(check.IsSuccess, string.Join("\r\n", check.Errors.Select(ee => ee.Message)));

            IExpressionEvaluator run = check.GetEvaluator();
            FormulaValue result = run.EvalAsync(CancellationToken.None, runtimeConfig).Result;

            return result;
        }

        public void DisposeObjects(List<IDisposable> objects)
        {
            if (objects != null)
            {
                foreach (IDisposable obj in objects)
                {
                    obj.Dispose();
                }
            }
        }
    }
}
