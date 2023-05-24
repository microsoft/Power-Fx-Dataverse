using Microsoft.PowerFx.Dataverse.Tests;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;

namespace TestApp
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string tableName = "Accounts";
            string expr = "First(Accounts)";
            List<IDisposable> disposableObjects = null;
            LiveOrgExecutionTests et = new LiveOrgExecutionTests();

            try
            {
                string folder = null; // @"C:\Temp\2023_05_11_15_00_36_413";
                FormulaValue result = et.RunDataverseTest(tableName, expr, out disposableObjects, async: true, cached: true, folder: folder);

                var obj = result.ToObject() as Entity;
                Console.WriteLine("OK!");
            }
            finally
            {
                et.DisposeObjects(disposableObjects);
            }
        }
    }
}
