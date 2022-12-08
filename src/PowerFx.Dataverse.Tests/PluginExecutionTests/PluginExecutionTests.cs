//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    // Excercise runing dataverse compiler from a plugin
    // In a plugin:
    // - use PowerFx2SqlEngine to convert EntityMetadata into RecordType and generate IR. 
    // - wrap Entity as RecordValue (via XrmRecordValue)
    // - exceute the IR via the interpreter (rather than compiling to SQL).
    [TestClass]
    public class PluginExecutionTests
    {
        EntityMetadataModel _trivialModel = new EntityMetadataModel
        {
            Attributes = new AttributeMetadataModel[]
            {
                new AttributeMetadataModel
                {
                    LogicalName= "new_field",
                    DisplayName = "field",
                    AttributeType = AttributeTypeCode.Decimal
                },
            }
        };

        // Verify we can convert EntityMetadata to RecordType
        [TestMethod]
        public void ConvertMetadata()
        {
            var rawProvider = new MockXrmMetadataProvider(_trivialModel);
            var provider = new CdsEntityMetadataProvider(rawProvider);

            var recordType = provider.GetRecordType(_trivialModel.LogicalName);

            Assert.AreEqual("![new_field:n]", recordType._type.ToString());

            var field = recordType.GetFieldTypes().First();
            Assert.AreEqual("new_field", field.Name);
            Assert.AreEqual(FormulaType.Number, field.Type);

            // $$$ Fails?
            // Assert.AreEqual("field", field.DisplayName);
        }

        [TestMethod]
        public void TestFailure()
        {
            Assert.Fail("This test should fail");
        }

        // Wrapper to track calls to TryGetEntityMetadata.
        // In pracitce, these are expensive and we need to limit them. 
        public class TrackingXrmMetadataProvider : IXrmMetadataProvider
        {
            private readonly IXrmMetadataProvider _inner;
            public List<string> _requests = new List<string>();

            public TrackingXrmMetadataProvider(IXrmMetadataProvider inner)
            {
                _inner = inner;
            }

            public bool TryGetEntityMetadata(string logicalOrDisplayName, out EntityMetadata entity)
            {
                _requests.Add(logicalOrDisplayName);
                return _inner.TryGetEntityMetadata(logicalOrDisplayName, out entity);
            }
        }

        // Verify we can convert EntityMetadata to RecordType
        [TestMethod]
        public void ConvertMetadataLazy()
        {
            var localName = DataverseTests.LocalModel.LogicalName;

            var rawProvider = new TrackingXrmMetadataProvider(
                new MockXrmMetadataProvider(DataverseTests.RelationshipModels)
            );

            // Passing in a display dictionary avoids unecessary calls to to metadata lookup.
            var disp = new Dictionary<string, string>
            {
                { "local", "Locals" },
                { "remote", "Remotes"  }
            };

            var provider = new CdsEntityMetadataProvider(rawProvider, disp);

            // Shouldn't access any other metadata (via relationships). 
            var reqs = rawProvider._requests;

            var recordType1 = provider.GetRecordType(localName);

            // Should not have requested anything else. 
            Assert.AreEqual(1, reqs.Count);
            Assert.AreEqual("local", reqs[0]);

            reqs.Clear();

            // 2nd attempt hits the cache
            provider.GetRecordType(localName);
            Assert.AreEqual(0, reqs.Count);

            // Now accessing Remote succeeds.             
            var recordType2 = (RecordType)recordType1.GetFieldType("refg"); // name for "Other" field.
            Assert.AreEqual(1, reqs.Count);
            Assert.AreEqual("remote", reqs[0]);

            var field2 = recordType2.GetFieldType("data"); // number
            Assert.IsInstanceOfType(field2, typeof(NumberType));
        }

        [TestMethod]
        public void Check()
        {
            var rawProvider = new MockXrmMetadataProvider(_trivialModel);
            var provider = new CdsEntityMetadataProvider(rawProvider);

            var metadata = _trivialModel.ToXrm();
            var engine = new DataverseEngine(metadata, provider, new PowerFxConfig());

            var check = engine.Check("1 + ThisRecord.field");

            Assert.IsTrue(check.IsSuccess);
            Assert.AreEqual(FormulaType.Number, check.ReturnType);

            Assert.IsNotNull(check._binding);
        }

        // Ensure a custom function shows up in intellisense. 
        [TestMethod]
        public void IntellisenseWithCustomFuncs()
        {
            var config = new PowerFxConfig();
            config.AddFunction(new DoubleItFunction());
            var engine = new DataverseEngine(null, null, config);

            var results = engine.Suggest("DoubleI", 7);
            var list = results.Suggestions.ToArray();
            Assert.AreEqual(1, list.Length);

            var x = list[0];
            Assert.AreEqual(x.Kind, SuggestionKind.Function);
            Assert.AreEqual("DoubleIt", x.DisplayText.Text);
        }

        // Verify we can exceute an IR from the Sql compiler, 
        // and add custom functions. 
        [TestMethod]
        public void ExecuteBasic()
        {
            var rawProvider = new MockXrmMetadataProvider(_trivialModel);
            var provider = new CdsEntityMetadataProvider(rawProvider);

            var metadata = _trivialModel.ToXrm();

            var config = new PowerFxConfig();
            config.AddFunction(new DoubleItFunction());
            var engine = new DataverseEngine(metadata, provider, config);

            var check = engine.Check("DoubleIt(ThisRecord.field) + 10");

            Assert.IsTrue(check.IsSuccess);
            Assert.AreEqual(FormulaType.Number, check.ReturnType);

            var run = RecalcEngine.CreateEvaluatorDirect(check);

            // Approximate an entity with a RecordValue
            var cache = new TypeMarshallerCache();
            var record = (RecordValue)cache.Marshal(new { new_field = 15 });
            var result = run.Eval(record);

            Assert.AreEqual(40.0, result.ToObject());
        }

        // $$$ OptionSets don't work:
        // Expression:   First(t1).Rating
        //  actual :      local_rating_optionSet.2     !!! 
        //  expected: 'Rating (Locals)'.Warm     // but this won't parse, needs metadata.

        [DataTestMethod]
        [DataRow("First(t1).Price", "100")] // trivial 
        [DataRow("t1", "t1")] // table
        [DataRow("First(t1)", "LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\"))")] // record
        [DataRow("LookUp(t1, false)", "If(false,First(FirstN(t1,0)))")] // blank
        [DataRow("First(t1).LocalId", "GUID(\"00000000000000000000000000000001\")")] // Guid
        public async Task TestSerialize(string expr, string expectedSerialized)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            var engine = new RecalcEngine();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues);

            // Test the serializer! 
            var serialized = result.ToExpression();

            Assert.AreEqual(expectedSerialized, serialized);

            // Deserialize. 
            var result2 = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues);            
        }

        [TestMethod]
        public async Task TestSerializeBasic()
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName); 
            
            var id = "00000000-0000-0000-0000-000000000001";
            var entityOriginal = el.LookupRef(new EntityReference(logicalName, Guid.Parse(id)), CancellationToken.None);
            RecordValue record = dv.Marshal(entityOriginal);

            // Test the serializer! 
            var expr = record.ToExpression();             

            // Should be short form - not flattened.  
            Assert.AreEqual("LookUp(t1, localid=GUID(\"" + id + "\"))", expr);

            // Deserialize 
            var engine = new RecalcEngine();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues);
            
            var entity = (Entity) result.ToObject();
            Assert.IsNotNull(entity); // such as if Lookup() failed and we got blank

            Assert.AreEqual(entityOriginal.LogicalName, entity.LogicalName);
            Assert.AreEqual(entityOriginal.Id, entity.Id);
        }

        [TestMethod]
        public async Task TestSerializeBasic2()
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);


            var id = "00000000-0000-0000-0000-000000000001";
            var entityOriginal = el.LookupRef(new EntityReference(logicalName, Guid.Parse(id)), CancellationToken.None);
            RecordValue record = await dv.RetrieveAsync(logicalName, Guid.Parse(id)) as RecordValue;

            // Test the serializer! 
            var expr = record.ToExpression();

            // Should be short form - not flattened.  
            Assert.AreEqual("LookUp(t1, localid=GUID(\"" + id + "\"))", expr);

            // Deserialize 
            var engine = new RecalcEngine();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues);

            var entity = (Entity)result.ToObject();
            Assert.IsNotNull(entity); // such as if Lookup() failed and we got blank

            Assert.AreEqual(entityOriginal.LogicalName, entity.LogicalName);
            Assert.AreEqual(entityOriginal.Id, entity.Id);
        }

        [TestMethod]
        public async Task TestSerializeBasic3()
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);


            var id = "00000000-0000-0000-0000-000000000001";
            var entityOriginal = el.LookupRef(new EntityReference(logicalName, Guid.Parse(id)), CancellationToken.None);
            RecordValue record = (await dv.RetrieveMultipleAsync(logicalName, new[] { Guid.Parse(id) }))[0] as RecordValue;

            // Test the serializer! 
            var expr = record.ToExpression();

            // Should be short form - not flattened.  
            Assert.AreEqual("LookUp(t1, localid=GUID(\"" + id + "\"))", expr);

            // Deserialize 
            var engine = new RecalcEngine();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues);

            var entity = (Entity)result.ToObject();
            Assert.IsNotNull(entity); // such as if Lookup() failed and we got blank

            Assert.AreEqual(entityOriginal.LogicalName, entity.LogicalName);
            Assert.AreEqual(entityOriginal.Id, entity.Id);
        }

        [TestMethod]
        public async Task TestSerializeBlank()
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName); 

            RecordType type = dv.GetRecordType(logicalName);
            var blank = FormulaValue.NewBlank(type);

            // Test the serializer! 
            var expr = blank.ToExpression();

            // Should be short form - not flattened.  
            Assert.AreEqual("If(false,First(FirstN(t1,0)))", expr);

            // Deserialize 
            var engine = new RecalcEngine();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues);

            var entity = (Entity)result.ToObject();
            Assert.IsNull(entity); // 
        }

        // Serialize the entire table 
        [TestMethod]
        public async Task TestSerializeTable()
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            TableValue table = dv.AddTable(displayName, logicalName); 
                        
            // Test the serializer!             
            var expr = table.ToExpression();

            // Should be short form - not Table() or some other literal.
            Assert.AreEqual("t1", expr);

            // Deserialize 
            var engine = new RecalcEngine();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues);

            Assert.IsInstanceOfType(result, typeof(DataverseTableValue));
        }

        // Serialize the entire table 
        [TestMethod]
        public void TestSerializeRelationship()
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            var logicalName2 = "remote";
            var displayName2 = "t2";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            TableValue table1 = dv.AddTable(displayName, logicalName);

            var engine = new RecalcEngine();
            Func<string, DataverseConnection, FormulaValue> eval = 
                (expr, dv) => engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues).Result;

            // Relationship refers to a type that we didn't AddTable for. 
            var result = eval("First(t1).Other", dv); // reference to Remote (t2)            

            var expr1Serialized = result.ToExpression();
            Assert.AreEqual("LookUp(remote, remoteid=GUID(\"00000000-0000-0000-0000-000000000002\"))", expr1Serialized);

            // Now add the table.
            TableValue table2 = dv.AddTable(displayName2, logicalName2);

            // Reserializing will fetch from the connection and use the updated name. 
            var expr1bSerialized = result.ToExpression();
            Assert.AreEqual("LookUp(t2, remoteid=GUID(\"00000000-0000-0000-0000-000000000002\"))", expr1bSerialized);
            
            // Compare relationship to direct lookup
            var expr3 = "First(t2)";
            var result3 = eval(expr3, dv);
            var expr3Serialized = result3.ToExpression();

            Assert.AreEqual("LookUp(t2, remoteid=GUID(\"00000000-0000-0000-0000-000000000002\"))", expr3Serialized);
        }


        // Run with 2 tables registered. 
        [DataTestMethod]
        [DataRow("First(t1).Other.Data",200.0)]
        [DataRow("First(t2).Data", 200.0)]
        [DataRow("First(t1).Other.remoteid = First(t2).remoteid", true)] // same Id
        [DataRow("If(true, First(t1).Other, First(t2)).Data", 200.0)] // Compatible for comparison 
        public void ExecuteViaInterpreter2tables(string expr, object expected)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            var logicalName2 = "remote";
            var displayName2 = "t2";

            var engine = new RecalcEngine();
            Func<string, DataverseConnection, FormulaValue> eval =
                (expr, dv) => engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues).Result;

            // Create new org (symbols) with both tables 
            (DataverseConnection dv2, EntityLookup el2) = CreateMemoryForRelationshipModels();
            dv2.AddTable(displayName, logicalName);
            dv2.AddTable(displayName2, logicalName2);
            
            var result = eval(expr, dv2);
            Assert.AreEqual(expected, result.ToObject());         
        }

        [DataTestMethod]

        // Row Scope
        [DataRow("new_price + 10", 110.0)] // Basic field lookup (RowScope) w/ logical names        
        [DataRow("Price + 10", 110.0, true)] //using Display name for Price
        [DataRow("ThisRecord.Other.Data", 200.0)] // Relationship 
        [DataRow("ThisRecord.Other.remoteid = GUID(\"00000000-0000-0000-0000-000000000002\")", true)] // Relationship 
        [DataRow("ThisRecord.Price + 10", 110.0, true)] // Basic field lookup (RowScope)
        [DataRow("ThisRecord.Rating = 'Rating (Locals)'.Warm", true)] // Option Sets                 

        // Single Global record
        [DataRow("First(t1).new_price", 100.0, false)]
        [DataRow("First(t1).Price", 100.0, false)]

        // Aggregates
        [DataRow("CountRows(Filter(t1, ThisRecord.Price > 50))", 1.0, false)] // Filter
        [DataRow("Sum(Filter(t1, ThisRecord.Price > 50), ThisRecord.Price)", 100.0, false)] // Filter
        [DataRow("Sum(Filter(t1, ThisRecord.Price > 50) As X, X.Price)", 100.0, false)] // with Alias        
        public void ExecuteViaInterpreter2(string expr, object expected, bool rowScope = true)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            var rowScopeSymbols = rowScope ? dv.GetRowScopeSymbols(tableLogicalName: logicalName) : null;
            var symbols = ReadOnlySymbolTable.Compose(rowScopeSymbols, dv.Symbols);

            var engine1 = new RecalcEngine();
            var check = engine1.Check(expr, symbolTable: symbols);
            Assert.IsTrue(check.IsSuccess);

            // Eval it 
            ReadOnlySymbolValues runtimeConfig;
            if (rowScopeSymbols != null)
            {
                var record = el.ConvertEntityToRecordValue(logicalName, dv, CancellationToken.None); // any record
                var rowScopeValues = ReadOnlySymbolValues.NewFromRecord(rowScopeSymbols, record);
                runtimeConfig = ReadOnlySymbolValues.Compose(rowScopeValues, dv.SymbolValues);
            }
            else
            {
                runtimeConfig = dv.SymbolValues;
            }

            var run = check.GetEvaluator();
            var result = run.EvalAsync(CancellationToken.None, runtimeConfig).Result;

            Assert.AreEqual(expected, result.ToObject());
        }

        // Set() function against entity fields in RowScope
        [DataTestMethod]
        [DataRow("Set(Price, 200); Price")]
        public void LocalSet(string expr)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            var rowScopeSymbols = dv.GetRowScopeSymbols(tableLogicalName: logicalName);

            var opts = new ParserOptions { AllowsSideEffects = true };
            var config = new PowerFxConfig(); // Pass in per engine
            config.EnableSetFunction();
            var engine1 = new RecalcEngine(config);
            
            var check = engine1.Check(expr, options: opts, symbolTable: rowScopeSymbols);
            Assert.IsTrue(check.IsSuccess);

            var run = check.GetEvaluator();

            var entity = el.GetFirstEntity(logicalName, dv, CancellationToken.None); // any record
            var record = dv.Marshal(entity);
            var rowScopeValues = ReadOnlySymbolValues.NewFromRecord(rowScopeSymbols, record);

            var result = run.EvalAsync(CancellationToken.None, rowScopeValues).Result;

            Assert.AreEqual(200.0, result.ToObject());

            // RecordValue is updated .
            Assert.AreEqual(200.0, record.GetField("new_price").ToObject());

            Assert.AreEqual(new Decimal(200.0), entity.Attributes["new_price"]);

            // verify on entity 
            var e2 = el.LookupRef(entity.ToEntityReference(), CancellationToken.None);
            Assert.AreEqual(new Decimal(200.0), e2.Attributes["new_price"]);
        }

        // Patch() function against entity fields in RowScope
        [DataTestMethod]
        [DataRow("Patch(t1, First(t1), { Price : 200}); First(t1).Price", 200)]
        [DataRow("With( { x : First(t1)}, Patch(t1, x, { Price : 200}); x.Price)", 100)] // Expected, x.Price is still old value!
        // [DataRow("Patch(t1, First(t1), { Price : 200}).Price")] // https://github.com/microsoft/Power-Fx/issues/852
        public void PatchFunction(string expr, double expected)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            var opts = new ParserOptions { AllowsSideEffects = true };
            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check.IsSuccess);

            var run = check.GetEvaluator();

            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            // Verify on expression - this may be old or no
            Assert.AreEqual(expected, result.ToObject());

            // verify on entity - this should always be updated 
            var r2 = engine1.EvalAsync("First(t1)", CancellationToken.None, runtimeConfig: dv.SymbolValues).Result;
            var entity = (Entity) r2.ToObject();
            var e2 = el.LookupRef(entity.ToEntityReference(), CancellationToken.None);
            Assert.AreEqual(new Decimal(200.0), e2.Attributes["new_price"]);
        }

        [DataTestMethod]
        [DataRow("Remove(t1, LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\")) )")]
        public void RemoveFunction(string expr)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            var opts = new ParserOptions { AllowsSideEffects = true };
            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check.IsSuccess);

            var run = check.GetEvaluator();

            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            // Verify on expression - this may be old or no

            // verify on entity - this should always be updated 
            Assert.IsFalse(el.Exists(new EntityReference(logicalName, _g1)));            
        }

        [TestMethod]
        public void BasicSymbols()
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            // We get same symbols back - this is important since Check / Eval need to match. 
            var sym1 = dv.GetRowScopeSymbols(tableLogicalName: logicalName);
            var sym2 = dv.GetRowScopeSymbols(tableLogicalName: logicalName);

            Assert.AreSame(sym1, sym2);
        }

        // Test blank references.
        [DataTestMethod]
        [DataRow("ThisRecord.Other.Data")] // Relationship 
        public void RecordBlank(string expr)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            // Set to blank. 
            var entity1 = el.LookupRefCore(_eRef1);
            entity1.Attributes["refg"] = null;
            entity1.Attributes["otherid"] = null;

            var symbols = dv.GetRowScopeSymbols(tableLogicalName: logicalName);

            var engine1 = new RecalcEngine();

            // Eval it                        
            var record = el.ConvertEntityToRecordValue(logicalName, dv, CancellationToken.None); // any record
            var runtimeConfig = ReadOnlySymbolValues.NewFromRecord(symbols, record);
            
            var result = engine1.EvalAsync(expr, CancellationToken.None, runtimeConfig: runtimeConfig ).Result;
            Assert.IsTrue(result is BlankValue);
        }

        // Calls to Dataverse have 3 possible outcomes:
        // - Success
        // - "Soft" failure - these are translated into ErrorValues and "caught".  Eg: record not found, access denied, network down, 
        // - "Hard" failures - these are bugs in our code and their exceptions aborts the execution.  Eg: NullRef, StackOveflow, etc 
        [DataTestMethod]
        [DataRow("ThisRecord.Other.Data")] // Relationship 
        public void NetworkErrors(string expr)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            var symbols = dv.GetRowScopeSymbols(tableLogicalName: logicalName);

            var engine1 = new RecalcEngine();

            // Eval it                        
            var record = el.ConvertEntityToRecordValue(logicalName, dv, CancellationToken.None); // any record
            var runtimeConfig = ReadOnlySymbolValues.NewFromRecord(symbols, record);

            // Case 1: Succeed 
            el._onLookupRef = null;
            var result2 = engine1.EvalAsync(expr, CancellationToken.None, runtimeConfig: runtimeConfig).Result;
            Assert.AreEqual(200.0, result2.ToObject());

            // Case 2: Soft error:
            // After we have the initial Record, simulate failure. 
            // Most exceptions from the IOrganizationService will get caught and converted to ErrorValue. 
            // IOrganizationService doesn't actually specify which exceptions it produces on failure. 
            var exceptionMessage = "Inject test failure";
            el._onLookupRef = (er) => 
                throw new FaultException<OrganizationServiceFault>(
                    new OrganizationServiceFault(),
                    new FaultReason(exceptionMessage));

            var result = engine1.EvalAsync(expr, CancellationToken.None, runtimeConfig: runtimeConfig).Result;
            Assert.IsTrue(result is ErrorValue);
            var error = (ErrorValue)result;
            var errorList = error.Errors;
            Assert.AreEqual(1, errorList.Count);
            Assert.IsTrue(errorList[0].Message.Contains(exceptionMessage));

            // Case 3: Hard error:
            // "Fatal" errors can propagated exception up.
            el._onLookupRef = (er) => throw new NullReferenceException("Fake nullref");

            Assert.ThrowsExceptionAsync<NullReferenceException>(
                () => engine1.EvalAsync(expr, CancellationToken.None, runtimeConfig: runtimeConfig)).Wait();
        }

        [TestMethod]
        public void TestDataverseConnection()
        {
            (DataverseConnection dv, _) = CreateMemoryForRelationshipModels();

            // Table wasn't added yet. 
            Assert.ThrowsException<InvalidOperationException>(
                () => dv.GetRecordType("local"),
                "Must first call AddTable for local");

            var table = dv.AddTable("variableName", "local");
            Assert.AreEqual("variableName", table.Type.TableSymbolName);

            // 2nd add will fail 
            Assert.ThrowsException<InvalidOperationException>(
                () => dv.AddTable("variableName2", "local"),
                "Table with logical name 'local' was already added as variableName.");
            
            Assert.ThrowsException<InvalidOperationException>(
                () => dv.AddTable("variableName", "remote"),
                "Table with variable name 'variableName' was already added as local.");

            Assert.ThrowsException<InvalidOperationException>(
                () => dv.AddTable("variableName", "local"),
                "Table with logical name 'local' was already added as variableName.");

            RecordType r = dv.GetRecordType("local");
            Assert.AreEqual("variableName", r.TableSymbolName);

            var type = r.GetFieldType("new_price");
            Assert.IsTrue(type is NumberType);

            // Throws on missing field. 
            Assert.ThrowsException<InvalidOperationException>(
                () => r.GetFieldType("new_missing"));

            // fails, must be logical name 
            Assert.ThrowsException<InvalidOperationException>(
                () => dv.GetRecordType("Locals"));
        }

        // Verify that a single engine can access two Dataverse orgs simultanously. 
        // Since logical names are just scoped to an org, different orgs can conflict on logical names. 
        [TestMethod]
        public async Task TwoOrgs()
        {
            (DataverseConnection dv1, _) = CreateMemoryForRelationshipModels();
            (var dv2, var el2) = CreateMemoryForRelationshipModels();

            // Two orgs, can also both have the same logical name 
            dv1.AddTable("T1", "local");
            dv2.AddTable("T2", "local");

            el2.LookupRefCore(_eRef1).Attributes["new_price"] = 200;

            var engine1 = new RecalcEngine();
            var s1 = dv1.SymbolValues;
            var s2 = dv2.SymbolValues;

            var s12 = ReadOnlySymbolValues.Compose(s1, s2);

            var result = await engine1.EvalAsync("First(T1).Price*1000 + First(T2).Price", CancellationToken.None, runtimeConfig: s12);

            Assert.AreEqual(100 * 1000 + 200.0, result.ToObject());
        }

        [TestMethod]
        public void DataverseConnectionMarshal()
        {
            (DataverseConnection dv, _) = CreateMemoryForRelationshipModels();

            dv.AddTable("variableName", "local");

            // Marshal 
            var entity1 = new Entity("local", _g1);
            entity1.Attributes["new_price"] = 100;
            entity1.Attributes["rating"] = new Xrm.Sdk.OptionSetValue(2); // Warm

            var val1 = dv.Marshal(entity1);

            RecordType r = val1.Type;
            Assert.IsTrue(r.GetFieldType("new_price") is NumberType);

            // Can also get fields on metadata not present in attributes
            Assert.IsTrue(r.GetFieldType("new_quantity") is NumberType);

            // Getting fields
            var x = val1.GetField("new_price");
            Assert.AreEqual(100.0, x.ToObject());

            // Blanks - pulling from the metadata.
            x = val1.GetField("new_quantity");
            Assert.IsTrue(x is BlankValue);
            Assert.IsTrue(x.Type is NumberType);

            // OptionSets. 
            var opt = val1.GetField("rating");

            Assert.AreEqual("Warm", opt.ToObject()); 
            Assert.AreEqual("OptionSetValue (2=Warm)", opt.ToString());
        }

        [DataTestMethod]
        [DataRow("Price + 10", 110.0)] // Basic field lookup
        [DataRow("Rating = 'Rating (Locals)'.Warm", true)] // Option Sets 
        [DataRow("ThisRecord.Price + Other.Data", 300.0)]
        public void ExecuteViaInterpreter(string expr, object expected)
        {
            (var dvConnection, var entityLookup) = CreateMemoryForRelationshipModels();

            var thisRecordName = "local"; // table only has 1 entity.

            // Create context to simulate evaluating on entity in thisRecordName table.
            var record = entityLookup.ConvertEntityToRecordValue(thisRecordName, null, CancellationToken.None);
            var metadata = entityLookup.LookupMetadata(thisRecordName, CancellationToken.None);
            var engine = new DataverseEngine(metadata, new CdsEntityMetadataProvider(entityLookup._rawProvider), new PowerFxConfig());

            var check = engine.Check(expr);
            Assert.IsTrue(check.IsSuccess);
            check.ThrowOnErrors();

            var run = RecalcEngine.CreateEvaluatorDirect(check);

            var result = run.Eval(record);

            Assert.AreEqual(expected, result.ToObject());
        }

        // Test with other metadata 
        // - schema names != logicalName
        // - calculated columns 
        [TestMethod]
        public void TestSchema()
        {
            // BaselineMetadata
            var lookup = CreateMemoryForBaselineMetadata();

            var thisRecordName = "account"; // table only has 1 entity.

            // Create context to simulate evaluating on entity in thisRecordName table.
            var record = lookup.ConvertEntityToRecordValue(thisRecordName, null, CancellationToken.None);
            var metadata = lookup.LookupMetadata(thisRecordName, CancellationToken.None);
            var engine = new DataverseEngine(metadata, lookup._provider, new PowerFxConfig());

            var expr = "ThisRecord.CurrencyPrice + Calc";
            var check = engine.Check(expr);
            Assert.IsTrue(check.IsSuccess);
            check.ThrowOnErrors();

            var run = RecalcEngine.CreateEvaluatorDirect(check);

            var result = run.Eval(record);

            Assert.AreEqual(250.0, result.ToObject());
        }

                
        static readonly Guid _g1 = new Guid("00000000-0000-0000-0000-000000000001");
        static readonly Guid _g2 = new Guid("00000000-0000-0000-0000-000000000002");

        static readonly EntityMetadata _localMetadata = DataverseTests.LocalModel.ToXrm();
        static readonly EntityMetadata _remoteMetadata = DataverseTests.RemoteModel.ToXrm();

        static readonly EntityReference _eRef1 = new EntityReference("local", _g1);

        // Create Entity objects to match DataverseTests.RelationshipModels;
        private (DataverseConnection, EntityLookup) CreateMemoryForRelationshipModels()
        {
            var entity1 = new Entity("local", _g1);
            var entity2 = new Entity("remote", _g2);

            entity1.Attributes["new_price"] = 100;
            entity1.Attributes["otherid"] = entity2.ToEntityReference();
            entity1.Attributes["rating"] = new Xrm.Sdk.OptionSetValue(2); // Warm

            // $$$ This is from ReferencingEntityNavigationPropertyName
            // This is the field we end up looking up when we try to access 'otherid'.
            entity1.Attributes["refg"] = entity1.Attributes["otherid"];
            entity2.Attributes["data"] = 200;

            MockXrmMetadataProvider xrmMetadataProvider = new MockXrmMetadataProvider(DataverseTests.RelationshipModels);
            EntityLookup entityLookup = new EntityLookup(xrmMetadataProvider);
            DataverseConnection dvConnection = new DataverseConnection(entityLookup, xrmMetadataProvider);
            entityLookup.Add(CancellationToken.None, entity1, entity2);

            return (dvConnection, entityLookup);
        }

        // Create Entity objects to match DataverseTests.BaselineMetadata;
        private EntityLookup CreateMemoryForBaselineMetadata()
        {
            var metadata = DataverseTests.BaselineMetadata;
            var entity1 = new Entity("account", _g1);

            var logicalName = "new_CurrencyPrice";
            entity1.Attributes[logicalName] = 100;

            // Ensure that these are all different:
            //  - logical name (what appears in API) - this will be in IR
            //  - display name - what appears to user, localized
            //  - schema name - physical name in database, used in SQL generation. 
            var attr = metadata.Attributes.Where(x => x.LogicalName == logicalName).First();
            Assert.AreEqual(attr.DisplayName, "CurrencyPrice");
            Assert.AreEqual(attr.SchemaName, "new_CurrencyPrice_Schema");

            // Calculated field test
            entity1.Attributes["new_Calc"] = 150;
            var attr2 = metadata.Attributes.Where(x => x.LogicalName == "new_Calc").First();
            Assert.AreEqual(3, attr2.SourceType); // this is a calc filed. 

            EntityLookup lookup = new EntityLookup(new MockXrmMetadataProvider(metadata));
            lookup.Add(CancellationToken.None, entity1, entity1);
            return lookup;
        }
    }

    // Example of a custom function
    public class DoubleItFunction : ReflectionFunction
    {
        public NumberValue Execute(NumberValue value)
        {
            return FormulaValue.New(value.Value * 2);
        }
    }
}