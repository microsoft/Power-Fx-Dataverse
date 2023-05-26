using Microsoft.PowerFx.Dataverse.Tests;
using Microsoft.PowerFx.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk;

namespace TestApp
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string tableName = "Accounts";
            string expr = "First(Accounts)";
            List<IDisposable> disposableObjects = null;
            var loet = new LiveOrgExecutionTests();

            try
            {                
                string folder = LiveOrgExecutionTests.GetCachedData("CachedData01.zip", @"C:\Data\Power-Fx-Dataverse\src\TestApp\bin\Release\net7.0");
                FormulaValue formulaValue = loet.RunDataverseTest(tableName, expr, out disposableObjects, async: true, cached: true, folder: folder, noConnection: true);

                object result = formulaValue.ToObject();
                var entity = result as Entity;

                Assert.IsNotNull(entity, result is ErrorValue ev ? string.Join("\r\n", ev.Errors.Select(er => er.Message)) : "Unknown Error");
            }
            finally
            {
                loet.DisposeObjects(disposableObjects);
            }
        }
    }
}