//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Xunit.Sdk;

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
        ParserOptions _parserAllowSideEffects = new ParserOptions
        {
            AllowsSideEffects = true
        };

        ParserOptions _parserAllowSideEffects_NumberIsFloat = new ParserOptions
        {
            AllowsSideEffects = true,
            NumberIsFloat = PowerFx2SqlEngine.NumberIsFloat
        };

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
            var provider = new CdsEntityMetadataProvider(rawProvider) { NumberIsFloat = false }; 

            var recordType = provider.GetRecordType(_trivialModel.LogicalName);

            Assert.AreEqual("![new_field:w]", recordType._type.ToString());

            var field = recordType.GetFieldTypes().First();
            Assert.AreEqual("new_field", field.Name);
            Assert.AreEqual(FormulaType.Decimal, field.Type);

            // $$$ Fails?
            // Assert.AreEqual("field", field.DisplayName);
        }

        // Verify we can convert EntityMetadata to RecordType
        [TestMethod]
        public void ConvertMetadataFloat()
        {
            var rawProvider = new MockXrmMetadataProvider(_trivialModel);
            var provider = new CdsEntityMetadataProvider(rawProvider) { NumberIsFloat = true };

            var recordType = provider.GetRecordType(_trivialModel.LogicalName);

            Assert.AreEqual("![new_field:n]", recordType._type.ToString());

            var field = recordType.GetFieldTypes().First();
            Assert.AreEqual("new_field", field.Name);
            Assert.AreEqual(FormulaType.Number, field.Type);

            // $$$ Fails?
            // Assert.AreEqual("field", field.DisplayName);
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
            var rawProvider = new TrackingXrmMetadataProvider(new MockXrmMetadataProvider(DataverseTests.RelationshipModels));

            // Passing in a display dictionary avoids unecessary calls to to metadata lookup.
            var disp = new Dictionary<string, string>
            {
                { "local", "Locals" },
                { "remote", "Remotes"  }
            };

            var provider = new CdsEntityMetadataProvider(rawProvider, disp) { NumberIsFloat = false };

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
            Assert.IsInstanceOfType(field2, typeof(DecimalType));
        }

        // Verify we can convert EntityMetadata to RecordType
        [TestMethod]
        public void ConvertMetadataLazyFloat()
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

            var provider = new CdsEntityMetadataProvider(rawProvider, disp) { NumberIsFloat = true };

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

        // Lookup missing field 
        [TestMethod]
        public void MetadataChecksMissingField()
        {
            var metadata = _trivialModel.ToXrm();
            var ok = metadata.TryGetRelationship("missing", out var attr);
            Assert.IsFalse(ok);
            Assert.IsNull(attr);

            ok = metadata.TryGetAttribute("missing", out var amd);
            Assert.IsFalse(ok);
            Assert.IsNull(amd);
        }

        [TestMethod]
        public void MetadataChecks()
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
            var ok = provider.TryGetXrmEntityMetadata(localName, out var entityMetadata);
            Assert.IsTrue(ok);
            Assert.IsNotNull(entityMetadata);

            // Lookup non-relationship field
            var logicalName = "new_price";
            var displayName = "Price";
            ok = entityMetadata.TryGetAttribute(logicalName, out var amd);
            Assert.IsTrue(ok);
            Assert.AreEqual(amd.LogicalName, logicalName);

            ok = entityMetadata.TryGetAttribute(logicalName.ToUpper(), out amd);
            Assert.IsFalse(ok, "case sensitive lookup");
            Assert.IsNull(amd);

            ok = entityMetadata.TryGetAttribute(displayName, out amd);
            Assert.IsNull(amd);
            Assert.IsFalse(ok, "only logical names, Not display names");

            // Relationships.
            // "refg" is the relationship for "otherid" attribute.
            ok = entityMetadata.TryGetAttribute("otherid", out amd);
            Assert.IsTrue(ok);

            ok = entityMetadata.TryGetRelationship("otherid", out var relationshipName);
            Assert.IsFalse(ok, "Attribute is not a relationship");
            Assert.IsNull(relationshipName);

            ok = entityMetadata.TryGetRelationship("refg", out relationshipName);
            Assert.IsTrue(ok);
            Assert.AreEqual("otherid", relationshipName);

            ok = entityMetadata.TryGetRelationship("Refg", out relationshipName);
            Assert.IsFalse(ok, "not case sensitive");
            Assert.IsNull(relationshipName);
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

            Assert.IsNotNull(check.Binding);
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
        public void CompileBasic()
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
        }

        // $$$ OptionSets don't work:
        // Expression:   First(t1).Rating
        //  actual :      local_rating_optionSet.2     !!! 
        //  expected: 'Rating (Locals)'.Warm     // but this won't parse, needs metadata.

        [DataTestMethod]
        [DataRow("First(t1).Price", "Decimal(100)")] // trivial 
        [DataRow("t1", "t1")] // table
        [DataRow("First(t1)", "LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\"))")] // record
        [DataRow("LookUp(t1, false)", "If(false,First(FirstN(t1,0)))")] // blank
        [DataRow("First(t1).LocalId", "GUID(\"00000000-0000-0000-0000-000000000001\")")] // Guid
        public async Task TestSerialize(string expr, string expectedSerialized)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            var engine = new RecalcEngine();
            engine.EnableDelegation();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues).ConfigureAwait(false);

            // Test the serializer! 
            var serialized = result.ToExpression();

            Assert.AreEqual(expectedSerialized, serialized);

            // Deserialize. 
            var result2 = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues).ConfigureAwait(false);
        }

        [DataTestMethod]
        [DataRow("First(t1).Price", "Float(100)")] // trivial 
        [DataRow("t1", "t1")] // table
        [DataRow("First(t1)", "LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\"))")] // record
        [DataRow("LookUp(t1, false)", "If(false,First(FirstN(t1,0)))")] // blank
        [DataRow("First(t1).LocalId", "GUID(\"00000000-0000-0000-0000-000000000001\")")] // Guid
        public async Task TestSerializeFloat(string expr, string expectedSerialized)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels(numberIsFloat: true);
            dv.AddTable(displayName, logicalName);

            var engine = new RecalcEngine();
            engine.EnableDelegation();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues).ConfigureAwait(false);

            // Test the serializer! 
            var serialized = result.ToExpression();

            Assert.AreEqual(expectedSerialized, serialized);

            // Deserialize. 
            var result2 = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues).ConfigureAwait(false);
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
            engine.EnableDelegation();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues).ConfigureAwait(false);

            var entity = (Entity)result.ToObject();
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
            RecordValue record = await dv.RetrieveAsync(logicalName, Guid.Parse(id)).ConfigureAwait(false) as RecordValue;

            // Test the serializer! 
            var expr = record.ToExpression();

            // Should be short form - not flattened.  
            Assert.AreEqual("LookUp(t1, localid=GUID(\"" + id + "\"))", expr);

            // Deserialize 
            var engine = new RecalcEngine();
            engine.EnableDelegation();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues).ConfigureAwait(false);

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
            RecordValue record = (await dv.RetrieveMultipleAsync(logicalName, new[] { Guid.Parse(id) }).ConfigureAwait(false))[0] as RecordValue;

            // Test the serializer! 
            var expr = record.ToExpression();

            // Should be short form - not flattened.  
            Assert.AreEqual("LookUp(t1, localid=GUID(\"" + id + "\"))", expr);

            // Deserialize 
            var engine = new RecalcEngine();
            engine.EnableDelegation();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues).ConfigureAwait(false);

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
            engine.EnableDelegation();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues).ConfigureAwait(false);

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
            engine.EnableDelegation();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues).ConfigureAwait(false);

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
            engine.EnableDelegation();
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
        // Deimcal values can't be used in test DataRow, converted to decimal at the test
        [DataTestMethod]
        [DataRow("First(t1).Other.Data", 200.0)]
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
            engine.EnableDelegation();
            Func<string, DataverseConnection, FormulaValue> eval =
                (expr, dv) => engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues).Result;

            // Create new org (symbols) with both tables 
            (DataverseConnection dv2, EntityLookup el2) = CreateMemoryForRelationshipModels();  // numberIsFloat: false
            dv2.AddTable(displayName, logicalName);
            dv2.AddTable(displayName2, logicalName2);

            var result = eval(expr, dv2);
            Assert.AreEqual(expected is double exp ? new decimal(exp) : expected, result.ToObject());
        }

        // Run with 2 tables registered. 
        [DataTestMethod]
        [DataRow("First(t1).Other.Data", 200.0)]
        [DataRow("First(t2).Data", 200.0)]
        [DataRow("First(t1).Other.remoteid = First(t2).remoteid", true)] // same Id
        [DataRow("If(true, First(t1).Other, First(t2)).Data", 200.0)] // Compatible for comparison 
        public void ExecuteViaInterpreter2tablesFloat(string expr, object expected)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            var logicalName2 = "remote";
            var displayName2 = "t2";

            var engine = new RecalcEngine();
            engine.EnableDelegation();
            Func<string, DataverseConnection, FormulaValue> eval =
                (expr, dv) => engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues).Result;

            // Create new org (symbols) with both tables 
            (DataverseConnection dv2, EntityLookup el2) = CreateMemoryForRelationshipModels(numberIsFloat: true);
            dv2.AddTable(displayName, logicalName);
            dv2.AddTable(displayName2, logicalName2);

            var result = eval(expr, dv2);
            Assert.AreEqual(expected, result.ToObject());
        }

        // Run with 2 tables registered. 
        [DataTestMethod]
        [DataRow("First(t1).money", 123.0)]
        [DataRow("With({x:First(t1).money, y:First(t1)}, x + y.money)", 246.0)]
        [DataRow("With({x:Collect(t1,{money:40})}, x.money + First(t1).money)", 163.0)]
        [DataRow("Patch(t1, First(t1), {money:321});First(t1).money", 321.0)]
        public void ExtractPrimitiveValueTest(string expr, object expected)
        {
            // create table "local"
            var logicalName = "allattributes";
            var displayName = "t1";

            var engine = new RecalcEngine();
            engine.EnableDelegation();

            // Create new org (symbols) with both tables 
            (DataverseConnection dv, EntityLookup el) = CreateMemoryForAllAttributeModel();
            dv.AddTable(displayName, logicalName);

            engine.Config.SymbolTable.EnableMutationFunctions();

            var opts = _parserAllowSideEffects;
            var check = engine.Check(expr, symbolTable: dv.Symbols, options: opts);

            var run = check.GetEvaluator();
            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.AreEqual(expected, result.ToDouble());
        }

        [DataTestMethod]
        [DataRow(true, "Number")]
        [DataRow(false, "Decimal")]
        public void TestMoney(bool numberIsFloat, string retTypeStr)
        {
            // create table "local"
            var logicalName = "allattributes";
            var displayName = "t1";

            var engine = new RecalcEngine();
            engine.EnableDelegation();

            // numberIsFloat controls how metadata parser handles currency. 
            (DataverseConnection dv, EntityLookup el) = CreateMemoryForAllAttributeModel(metadataNumberIsFloat: numberIsFloat);
            dv.AddTable(displayName, logicalName);

            var expr = "First(t1).money"; // field of Currency type 

            var check = engine.Check(expr, symbolTable: dv.Symbols);
            Assert.IsTrue(check.IsSuccess);
            var retType = check.ReturnType.ToString();
            Assert.AreEqual(retTypeStr, retType);
        }

        [DataTestMethod]
        public void TestBigInt()
        {            
            var logicalName = "allattributes";
            var displayName = "t1";

            var engine = new RecalcEngine();
            engine.EnableDelegation();
            engine.Config.SymbolTable.EnableMutationFunctions();
            
            (DataverseConnection dv, EntityLookup el) = CreateMemoryForAllAttributeModel(metadataNumberIsFloat: false);
            dv.AddTable(displayName, logicalName);            

            var expr = "First(t1).BigInt";
            long lngRef = long.MaxValue;

            foreach (long lng in new[] { long.MinValue, -278175, 0, 17, long.MaxValue })
            {
                var check = engine.Check(expr, new ParserOptions() { AllowsSideEffects = true }, symbolTable: dv.Symbols);
                Assert.IsTrue(check.IsSuccess);
                var retType = check.ReturnType.ToString();
                Assert.AreEqual("Decimal", retType);

                FormulaValue fv = check.GetEvaluator().EvalAsync(CancellationToken.None, dv.SymbolValues).Result;
                Assert.AreEqual(lngRef, fv.ToDecimal());

                var expr2 = $"Patch(t1, First(t1), {{ BigInt: {lng} }}).BigInt";
                var check2 = engine.Check(expr2, new ParserOptions() { AllowsSideEffects = true }, symbolTable: dv.Symbols);
                Assert.IsTrue(check2.IsSuccess, string.Join(", ", check2.Errors.Select(er => er.Message)));

                FormulaValue fv2 = check2.GetEvaluator().EvalAsync(CancellationToken.None, dv.SymbolValues).Result;
                Assert.AreEqual(lng, fv2.ToDecimal());

                lngRef = lng;
            }
        }

        [DataTestMethod]
        [DataRow("Patch(t1, First(t1), {Memo:\"LOREM\nIPSUM\nDOLOR\nSIT\nAMET\"})")]
        [DataRow("First(t1).Memo")]
        public void SupportAllColumnTypesTest(string expr)
        {
            // create table "local"
            var logicalName = "allattributes";
            var displayName = "t1";

            var engine = new RecalcEngine();
            engine.EnableDelegation();

            // Create new org (symbols) with both tables 
            (DataverseConnection dv, EntityLookup el) = CreateMemoryForAllAttributeModel();
            dv.AddTable(displayName, logicalName);

            engine.Config.SymbolTable.EnableMutationFunctions();

            var opts = _parserAllowSideEffects;
            var check = engine.Check(expr, symbolTable: dv.Symbols, options: opts);

            var run = check.GetEvaluator();
            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.IsNotInstanceOfType(result, typeof(ErrorValue));
        }

        [DataTestMethod]
        [DataRow("If(First(t1).boolean, \"YES\", \"NO\")", "YES")]
        [DataRow("If(First(t1).boolean = allattributes_boolean_optionSet.'1', \"YES\", \"NO\")", "YES")]
        [DataRow("If(First(t1).boolean = 'Boolean (All Attributes)'.'1', \"YES\", \"NO\")", "YES")]
        [DataRow("Text(First(t1).boolean)", "Yes")]
        [DataRow("Text(First(t1).boolean) & \"Maybe\"", "YesMaybe")]
        [DataRow("Patch(t1, First(t1), {boolean:allattributes_boolean_optionSet.'0',email:\"dummy@email.com\"});First(t1).email", "dummy@email.com")]
        [DataRow("With({before: First(t1).boolean}, Patch(t1, First(t1), {boolean:'Boolean (All Attributes)'.'0'});If(First(t1).boolean <> before, \"good\", \"bad\"))", "good")]
        [DataRow("Collect(t1, {boolean:allattributes_boolean_optionSet.'1',email:\"dummy1@email.com\"});LookUp(t1, email = \"dummy1@email.com\").email", "dummy1@email.com")]
        [DataRow("Collect(t1, {boolean:allattributes_boolean_optionSet.'1',email:\"dummy2@email.com\"});If(LookUp(t1, email = \"dummy2@email.com\").boolean, \"Affirmitive\", \"Nope\")", "Affirmitive")]
        public void BooleanOptionSetCoercionTest(string expr, string expected)
        {
            // create table "local"
            var logicalName = "allattributes";
            var displayName = "t1";

            var engine = new RecalcEngine();
            engine.EnableDelegation();

            // Create new org (symbols) with both tables 
            (DataverseConnection dv, EntityLookup el) = CreateMemoryForAllAttributeModel();
            dv.AddTable(displayName, logicalName);

            engine.Config.SymbolTable.EnableMutationFunctions();

            var opts = _parserAllowSideEffects;
            var check = engine.Check(expr, symbolTable: dv.Symbols, options: opts);

            var run = check.GetEvaluator();
            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.AreEqual(expected, result.ToObject());
        }

        // https://github.com/microsoft/Power-Fx-Dataverse/issues/102
        [DataTestMethod]
        [DataRow("Collect(t1, {Price:111, Other: LookUp(Remote, RemoteId = GUID(\"00000000-0000-0000-0000-000000000002\"))});Text(Last(t1).Price)", "111")]
        [DataRow("With({remote: LookUp(Remote, RemoteId = GUID(\"00000000-0000-0000-0000-000000000002\"))}, Collect(t1, {Price:111, Other: remote});Text(Last(t1).Price))", "111")]
        [DataRow("Patch(t1, LookUp(t1, LocalId = GUID(\"00000000-0000-0000-0000-000000000001\")), {Other: LookUp(Remote, RemoteId = GUID(\"00000000-0000-0000-0000-000000000002\")), Price: 222});Text(LookUp(t1, LocalId = GUID(\"00000000-0000-0000-0000-000000000001\")).Price)", "222")]
        [DataRow("With({local: LookUp(t1, LocalId = GUID(\"00000000-0000-0000-0000-000000000001\")), remote: LookUp(Remote, RemoteId = GUID(\"00000000-0000-0000-0000-000000000002\"))}, Patch(t1, local, {Other: remote, Price: 222});Text(LookUp(t1, LocalId = GUID(\"00000000-0000-0000-0000-000000000001\")).Price))", "222")]
        public void CardsRegressionRelationshipModelsTest(string expr, string expected)
        {
            var engine = new RecalcEngine();
            engine.EnableDelegation();

            // Create new org (symbols) with both tables 
            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable("t1", "local");
            dv.AddTable("Remote", "remote");

            engine.Config.SymbolTable.EnableMutationFunctions();

            var opts = _parserAllowSideEffects;
            var check = engine.Check(expr, symbolTable: dv.Symbols, options: opts);

            var run = check.GetEvaluator();
            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.AreEqual(expected, result.ToObject());
        }

        // Hyperlink types are imported as String
        [DataTestMethod]
        [DataRow("First(t1).hyperlink", "Hyperlink column type not supported.")]
        [DataRow("With({x:First(t1)}, x.hyperlink)", "Hyperlink column type not supported.")]
        public void HyperlinkIsString(string expr, string expected)
        {
            // create table "local"
            var logicalName = "allattributes";
            var displayName = "t1";

            var engine = new RecalcEngine();
            engine.EnableDelegation();

            // Create new org (symbols) with both tables 
            (DataverseConnection dv, EntityLookup el) = CreateMemoryForAllAttributeModel();
            dv.AddTable(displayName, logicalName);

            engine.Config.SymbolTable.EnableMutationFunctions();

            var opts = _parserAllowSideEffects;
            var check = engine.Check(expr, symbolTable: dv.Symbols, options: opts);

            var run = check.GetEvaluator();
            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.IsInstanceOfType(result, typeof(StringValue));
            Assert.AreEqual(FormulaType.String, result.Type);

            Assert.AreEqual("teste_url", result.ToObject());
        }

        // Ensure a custom function shows up in intellisense. 
        [TestMethod]
        public void IntellisenseWithWholeOrgPolicy()
        {
            // Everything policy 
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "xxxt1"); // unique display name
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv2, EntityLookup el2) = CreateMemoryForRelationshipModels(policy);

            var engine = new RecalcEngine();
            engine.EnableDelegation();

            // Intellisense doesn't return anything on pure empty, 
            // needs at least first char of identifier. 

            var check = engine.Check("xx", symbolTable: dv2.Symbols);

            var results = engine.Suggest(check, 2);
            var list = results.Suggestions.ToArray();

            Assert.AreEqual(1, list.Length);
            Assert.AreEqual("xxxt1", list[0].DisplayText.Text);

            // Triggers a lazy load
            var check2 = engine.Check("First(xxxt1)", symbolTable: dv2.Symbols);
            Assert.IsTrue(check2.IsSuccess);

            // After lazily load, now the symbol table is populated and we see the symbols. 
            var results2 = engine.Suggest(check, 2);
            var list2 = results2.Suggestions.ToArray();

            Assert.AreEqual(1, list2.Length);
            Assert.AreEqual("xxxt1", list2[0].DisplayText.Text);
        }

        [TestMethod]
        public void SingleOrgPolicyTest()
        {
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "Remote");
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels(policy);

            foreach (var name in new string[] { "local", "t1", "remote", "Remote" })
            {
                var ok = dv.Symbols.TryLookupSlot(name, out var s1);
                Assert.IsTrue(ok);
                Assert.IsNotNull(s1);
            }
        }

        // Ensure lazy loaded symbols are available on first use. 
        [TestMethod]
        public void SingleOrgPolicyLazyEval()
        {
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "Remote");
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels(policy);

            var engine = new RecalcEngine();
                        
            // Ensure first call gets correct answer. 
            var result = engine.EvalAsync("CountRows(local)", default, dv.SymbolValues).Result;
            Assert.AreEqual(3.0, result.ToDouble());

            // 2nd call better be correct. 
            var result2 = engine.EvalAsync("CountRows(local)", default, dv.SymbolValues).Result;
            Assert.AreEqual(3.0, result2.ToDouble());
        }

        // When using WholeOrg policy, we're using display names,
        // which are converted to invariant.
        [DataTestMethod]
        [DataRow("new_price + 10", "Price + 10")]
        [DataRow("ThisRecord.new_price + 10", "ThisRecord.Price + 10")]
        [DataRow("First(local).new_price", "First(t1).Price")]
        [DataRow("ThisRecord.refg.data", "ThisRecord.Other.Data")] // relationships
        [DataRow("First(remote).data", "First(Remote).Data")]
        [DataRow("Set(refg, First(remote));refg.data", "Set(Other, First(Remote));Other.Data")] // relationships
        [DataRow("new_price * new_quantity", "Price * Quantity", "new_price * Quantity")]
        public void WholeOrgConversions(string logical, string display, string mixed = null)
        {
            var logicalName = "local";

            // Everything policy 
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "Remote");
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels(policy);

            {
                var rowScopeSymbols = dv.GetRowScopeSymbols(tableLogicalName: logicalName);
                var symbols = ReadOnlySymbolTable.Compose(rowScopeSymbols, dv.Symbols);

                var config = new PowerFxConfig();
                config.EnableSetFunction();
                var engine = new RecalcEngine(config);
                engine.EnableDelegation();

                // Check conversion against all forms. 
                foreach (var expr in new string[] { logical, display, mixed })
                {
                    if (expr == null)
                    {
                        continue;
                    }

                    // Get invariant
                    var check = new CheckResult(engine)
                        .SetText(expr, _parserAllowSideEffects)
                        .SetBindingInfo(symbols);

                    check.ApplyBinding();
                    Assert.IsTrue(check.IsSuccess);

                    var invariant = check.ApplyGetInvariant();
                    Assert.AreEqual(invariant, logical);

                    // Get display 
                    var display2 = engine.GetDisplayExpression(expr, symbols);
                    Assert.AreEqual(display, display2);
                }
            }
        }

        [DataRow("1+2", "")] // none
        [DataRow("ThisRecord.Price * Quantity", "Read local: new_price, new_quantity;")] // basic read
        [DataRow("Price%", "Read local: new_price;")] // unary op
        [DataRow("ThisRecord", "Read local: ;")] // whole scope 
        [DataRow("First(Remote).Data", "Read remote: data;")] // other table
        [DataRow("Set(Price, 200)", "Write local: new_price;")] // set, 
        [DataRow("Set(Price, Quantity)", "Read local: new_quantity; Write local: new_price;")] // set, 
        [DataRow("Set(Price, Price + 1)", "Read local: new_price; Write local: new_price;")] // set, 
        [DataRow("ThisRecord.Other.Data", "Read local: otherid; Read remote: data;")] //relationship
        [DataRow("{x:5}.x", "")] // non dataverse record
        [DataRow("With({x : ThisRecord}, x.Price)", "Read local: new_price;")] // alias
        [DataRow("With({Price : 5}, Price + Quantity)", "Read local: new_quantity;")] // Price is shadowed
        [DataRow("With({Price : 5}, ThisRecord.Price)", "")] // shadowed
        [DataRow("LookUp(t1,Price=255)", "Read local: new_price;")] // Lookup and RowScope
        [DataRow("Filter(t1,Price > 200)", "Read local: new_price;")] // Lookup and RowScope
        [DataRow("First(t1)", "Read local: ;")]
        [DataRow("Last(t1)", "Read local: ;")]
        [DataRow("t1", "Read local: ;")] // whole table
        [DataRow("12 & true & \"abc\" ", "")] // walker ignores literals
        [DataRow("12;Price;12", "Read local: new_price;")] // chaining
        [DataRow("ParamLocal1.Price", "Read local: new_price;")] // basic read
        [DataRow("First(t1).Price + First(Remote).'Other Other'.'Data Two'", "Read local: new_price; Read remote: otherotherid; Read doubleremote: data2;")] // 3 entities
        [DataRow("Patch(t1, First(t1), { Price : 200})", "Read local: ; Write local: new_price;")] // Patch, arg1 reads
        [DataRow("Collect(t1, { Price : 200})", "Write local: new_price;")] // collect , does not write to t1. 
        [DataRow("Collect(t1,{ Other : First(Remote)})", "Read remote: ; Write local: otherid;")]
        public void GetDependencies(string expr, string expected)
        {
            var logicalName = "local";

            // Everything policy 
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "Remote");
            map.Add("doubleremote", "Remote2");
            var policy = new SingleOrgPolicy(map);

            var config = new PowerFxConfig();
            config.SymbolTable.EnableMutationFunctions();
            var engine = new RecalcEngine(config);
            engine.EnableDelegation();

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels(policy);

            // Simulate a parameter
            var parameterSymbols = new SymbolTable { DebugName = "Parameters " };
            parameterSymbols.AddVariable("ParamLocal1", dv.GetRecordType("local"), mutable: true);

            var rowScopeSymbols = dv.GetRowScopeSymbols(tableLogicalName: logicalName);
            var symbols = ReadOnlySymbolTable.Compose(rowScopeSymbols, dv.Symbols, parameterSymbols);

            var check = new CheckResult(engine)
                .SetText(expr, _parserAllowSideEffects)
                .SetBindingInfo(symbols);

            var info = DependencyInfo.Scan(check, dv.MetadataCache);

            var actual = info.ToString().Replace("\r", "").Replace("\n", "").Trim();

            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void RefreshDataverseConnectionSingleOrgPolicyTest()
        {
            var logicalName = "local";

            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "Remote");

            var policy = new SingleOrgPolicy(map);

            var expr = "Sum(t1,Price)";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels(policy);  // numberIsFloat: false

            var rowScopeSymbols = dv.GetRowScopeSymbols(tableLogicalName: logicalName);
            var symbols = ReadOnlySymbolTable.Compose(rowScopeSymbols, dv.Symbols);

            var opts = _parserAllowSideEffects;
            var config = new PowerFxConfig();
            config.SymbolTable.EnableMutationFunctions();

            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation();

            var check1 = engine1.Check(expr, options: opts, symbolTable: symbols);
            Assert.IsTrue(check1.IsSuccess);

            var run1 = check1.GetEvaluator();
            var result1 = run1.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.AreEqual(100m, result1.ToObject());

            // Simulates a row being deleted by an external user
            el.DeleteAsync(logicalName, _g1);
            el.DeleteAsync(logicalName, _g3);
            el.DeleteAsync(logicalName, _g4);

            // Evals the same expression by a new engine. Should return a wrong result.
            var engine2 = new RecalcEngine(config);
            engine2.EnableDelegation();
            var check2 = engine2.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check2.IsSuccess);

            var run2 = check2.GetEvaluator();
            var result2 = run2.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.AreNotEqual(0, result2.ToObject());

            // Refresh connection cache.
            dv.RefreshCache();

            // Evals the same expression by a new engine. Sum should now return the refreshed value.
            var engine3 = new RecalcEngine(config);
            engine3.EnableDelegation();
            var check3 = engine3.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check3.IsSuccess);

            var run3 = check3.GetEvaluator();
            var result3 = run3.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.IsInstanceOfType(result3, typeof(BlankValue));
        }

        [TestMethod]
        public void RefreshDataverseConnectionSingleOrgPolicyTestFloat()
        {
            var logicalName = "local";

            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "Remote");

            var policy = new SingleOrgPolicy(map);

            var expr = "Sum(t1,Price)";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels(policy, numberIsFloat: true);

            var rowScopeSymbols = dv.GetRowScopeSymbols(tableLogicalName: logicalName);
            var symbols = ReadOnlySymbolTable.Compose(rowScopeSymbols, dv.Symbols);

            var opts = _parserAllowSideEffects;
            var config = new PowerFxConfig();
            config.SymbolTable.EnableMutationFunctions();

            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation();

            var check1 = engine1.Check(expr, options: opts, symbolTable: symbols);
            Assert.IsTrue(check1.IsSuccess);

            var run1 = check1.GetEvaluator();
            var result1 = run1.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.AreEqual(100.0, result1.ToObject());

            // Simulates a row being deleted by an external user
            el.DeleteAsync(logicalName, _g1);
            el.DeleteAsync(logicalName, _g3);
            el.DeleteAsync(logicalName, _g4);

            // Evals the same expression by a new engine. Should return a wrong result.
            var engine2 = new RecalcEngine(config);
            engine2.EnableDelegation();
            var check2 = engine2.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check2.IsSuccess);

            var run2 = check2.GetEvaluator();
            var result2 = run2.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.AreNotEqual(0, result2.ToObject());

            // Refresh connection cache.
            dv.RefreshCache();

            // Evals the same expression by a new engine. Sum should now return the refreshed value.
            var engine3 = new RecalcEngine(config);
            engine3.EnableDelegation();
            var check3 = engine3.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check3.IsSuccess);

            var run3 = check3.GetEvaluator();
            var result3 = run3.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.IsInstanceOfType(result3, typeof(BlankValue));
        }

        [TestMethod]
        public void RefreshDataverseConnectionMultiOrgPolicyTest()
        {
            var logicalName = "local";
            var displayName = "t1";

            var expr = "Sum(t1,Price)";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();  // numberIsFloat: false
            dv.AddTable(displayName, logicalName);

            var opts = _parserAllowSideEffects;
            var config = new PowerFxConfig();
            config.SymbolTable.EnableMutationFunctions();

            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation();
            var check1 = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check1.IsSuccess);

            var run1 = check1.GetEvaluator();
            var result1 = run1.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.AreEqual(100m, result1.ToObject());

            // Simulates a row being deleted by an external force
            el.DeleteAsync(logicalName, _g1);
            el.DeleteAsync(logicalName, _g3);
            el.DeleteAsync(logicalName, _g4);

            // Evals the same expression by a new engine. Should return a wrong result.
            var engine2 = new RecalcEngine(config);
            engine2.EnableDelegation();
            var check2 = engine2.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check2.IsSuccess);

            var run2 = check2.GetEvaluator();
            var result2 = run2.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.AreNotEqual(0, result2.ToObject());

            // Refresh connection cache.
            dv.RefreshCache();

            // Evals the same expression by a new engine. Sum should now return the refreshed value.
            var engine3 = new RecalcEngine(config);
            engine3.EnableDelegation();
            var check3 = engine3.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check3.IsSuccess);

            var run3 = check3.GetEvaluator();
            var result3 = run3.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.IsInstanceOfType(result3, typeof(BlankValue));
        }

        [TestMethod]
        public void RefreshDataverseConnectionMultiOrgPolicyTestFloat()
        {
            var logicalName = "local";
            var displayName = "t1";

            var expr = "Sum(t1,Price)";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels(numberIsFloat: true);
            dv.AddTable(displayName, logicalName);

            var opts = _parserAllowSideEffects;
            var config = new PowerFxConfig();
            config.SymbolTable.EnableMutationFunctions();

            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation();
            var check1 = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check1.IsSuccess);

            var run1 = check1.GetEvaluator();
            var result1 = run1.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.AreEqual(100.0, result1.ToObject());

            // Simulates a row being deleted by an external force
            el.DeleteAsync(logicalName, _g1);
            el.DeleteAsync(logicalName, _g3);
            el.DeleteAsync(logicalName, _g4);

            // Evals the same expression by a new engine. Should return a wrong result.
            var engine2 = new RecalcEngine(config);
            engine2.EnableDelegation();
            var check2 = engine2.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check2.IsSuccess);

            var run2 = check2.GetEvaluator();
            var result2 = run2.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.AreNotEqual(0, result2.ToObject());

            // Refresh connection cache.
            dv.RefreshCache();

            // Evals the same expression by a new engine. Sum should now return the refreshed value.
            var engine3 = new RecalcEngine(config);
            engine3.EnableDelegation();
            var check3 = engine3.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check3.IsSuccess);

            var run3 = check3.GetEvaluator();
            var result3 = run3.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.IsInstanceOfType(result3, typeof(BlankValue));
        }

        // Dependency finder. 
        [DataTestMethod]
        [DataRow("1+2", "")] // none
        [DataRow("First(t1).Price", "local")]
        [DataRow("First(Remote)", "remote")]
        [DataRow("First(t1).Price & IsBlank(First(Remote))", "local,remote")]
        public void TableDependencyFinder(string expression, string listTables)
        {
            var logicalName = "local";

            // Everything policy 
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "Remote");
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels(policy);

            {
                var rowScopeSymbols = dv.GetRowScopeSymbols(tableLogicalName: logicalName);
                var symbols = ReadOnlySymbolTable.Compose(rowScopeSymbols, dv.Symbols);

                var config = new PowerFxConfig();
                config.EnableSetFunction();
                var engine = new RecalcEngine(config);
                engine.EnableDelegation();

                var check = new CheckResult(engine)
                    .SetText(expression)
                    .SetBindingInfo(symbols);

                var list = policy.GetDependencies(check).ToArray();
                Array.Sort(list);
                var x = string.Join(",", list);

                Assert.AreEqual(listTables, x);
            }
        }


        // Enumerate various setups to run tests in. 
        private IEnumerable<(DataverseConnection dv, EntityLookup el)> Setups()
        {
            // Explicit policy 
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            yield return (dv, el);

            // Everything policy 
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "t2");
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv2, EntityLookup el2) = CreateMemoryForRelationshipModels(policy);

            yield return (dv2, el2);
        }

        [DataTestMethod]

        // Row Scope
        [DataRow("new_price + 10", 110.0)] // Basic field lookup (RowScope) w/ logical names
        [DataRow("new_price + new_quantity", 100.0)] // new_quantity is blank. 

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

            foreach ((DataverseConnection dv, EntityLookup el) in Setups())
            {
                var rowScopeSymbols = rowScope ? dv.GetRowScopeSymbols(tableLogicalName: logicalName) : null;
                var symbols = ReadOnlySymbolTable.Compose(rowScopeSymbols, dv.Symbols);

                var engine1 = new RecalcEngine();
                engine1.EnableDelegation();
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

                Assert.AreEqual(expected, result.ToDoubleOrObject());
            }
        }

        // Set() function against entity fields in RowScope
        [DataTestMethod]
        [DataRow("Set(Price, 200); Price", 200.0)]
        public void LocalSet(string expr, object expected)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();  // numberIsFloat: false
            dv.AddTable(displayName, logicalName);
            dv.AddTable("Remote", "remote");


            var rowScopeSymbols = dv.GetRowScopeSymbols(tableLogicalName: logicalName);

            var opts = _parserAllowSideEffects;
            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation();

            var allSymbols = ReadOnlySymbolTable.Compose(rowScopeSymbols, dv.Symbols);
            var check = engine1.Check(expr, options: opts, symbolTable: allSymbols);
            Assert.IsTrue(check.IsSuccess, string.Join("\r\n", check.Errors.Select(ee => ee.Message)));

            var run = check.GetEvaluator();

            var entity = el.GetFirstEntity(logicalName, dv, CancellationToken.None); // any record
            var record = dv.Marshal(entity);
            var rowScopeValues = ReadOnlySymbolValues.NewFromRecord(rowScopeSymbols, record);
            var allValues = allSymbols.CreateValues(rowScopeValues, dv.SymbolValues);

            var result = run.EvalAsync(CancellationToken.None, allValues).Result;

            Assert.AreEqual(new Decimal((double)expected), result.ToObject());

            // Extra validation that recordValue is updated .
            if (expr.StartsWith("Set(Price, 200)"))
            {
                Assert.AreEqual(200m, record.GetField("new_price").ToObject());

                Assert.AreEqual(new Decimal(200.0), entity.Attributes["new_price"]);

                // verify on entity 
                var e2 = el.LookupRef(entity.ToEntityReference(), CancellationToken.None);
                Assert.AreEqual(new Decimal(200.0), e2.Attributes["new_price"]);
            }
        }

        // Set() function against entity fields in RowScope
        [DataTestMethod]
        [DataRow("Set(Price, 200); Price", 200.0)]
        public void LocalSetFloat(string expr, object expected)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels(numberIsFloat: true);
            dv.AddTable(displayName, logicalName);
            dv.AddTable("Remote", "remote");


            var rowScopeSymbols = dv.GetRowScopeSymbols(tableLogicalName: logicalName);

            var opts = _parserAllowSideEffects_NumberIsFloat;
            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation();

            var allSymbols = ReadOnlySymbolTable.Compose(rowScopeSymbols, dv.Symbols);
            var check = engine1.Check(expr, options: opts, symbolTable: allSymbols);
            Assert.IsTrue(check.IsSuccess);

            var run = check.GetEvaluator();

            var entity = el.GetFirstEntity(logicalName, dv, CancellationToken.None); // any record
            var record = dv.Marshal(entity);
            var rowScopeValues = ReadOnlySymbolValues.NewFromRecord(rowScopeSymbols, record);
            var allValues = allSymbols.CreateValues(rowScopeValues, dv.SymbolValues);

            var result = run.EvalAsync(CancellationToken.None, allValues).Result;

            Assert.AreEqual(expected, result.ToObject());

            // Extra validation that recordValue is updated .
            if (expr.StartsWith("Set(Price, 200)"))
            {
                Assert.AreEqual(200.0, record.GetField("new_price").ToObject());

                Assert.AreEqual(new Decimal(200.0), entity.Attributes["new_price"]);

                // verify on entity 
                var e2 = el.LookupRef(entity.ToEntityReference(), CancellationToken.None);
                Assert.AreEqual(new Decimal(200.0), e2.Attributes["new_price"]);
            }
        }

        // Patch() function against entity fields in RowScope
        // Decimal is not allowed as a value in DataRow, cast to Decimal during test
        [DataTestMethod]
        [DataRow("Patch(t1, First(t1), { Price : 200}); First(t1).Price", 200.0)]
        [DataRow("With( { x : First(t1)}, Patch(t1, x, { Price : 200}); x.Price)", 100.0)] // Expected, x.Price is still old value!
        [DataRow("Patch(t1, First(t1), { Price : 200}).Price", 200.0)]
        [DataRow("Collect(t1, { Price : 200}).Price", 200.0)]
        [DataRow("With( {oldCount : CountRows(t1)}, Collect(t1, { Price : 200});CountRows(t1)-oldCount)", 1.0)]
        [DataRow("Collect(t1, { Price : 255}); LookUp(t1,Price=255).Price", 255.0)]
        [DataRow("Patch(t1, First(t1), { Price : Blank()}); First(t1).Price", null)] // Set to blank will clear it out
        [DataRow("Patch(t1, {localid:GUID(\"00000000-0000-0000-0000-000000000001\")}, { Price : 200}).Price", 200.0)]

        public void PatchFunctionFloat(string expr, double? expected)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            foreach (var numberIsFloat in new bool[] {false, true})
            {
                (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels(numberIsFloat: numberIsFloat);
                dv.AddTable(displayName, logicalName);

                var opts = new ParserOptions
                {
                    AllowsSideEffects = true,
                    NumberIsFloat = numberIsFloat
                };

                var config = new PowerFxConfig(); // Pass in per engine
                config.SymbolTable.EnableMutationFunctions();
                var engine1 = new RecalcEngine(config);
                engine1.EnableDelegation();

                var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
                Assert.IsTrue(check.IsSuccess);

                var run = check.GetEvaluator();

                var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

                if (numberIsFloat)
                {
                    Assert.AreEqual(expected, result.ToObject());
                }
                else
                {
                    Assert.AreEqual(expected is not null ? new Decimal((double)expected) : expected, result.ToObject());
                }               

                // verify on entity - this should always be updated 
                if (expr.Contains("Patch("))
                {
                    var r2 = engine1.EvalAsync("First(t1)", CancellationToken.None, runtimeConfig: dv.SymbolValues).Result;
                    var entity = (Entity)r2.ToObject();
                    var e2 = el.LookupRef(entity.ToEntityReference(), CancellationToken.None);
                    var actualValue = e2.Attributes["new_price"];
                    if (expected.HasValue)
                    {
                        Assert.AreEqual(new Decimal(200.0), actualValue);
                    }
                    else
                    {
                        Assert.IsNull(actualValue);
                    }
                }
            }
        }

        // Table 't1' has
        // 1st item with
        // Price = 100, Old_Price = 200,  Date = Date(2023, 6, 1), DateTime = DateTime(2023, 6, 1, 12, 0, 0)
        // 2nd item with
        // Price = 10
        // 3rd item with
        // Price = -10

        [DataTestMethod]

        //Basic case
        [DataRow("LookUp(t1, Price = 255).Price",
            null,
            "(__retrieveSingle(t1, __eq(t1, new_price, 255))).new_price")]

        [DataRow("LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\")).Price",
            100.0,
            "(__retrieveGUID(t1, GUID(00000000-0000-0000-0000-000000000001))).new_price")]

        //Basic case with And and Or
        [DataRow("LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\") And Price > 0).Price",
            100.0,
            "(__retrieveSingle(t1, __and(__eq(t1, localid, GUID(00000000-0000-0000-0000-000000000001)), __gt(t1, new_price, 0)))).new_price")]

        [DataRow("LookUp(t1, LocalId=GUID(\"00000000-0000-0000-0000-000000000001\") Or Price > 0).Price",
            100.0,
            "(__retrieveSingle(t1, __or(__eq(t1, localid, GUID(00000000-0000-0000-0000-000000000001)), __gt(t1, new_price, 0)))).new_price")]

        // variable
        [DataRow("LookUp(t1, LocalId=_g1).Price",
            100.0,
            "(__retrieveGUID(t1, _g1)).new_price")]

        // These three tests have Coalesce for numeric literals where the NumberIsFloat version does not.
        // Although not wrong, they should be the same.  Being tracked with https://github.com/microsoft/Power-Fx/issues/1609
        // Date
        [DataRow("LookUp(t1, Date = Date(2023, 6, 1)).Price",
            100.0,
            "(__retrieveSingle(t1, __eq(t1, new_date, Date(Coalesce(Float(2023), 0), Coalesce(Float(6), 0), Coalesce(Float(1), 0))))).new_price")]

        // DateTime with coercion
        [DataRow("LookUp(t1, DateTime = Date(2023, 6, 1)).Price",
             null,
            "(__retrieveSingle(t1, __eq(t1, new_datetime, DateToDateTime(Date(Coalesce(Float(2023), 0), Coalesce(Float(6), 0), Coalesce(Float(1), 0)))))).new_price")]

        [DataRow("LookUp(t1, DateTime = DateTime(2023, 6, 1, 12, 0, 0)).Price",
             100.0,
            "(__retrieveSingle(t1, __eq(t1, new_datetime, DateTime(Coalesce(Float(2023), 0), Coalesce(Float(6), 0), Coalesce(Float(1), 0), Coalesce(Float(12), 0), Coalesce(Float(0), 0), Coalesce(Float(0), 0))))).new_price")]


        // reversed order still ok 
        [DataRow("LookUp(t1, _g1 = LocalId).Price",
            100.0,
            "(__retrieveGUID(t1, _g1)).new_price")]

        // explicit ThisRecord is ok. IR will handle. 
        [DataRow("LookUp(t1, ThisRecord.LocalId=_g1).Price",
            100.0,
            "(__retrieveGUID(t1, _g1)).new_price")]

        // Alias is ok. IR will handle. 
        [DataRow("LookUp(t1 As XYZ, XYZ.LocalId=_g1).Price",
            100.0,
            "(__retrieveGUID(t1, _g1)).new_price")] // variable

        // lambda uses ThisRecord.Price, can't delegate
        [DataRow("LookUp(t1, LocalId=If(Price>50, _g1, _gMissing)).Price",
            100.0,
            "(LookUp(t1, (EqGuid(localid,If(GtDecimals(new_price,50), (_g1), (_gMissing)))))).new_price",
            "Warning 22-27: Can't delegate LookUp: Expression compares multiple fields.")]

        // On non primary field.
        [DataRow("LookUp(t1, Price > 50).Price",
            100.0,
            "(__retrieveSingle(t1, __gt(t1, new_price, 50))).new_price")]

        // successful with complex expression
        [DataRow("LookUp(t1, LocalId=If(true, _g1, _gMissing)).Price",
            100.0,
            "(__retrieveGUID(t1, If(True, (_g1), (_gMissing)))).new_price")]

        // nested delegation, both delegated.
        [DataRow("LookUp(t1, LocalId=LookUp(t1, LocalId=_g1).LocalId).Price",
            100.0,
            "(__retrieveGUID(t1, (__retrieveGUID(t1, _g1)).localid)).new_price"
            )]

        // Can't delegate if Table Arg is delegated.
        [DataRow("LookUp(Filter(t1, Price = 100), localid=_g1).Price",
            100.0,
            "(LookUp(__retrieveMultiple(t1, __eq(t1, new_price, 100), 999), (EqGuid(localid,_g1)))).new_price",
            "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows."
        )]

        // Can't delegate if Table Arg is delegated.
        [DataRow("LookUp(FirstN(t1, 1), localid=_g1).Price",
            100.0,
            "(LookUp(__retrieveMultiple(t1, __noFilter(), Float(1)), (EqGuid(localid,_g1)))).new_price",
            "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")] // $$$ span

        // Can Delegate on non primary-key field.
        [DataRow("LookUp(t1, LocalId=LookUp(t1, ThisRecord.Price>50).LocalId).Price",
            100.0,
            "(__retrieveGUID(t1, (__retrieveSingle(t1, __gt(t1, new_price, 50))).localid)).new_price")]

        [DataRow("LookUp(t1, LocalId=First([_g1,_gMissing]).Value).Price",
            100.0,
            "(__retrieveGUID(t1, (First(Table({Value:_g1}, {Value:_gMissing}))).Value)).new_price")]

        // unsupported function, can't yet delegate
        [DataRow("Last(t1).Price",
            -10.0,
            "(Last(t1)).new_price",
            "Warning 5-7: This operation on table 'local' may not work if it has more than 999 rows."
            )]

        // unsupported function, can't yet delegate
        [DataRow("CountRows(t1)",
            3.0,
            "CountRows(t1)",
            "Warning 10-12: This operation on table 'local' may not work if it has more than 999 rows."
            )]

        // Functions like IsBlank, Collect,Patch, shouldn't require delegation. Ensure no warnings. 
        [DataRow("IsBlank(t1)",
            false, // nothing to delegate
            "IsBlank(t1)"
            )]

        [DataRow("IsBlank(Filter(t1, 1=1))",
            false, // nothing to delegate
            "IsBlank(Filter(t1, (EqDecimals(1,1))))",
            "Warning 15-17: This operation on table 'local' may not work if it has more than 999 rows."
            )]


        [DataRow("Collect(t1, { Price : 200}).Price",
            200.0, // Collect shouldn't give warnings. 
            "(Collect((t1), {new_price:200})).new_price"
            )]

        // Aliasing prevents delegation. 
        [DataRow("With({r : t1}, LookUp(r, LocalId=_g1).Price)",
            100.0,
            "With({r:t1}, ((LookUp(r, (EqGuid(localid,_g1)))).new_price))",
            "Warning 10-12: This operation on table 'local' may not work if it has more than 999 rows.")]

        // $$$ Confirm is NotFound Error or Blank? 
        [DataRow("IsError(LookUp(t1, LocalId=If(false, _g1, _gMissing)))",
            true, // delegated, but not found is Error
            "IsError(__retrieveGUID(t1, If(False, (_g1), (_gMissing))))")]

        // $$$ Does using fakeT1, same as t1, cause warnings since it's not delegated?
        [DataRow("LookUp(fakeT1, LocalId=_g1).Price",
            100.0,
            "(LookUp(fakeT1, (EqGuid(localid,_g1)))).new_price")] // variable

        [DataRow("With( { f : _g1}, LookUp(t1, LocalId=f)).Price",
            100.0,
            "(With({f:_g1}, (__retrieveGUID(t1, f)))).new_price")] // variable

        [DataRow("LookUp(t1, LocalId=LocalId).Price",
            100.0,
            "(LookUp(t1, (EqGuid(localid,localid)))).new_price",
            "Warning 18-19: This predicate will always be true. Did you mean to use ThisRecord or [@ ]?",
            "Warning 19-26: Can't delegate LookUp: Expression compares multiple fields.")] // variable

        // Error Handling
        [DataRow("LookUp(t1, Price = If(1/0, 255)).Price",
            typeof(ErrorValue),
            "(__retrieveSingle(t1, __eq(t1, new_price, If(DecimalToBoolean(DivDecimals(1,0)), (255))))).new_price")]

        // Blank Handling
        [DataRow("LookUp(t1, Price = Blank()).Price",
            null,
            "(__retrieveSingle(t1, __eq(t1, new_price, Blank()))).new_price")]

        [DataRow("LookUp(t1, Price <> Blank()).Price",
            100.0,
            "(__retrieveSingle(t1, __neq(t1, new_price, Blank()))).new_price")]

        [DataRow("LookUp(t1, Price < Blank()).Price",
            -10.0,
            "(__retrieveSingle(t1, __lt(t1, new_price, Blank()))).new_price")]

        [DataRow("LookUp(t1, Currency > 0).Price",
            100.0,
            "(LookUp(t1, (GtDecimals(new_currency,0)))).new_price",
            "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        public void LookUpDelegation(string expr, object expected, string expectedIr, params string[] expectedWarnings)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";
                        
            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();  // numberIsFloat: false
            var tableT1 = dv.AddTable(displayName, logicalName);

            var opts = _parserAllowSideEffects;
            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_g1", FormulaValue.New(_g1)); // matches entity
            engine1.UpdateVariable("_gMissing", FormulaValue.New(Guid.Parse("00000000-0000-0000-9999-000000000001"))); // no match

            // Add a variable with same table type.
            // But it's not in the same symbol table, so we can't delegate this. 
            // Previously this was UpdateVariable, but UpdateVariable no longer supports dataverse tables (by design).
            var fakeSymbolTable = new SymbolTable();
            var fakeSlot = fakeSymbolTable.AddVariable("fakeT1", tableT1.Type);
            var allSymbols = ReadOnlySymbolTable.Compose(fakeSymbolTable, dv.Symbols);

            var check = engine1.Check(expr, options: opts, symbolTable: allSymbols);
            Assert.IsTrue(check.IsSuccess);

            // comapre IR to verify the delegations are happening exactly where we expect 
            var irNode = check.ApplyIR();
            var actualIr = check.GetCompactIRString();
            Assert.AreEqual(expectedIr, actualIr);

            // Validate delegation warnings.
            // error.ToString() will capture warning status, message, and source span. 
            var errors = check.ApplyErrors();

            var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

            Assert.AreEqual(expectedWarnings.Length, errorList.Length);
            for (int i = 0; i < errorList.Length; i++)
            {
                Assert.AreEqual(expectedWarnings[i], errorList[i]);
            }

            // Can still run and verify results. 
            var run = check.GetEvaluator();

            // Place a reference to tableT1 in the fakeT1 symbol values and compose in
            var fakeSymbolValues = new SymbolValues(fakeSymbolTable);
            fakeSymbolValues.Set(fakeSlot, tableT1);
            var allValues = SymbolValues.Compose(fakeSymbolValues, dv.SymbolValues);

            var result = run.EvalAsync(CancellationToken.None, allValues).Result;

            if (expected is null)
            {
                Assert.IsInstanceOfType(result, typeof(BlankValue));
            }

            if( expected is Type expectedType)
            {
                Assert.IsInstanceOfType(result, typeof(ErrorValue));
            }
            else
            {
                Assert.AreEqual(expected is double dexp ? new decimal(dexp) : expected, result.ToObject());
            }
        }

        // Table 't1' has
        // 1st item with
        // Price = 100, Old_Price = 200,  Date = Date(2023, 6, 1), DateTime = DateTime(2023, 6, 1, 12, 0, 0)
        // 2nd item with
        // Price = 10
        // 3rd item with
        // Price = -10

        [DataTestMethod]

        //Basic case
        [DataRow("LookUp(t1, Price = 255).Price",
            null,
            "(__retrieveSingle(t1, __eq(t1, new_price, 255))).new_price")]

        [DataRow("LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\")).Price",
            100.0,
            "(__retrieveGUID(t1, GUID(00000000-0000-0000-0000-000000000001))).new_price")]

        //Basic case with And and Or
        [DataRow("LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\") And Price > 0).Price",
            100.0,
            "(__retrieveSingle(t1, __and(__eq(t1, localid, GUID(00000000-0000-0000-0000-000000000001)), __gt(t1, new_price, 0)))).new_price")]

        [DataRow("LookUp(t1, LocalId=GUID(\"00000000-0000-0000-0000-000000000001\") Or Price > 0).Price",
            100.0,
            "(__retrieveSingle(t1, __or(__eq(t1, localid, GUID(00000000-0000-0000-0000-000000000001)), __gt(t1, new_price, 0)))).new_price")]

        // variable
        [DataRow("LookUp(t1, LocalId=_g1).Price",
            100.0,
            "(__retrieveGUID(t1, _g1)).new_price")]

        // Date
        [DataRow("LookUp(t1, Date = Date(2023, 6, 1)).Price",
            100.0,
            "(__retrieveSingle(t1, __eq(t1, new_date, Date(2023, 6, 1)))).new_price")]

        // DateTime with coercion
        [DataRow("LookUp(t1, DateTime = Date(2023, 6, 1)).Price",
             null,
            "(__retrieveSingle(t1, __eq(t1, new_datetime, DateToDateTime(Date(2023, 6, 1))))).new_price")]

        [DataRow("LookUp(t1, DateTime = DateTime(2023, 6, 1, 12, 0, 0)).Price",
             100.0,
            "(__retrieveSingle(t1, __eq(t1, new_datetime, DateTime(2023, 6, 1, 12, 0, 0)))).new_price")]


        // reversed order still ok 
        [DataRow("LookUp(t1, _g1 = LocalId).Price",
            100.0,
            "(__retrieveGUID(t1, _g1)).new_price")]

        // explicit ThisRecord is ok. IR will handle. 
        [DataRow("LookUp(t1, ThisRecord.LocalId=_g1).Price",
            100.0,
            "(__retrieveGUID(t1, _g1)).new_price")]

        // Alias is ok. IR will handle. 
        [DataRow("LookUp(t1 As XYZ, XYZ.LocalId=_g1).Price",
            100.0,
            "(__retrieveGUID(t1, _g1)).new_price")] // variable

        // lambda uses ThisRecord.Price, can't delegate
        [DataRow("LookUp(t1, LocalId=If(Price>50, _g1, _gMissing)).Price",
            100.0,
            "(LookUp(t1, (EqGuid(localid,If(GtNumbers(new_price,50), (_g1), (_gMissing)))))).new_price",
            "Warning 22-27: Can't delegate LookUp: Expression compares multiple fields.")]

        // On non primary field.
        [DataRow("LookUp(t1, Price > 50).Price",
            100.0,
            "(__retrieveSingle(t1, __gt(t1, new_price, 50))).new_price")]

        // successful with complex expression
        [DataRow("LookUp(t1, LocalId=If(true, _g1, _gMissing)).Price",
            100.0,
            "(__retrieveGUID(t1, If(True, (_g1), (_gMissing)))).new_price")]

        // nested delegation, both delegated.
        [DataRow("LookUp(t1, LocalId=LookUp(t1, LocalId=_g1).LocalId).Price",
            100.0,
            "(__retrieveGUID(t1, (__retrieveGUID(t1, _g1)).localid)).new_price"
            )]

        // Can't delegate if Table Arg is delegated.
        [DataRow("LookUp(Filter(t1, Price = 100), localid=_g1).Price",
            100.0,
            "(LookUp(__retrieveMultiple(t1, __eq(t1, new_price, 100), 999), (EqGuid(localid,_g1)))).new_price",
            "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows."
        )]

        // Can't delegate if Table Arg is delegated.
        [DataRow("LookUp(FirstN(t1, 1), localid=_g1).Price",
            100.0,
            "(LookUp(__retrieveMultiple(t1, __noFilter(), 1), (EqGuid(localid,_g1)))).new_price",
            "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")] // $$$ span

        // Can Delegate on non primary-key field.
        [DataRow("LookUp(t1, LocalId=LookUp(t1, ThisRecord.Price>50).LocalId).Price",
            100.0,
            "(__retrieveGUID(t1, (__retrieveSingle(t1, __gt(t1, new_price, 50))).localid)).new_price")]

        [DataRow("LookUp(t1, LocalId=First([_g1,_gMissing]).Value).Price",
            100.0,
            "(__retrieveGUID(t1, (First(Table({Value:_g1}, {Value:_gMissing}))).Value)).new_price")]

        // unsupported function, can't yet delegate
        [DataRow("Last(t1).Price",
            -10.0,
            "(Last(t1)).new_price",
            "Warning 5-7: This operation on table 'local' may not work if it has more than 999 rows."
            )]

        // unsupported function, can't yet delegate
        [DataRow("CountRows(t1)",
            3.0,
            "CountRows(t1)",
            "Warning 10-12: This operation on table 'local' may not work if it has more than 999 rows."
            )]

        // Functions like IsBlank, Collect,Patch, shouldn't require delegation. Ensure no warnings. 
        [DataRow("IsBlank(t1)",
            false, // nothing to delegate
            "IsBlank(t1)"
            )]

        [DataRow("IsBlank(Filter(t1, 1=1))",
            false, // nothing to delegate
            "IsBlank(Filter(t1, (EqNumbers(1,1))))",
            "Warning 15-17: This operation on table 'local' may not work if it has more than 999 rows."
            )]


        [DataRow("Collect(t1, { Price : 200}).Price",
            200.0, // Collect shouldn't give warnings. 
            "(Collect((t1), {new_price:200})).new_price"
            )]

        // Aliasing prevents delegation. 
        [DataRow("With({r : t1}, LookUp(r, LocalId=_g1).Price)",
            100.0,
            "With({r:t1}, ((LookUp(r, (EqGuid(localid,_g1)))).new_price))",
            "Warning 10-12: This operation on table 'local' may not work if it has more than 999 rows.")]

        // $$$ Confirm is NotFound Error or Blank? 
        [DataRow("IsError(LookUp(t1, LocalId=If(false, _g1, _gMissing)))",
            true, // delegated, but not found is Error
            "IsError(__retrieveGUID(t1, If(False, (_g1), (_gMissing))))")]

        // $$$ Does using fakeT1, same as t1, cause warnings since it's not delegated?
        [DataRow("LookUp(fakeT1, LocalId=_g1).Price",
            100.0,
            "(LookUp(fakeT1, (EqGuid(localid,_g1)))).new_price")] // variable

        [DataRow("With( { f : _g1}, LookUp(t1, LocalId=f)).Price",
            100.0,
            "(With({f:_g1}, (__retrieveGUID(t1, f)))).new_price")] // variable

        [DataRow("LookUp(t1, LocalId=LocalId).Price",
            100.0,
            "(LookUp(t1, (EqGuid(localid,localid)))).new_price",
            "Warning 18-19: This predicate will always be true. Did you mean to use ThisRecord or [@ ]?",
            "Warning 19-26: Can't delegate LookUp: Expression compares multiple fields.")] // variable

        // Error Handling
        [DataRow("LookUp(t1, Price = If(1/0, 255)).Price",
            typeof(ErrorValue),
            "(__retrieveSingle(t1, __eq(t1, new_price, If(NumberToBoolean(DivNumbers(1,0)), (255))))).new_price")]

        // Blank Handling
        [DataRow("LookUp(t1, Price = Blank()).Price",
            null,
            "(__retrieveSingle(t1, __eq(t1, new_price, Blank()))).new_price")]

        [DataRow("LookUp(t1, Price <> Blank()).Price",
            100.0,
            "(__retrieveSingle(t1, __neq(t1, new_price, Blank()))).new_price")]

        [DataRow("LookUp(t1, Price < Blank()).Price",
            -10.0,
            "(__retrieveSingle(t1, __lt(t1, new_price, Blank()))).new_price")]

        [DataRow("LookUp(t1, Currency > 0).Price",
            100.0,
            "(LookUp(t1, (GtNumbers(new_currency,0)))).new_price",
            "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        public void LookUpDelegationFloat(string expr, object expected, string expectedIr, params string[] expectedWarnings)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels(numberIsFloat: true);
            var tableT1 = dv.AddTable(displayName, logicalName);

            var opts = _parserAllowSideEffects_NumberIsFloat;
            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_g1", FormulaValue.New(_g1)); // matches entity
            engine1.UpdateVariable("_gMissing", FormulaValue.New(Guid.Parse("00000000-0000-0000-9999-000000000001"))); // no match

            // Add a variable with same table type.
            // But it's not in the same symbol table, so we can't delegate this. 
            // Previously this was UpdateVariable, but UpdateVariable no longer supports dataverse tables (by design).
            var fakeSymbolTable = new SymbolTable();
            var fakeSlot = fakeSymbolTable.AddVariable("fakeT1", tableT1.Type);
            var allSymbols = ReadOnlySymbolTable.Compose(fakeSymbolTable, dv.Symbols);

            var check = engine1.Check(expr, options: opts, symbolTable: allSymbols);
            Assert.IsTrue(check.IsSuccess, string.Join("\r\n", check.Errors.Select(ee => ee.Message)));

            // comapre IR to verify the delegations are happening exactly where we expect 
            var irNode = check.ApplyIR();
            var actualIr = check.GetCompactIRString();
            Assert.AreEqual(expectedIr, actualIr);

            // Validate delegation warnings.
            // error.ToString() will capture warning status, message, and source span. 
            var errors = check.ApplyErrors();

            var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

            Assert.AreEqual(expectedWarnings.Length, errorList.Length);
            for (int i = 0; i < errorList.Length; i++)
            {
                Assert.AreEqual(expectedWarnings[i], errorList[i]);
            }

            // Can still run and verify results. 
            var run = check.GetEvaluator();

            // Place a reference to tableT1 in the fakeT1 symbol values and compose in
            var fakeSymbolValues = new SymbolValues(fakeSymbolTable);
            fakeSymbolValues.Set(fakeSlot, tableT1);
            var allValues = SymbolValues.Compose(fakeSymbolValues, dv.SymbolValues);

            var result = run.EvalAsync(CancellationToken.None, allValues).Result;

            if (expected is null)
            {
                Assert.IsInstanceOfType(result, typeof(BlankValue));
            }

            if (expected is Type expectedType)
            {
                Assert.IsInstanceOfType(result, typeof(ErrorValue));
            }
            else
            {
                Assert.AreEqual(expected, result.ToObject());
            }
        }

        // Table 't1' has
        // 1st item with
        // Price = 100, Old_Price = 200,  Date = Date(2023, 6, 1), DateTime = DateTime(2023, 6, 1, 12, 0, 0)
        // 2nd item with
        // Price = 10
        // 3rd item with
        // Price = -10

        [TestMethod]

        // Basic case.
        [DataRow("FirstN(t1, 2)",
            2,
            "__retrieveMultiple(t1, __noFilter(), Float(2))")]

        // Variable as arg 
        [DataRow("FirstN(t1, _count)",
            3,
            "__retrieveMultiple(t1, __noFilter(), _count)")]

        // Function as arg 
        [DataRow("FirstN(t1, If(1<0,_count, 1))",
            1,
            "__retrieveMultiple(t1, __noFilter(), If(LtDecimals(1,0), (_count), (Float(1))))")]

        // Filter inside FirstN, both can be cominded (vice versa isn't true)
        [DataRow("FirstN(Filter(t1, Price > 90), 10)",
            1,
            "__retrieveMultiple(t1, __gt(t1, new_price, 90), Float(10))")]

        // Aliasing prevents delegation. 
        [DataRow("With({r : t1}, FirstN(r, Float(100)))",
            3,
            "With({r:t1}, (FirstN(r, Float(100))))",
            "Warning 10-12: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Error handling

        // Error propagates
        [DataRow("FirstN(t1, 1/0)",
            -1,
            "__retrieveMultiple(t1, __noFilter(), Float(DivDecimals(1,0)))")]

        // Blank is treated as 0.
        [DataRow("FirstN(t1, If(1<0, 1))",
            0,
            "__retrieveMultiple(t1, __noFilter(), Float(If(LtDecimals(1,0), (1))))")]

        //Inserts default second arg.
       [DataRow("FirstN(t1)",
            1,
            "__retrieveMultiple(t1, __noFilter(), 1)")]
        public void FirstNDelegation(string expr, int expectedRows, string expectedIr, params string[] expectedWarnings)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();  // numberIsFloat: false
            var tableT1 = dv.AddTable(displayName, logicalName);

            var opts = _parserAllowSideEffects;
            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            config.Features.FirstLastNRequiresSecondArguments = false;
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_count", FormulaValue.New(100));

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check.IsSuccess);

            // compare IR to verify the delegations are happening exactly where we expect 
            var irNode = check.ApplyIR();
            var actualIr = check.GetCompactIRString();
            Assert.AreEqual(expectedIr, actualIr);

            // Validate delegation warnings.
            // error.ToString() will capture warning status, message, and source span. 
            var errors = check.ApplyErrors();

            var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

            Assert.AreEqual(expectedWarnings.Length, errorList.Length);
            for (int i = 0; i < errorList.Length; i++)
            {
                Assert.AreEqual(expectedWarnings[i], errorList[i]);
            }

            var run = check.GetEvaluator();

            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            // To check error cases.
            if(expectedRows < 0)
            {
                Assert.IsInstanceOfType(result, typeof(ErrorValue));
            }
            else
            {
                Assert.IsInstanceOfType(result, typeof(TableValue));
                Assert.AreEqual(expectedRows, ((TableValue)result).Rows.Count());
            }
        }

        // Table 't1' has
        // 1st item with
        // Price = 100, Old_Price = 200,  Date = Date(2023, 6, 1), DateTime = DateTime(2023, 6, 1, 12, 0, 0)
        // 2nd item with
        // Price = 10
        // 3rd item with
        // Price = -10

        [TestMethod]

        // Basic case.
        [DataRow("FirstN(t1, 2)",
            2,
            "__retrieveMultiple(t1, __noFilter(), 2)")]

        // Variable as arg 
        [DataRow("FirstN(t1, _count)",
            3,
            "__retrieveMultiple(t1, __noFilter(), _count)")]

        // Function as arg 
        [DataRow("FirstN(t1, If(1<0,_count, 1))",
            1,
            "__retrieveMultiple(t1, __noFilter(), If(LtNumbers(1,0), (_count), (1)))")]

        // Filter inside FirstN, both can be cominded (vice versa isn't true)
        [DataRow("FirstN(Filter(t1, Price > 90), 10)",
            1,
            "__retrieveMultiple(t1, __gt(t1, new_price, 90), 10)")]

        // Aliasing prevents delegation. 
        [DataRow("With({r : t1}, FirstN(r, 100))",
            3,
            "With({r:t1}, (FirstN(r, 100)))",
            "Warning 10-12: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Error handling

        // Error propagates
        [DataRow("FirstN(t1, 1/0)",
            -1,
            "__retrieveMultiple(t1, __noFilter(), DivNumbers(1,0))")]

        // Blank is treated as 0.
        [DataRow("FirstN(t1, If(1<0, 1))",
            0,
            "__retrieveMultiple(t1, __noFilter(), If(LtNumbers(1,0), (1)))")]

        //Inserts default second arg.
        [DataRow("FirstN(t1)",
            1,
            "__retrieveMultiple(t1, __noFilter(), 1)")]
        public void FirstNDelegationFloat(string expr, int expectedRows, string expectedIr, params string[] expectedWarnings)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels(numberIsFloat: true);
            var tableT1 = dv.AddTable(displayName, logicalName);

            var opts = _parserAllowSideEffects_NumberIsFloat;
            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            config.Features.FirstLastNRequiresSecondArguments = false;
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_count", FormulaValue.New(100));

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check.IsSuccess);

            // compare IR to verify the delegations are happening exactly where we expect 
            var irNode = check.ApplyIR();
            var actualIr = check.GetCompactIRString();
            Assert.AreEqual(expectedIr, actualIr);

            // Validate delegation warnings.
            // error.ToString() will capture warning status, message, and source span. 
            var errors = check.ApplyErrors();

            var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

            Assert.AreEqual(expectedWarnings.Length, errorList.Length);
            for (int i = 0; i < errorList.Length; i++)
            {
                Assert.AreEqual(expectedWarnings[i], errorList[i]);
            }

            var run = check.GetEvaluator();

            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            // To check error cases.
            if (expectedRows < 0)
            {
                Assert.IsInstanceOfType(result, typeof(ErrorValue));
            }
            else
            {
                Assert.IsInstanceOfType(result, typeof(TableValue));
                Assert.AreEqual(expectedRows, ((TableValue)result).Rows.Count());
            }
        }

        // Table 't1' has
        // 1st item with
        // Price = 100, Old_Price = 200,  Date = Date(2023, 6, 1), DateTime = DateTime(2023, 6, 1, 12, 0, 0)
        // 2nd item with
        // Price = 10
        // 3rd item with
        // Price = -10

        [TestMethod]

        //Basic case 
        [DataRow("First(t1).Price",
            100.0,
            "(__retrieveSingle(t1, __noFilter())).new_price")]

        // Filter inside FirstN, both can be combined *(vice versa isn't true)*
        [DataRow("First(Filter(t1, Price < 100)).Price",
            10.0,
            "(__retrieveSingle(t1, __lt(t1, new_price, 100))).new_price")]

        [DataRow("First(FirstN(t1, 2)).Price",
            100.0,
            "(__retrieveSingle(t1, __noFilter())).new_price")]

        // Aliasing prevents delegation. 
        [DataRow("With({r : t1}, First(r).Price)",
            100.0,
            "With({r:t1}, ((First(r)).new_price))",
            "Warning 10-12: This operation on table 'local' may not work if it has more than 999 rows.")]
        public void FirstDelegation(string expr, object expected, string expectedIr, params string[] expectedWarnings)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();  // numberIsFloat: false
            var tableT1 = dv.AddTable(displayName, logicalName);

            var opts = _parserAllowSideEffects;
            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_count", FormulaValue.New(100));

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check.IsSuccess);

            // compare IR to verify the delegations are happening exactly where we expect 
            var irNode = check.ApplyIR();
            var actualIr = check.GetCompactIRString();
            Assert.AreEqual(expectedIr, actualIr);

            // Validate delegation warnings.
            // error.ToString() will capture warning status, message, and source span. 
            var errors = check.ApplyErrors();

            var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

            Assert.AreEqual(expectedWarnings.Length, errorList.Length);
            for (int i = 0; i < errorList.Length; i++)
            {
                Assert.AreEqual(expectedWarnings[i], errorList[i]);
            }

            var run = check.GetEvaluator();

            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.AreEqual(new decimal((double)expected), result.ToObject());
        }

        // Table 't1' has
        // 1st item with
        // Price = 100, Old_Price = 200,  Date = Date(2023, 6, 1), DateTime = DateTime(2023, 6, 1, 12, 0, 0)
        // 2nd item with
        // Price = 10
        // 3rd item with
        // Price = -10

        [TestMethod]

        //Basic case 
        [DataRow("First(t1).Price",
            100.0,
            "(__retrieveSingle(t1, __noFilter())).new_price")]

        // Filter inside FirstN, both can be combined *(vice versa isn't true)*
        [DataRow("First(Filter(t1, Price < 100)).Price",
            10.0,
            "(__retrieveSingle(t1, __lt(t1, new_price, 100))).new_price")]

        [DataRow("First(FirstN(t1, 2)).Price",
            100.0,
            "(__retrieveSingle(t1, __noFilter())).new_price")]

        // Aliasing prevents delegation. 
        [DataRow("With({r : t1}, First(r).Price)",
            100.0,
            "With({r:t1}, ((First(r)).new_price))",
            "Warning 10-12: This operation on table 'local' may not work if it has more than 999 rows.")]
        public void FirstDelegationFloat(string expr, object expected, string expectedIr, params string[] expectedWarnings)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels(numberIsFloat: true);
            var tableT1 = dv.AddTable(displayName, logicalName);

            var opts = _parserAllowSideEffects_NumberIsFloat;
            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_count", FormulaValue.New(100));

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check.IsSuccess);

            // compare IR to verify the delegations are happening exactly where we expect 
            var irNode = check.ApplyIR();
            var actualIr = check.GetCompactIRString();
            Assert.AreEqual(expectedIr, actualIr);

            // Validate delegation warnings.
            // error.ToString() will capture warning status, message, and source span. 
            var errors = check.ApplyErrors();

            var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

            Assert.AreEqual(expectedWarnings.Length, errorList.Length);
            for (int i = 0; i < errorList.Length; i++)
            {
                Assert.AreEqual(expectedWarnings[i], errorList[i]);
            }

            var run = check.GetEvaluator();

            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.AreEqual(expected, result.ToObject());
        }

        // Table 't1' has
        // 1st item with
        // Price = 100, Old_Price = 200,  Date = Date(2023, 6, 1), DateTime = DateTime(2023, 6, 1, 12, 0, 0), Currency = 100
        // 2nd item with
        // Price = 10
        // 3rd item with
        // Price = -10

        [TestMethod]

        //Basic case 
        [DataRow("Filter(t1, Price < 100)",
            2,
            "__retrieveMultiple(t1, __lt(t1, new_price, 100), 999)")]

        [DataRow("Filter(t1, Price <= 100)",
            3,
            "__retrieveMultiple(t1, __lte(t1, new_price, 100), 999)")]

        [DataRow("Filter(t1, Price = 100)",
            1,
            "__retrieveMultiple(t1, __eq(t1, new_price, 100), 999)")]

        [DataRow("Filter(t1, Price > 100)",
            0,
            "__retrieveMultiple(t1, __gt(t1, new_price, 100), 999)")]

        [DataRow("Filter(t1, Price >= 100)",
            1,
            "__retrieveMultiple(t1, __gte(t1, new_price, 100), 999)")]

        [DataRow("Filter(t1, Price < Float(120))",
            3,
            "__retrieveMultiple(t1, __lt(t1, new_price, Float(120)), 999)")]

        [DataRow("Filter(t1, Price < Decimal(20))",
            2,
            "__retrieveMultiple(t1, __lt(t1, new_price, Decimal(20)), 999)")]

        [DataRow("Filter(t1, Price < Abs(-120))",
            3,
            "__retrieveMultiple(t1, __lt(t1, new_price, Abs(Coalesce(NegateDecimal(120), 0))), 999)")]

        // These two tests have Coalesce for numeric literals where the NumberIsFloat version does not.
        // Although not wrong, they should be the same.  Being tracked with https://github.com/microsoft/Power-Fx/issues/1609
        // Date
        [DataRow("Filter(t1, Date = Date(2023, 6, 1))",
            1,
            "__retrieveMultiple(t1, __eq(t1, new_date, Date(Coalesce(Float(2023), 0), Coalesce(Float(6), 0), Coalesce(Float(1), 0))), 999)")]

        // DateTime with coercion
        [DataRow("Filter(t1, DateTime = Date(2023, 6, 1))",
            0,
            "__retrieveMultiple(t1, __eq(t1, new_datetime, DateToDateTime(Date(Coalesce(Float(2023), 0), Coalesce(Float(6), 0), Coalesce(Float(1), 0)))), 999)")]

        //Order doesn't matter
        [DataRow("Filter(t1, 0 > Price)",
            1,
            "__retrieveMultiple(t1, __lt(t1, new_price, 0), 999)")]

        // Variable as arg 
        [DataRow("Filter(t1, Price > _count)",
            0,
            "__retrieveMultiple(t1, __gt(t1, new_price, _count), 999)")]

        // Function as arg 
        [DataRow("Filter(t1, Price > If(1<0,_count, 1))",
            2,
            "__retrieveMultiple(t1, __gt(t1, new_price, If(LtDecimals(1,0), (_count), (1))), 999)")]

        // Filter nested in another function both delegated.
        [DataRow("Filter(Filter(t1, Price > 0), Price < 100)",
            1,
            "__retrieveMultiple(t1, __and(__gt(t1, new_price, 0), __lt(t1, new_price, 100)), 999)")]

        // Basic case with And
        [DataRow("Filter(t1, Price < 120 And 90 < Price)",
            1,
            "__retrieveMultiple(t1, __and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)), 999)")]

        // Basic case with Or
        [DataRow("Filter(t1, Price < 0 Or Price > 90)",
            2,
            "__retrieveMultiple(t1, __or(__lt(t1, new_price, 0), __gt(t1, new_price, 90)), 999)")]

        // Delegation Not Allowed 

        // predicate that uses function that is not delegable.
        [DataRow("Filter(t1, IsBlank(Price))",
            0,
            "Filter(t1, (IsBlank(new_price)))",
            "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        // predicate that uses function that is not delegable.
        [DataRow("Filter(t1, Price < 120 And IsBlank(_count))",
            0,
            "Filter(t1, (And(LtDecimals(new_price,120), (IsBlank(_count)))))",
            "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Filter nested in FirstN function. Only FirstN is delegated.
        [DataRow("Filter(FirstN(t1, 100), Price = 100)",
            1,
            "Filter(__retrieveMultiple(t1, __noFilter(), Float(100)), (EqDecimals(new_price,100)))",
            "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Aliasing prevents delegation. 
        [DataRow("With({r : t1}, Filter(r, Price < 120))",
            3,
            "With({r:t1}, (Filter(r, (LtDecimals(new_price,120)))))",
            "Warning 10-12: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Comparing fields can't be delegated.
        [DataRow("Filter(t1, Price < Old_Price)",
            2,
            "Filter(t1, (LtDecimals(new_price,old_price)))",
            "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Error handling
        [DataRow("Filter(t1, Price < 1/0)",
            -1,
            "__retrieveMultiple(t1, __lt(t1, new_price, DivDecimals(1,0)), 999)")]

        // Blank handling
        [DataRow("Filter(t1, Price < Blank())",
            1,
            "__retrieveMultiple(t1, __lt(t1, new_price, Blank()), 999)")]

        [DataRow("Filter(t1, Price > Blank())",
            2,
            "__retrieveMultiple(t1, __gt(t1, new_price, Blank()), 999)")]

        [DataRow("Filter(t1, Price = Blank())",
            0,
            "__retrieveMultiple(t1, __eq(t1, new_price, Blank()), 999)")]

        [DataRow("Filter(t1, Price <> Blank())",
            3,
            "__retrieveMultiple(t1, __neq(t1, new_price, Blank()), 999)")]

        [DataRow("Filter(t1, Currency > 0)",
            1,
            "Filter(t1, (GtDecimals(new_currency,0)))",
            "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        public void FilterDelegation(string expr, int expectedRows, string expectedIr, params string[] expectedWarnings)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();  // numberIsFloat: false
            var tableT1 = dv.AddTable(displayName, logicalName);
            var tableT2 = dv.AddTable("t2", "remote");

            var opts = _parserAllowSideEffects;
            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_count", FormulaValue.New(100m));

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check.IsSuccess);

            // compare IR to verify the delegations are happening exactly where we expect 
            var irNode = check.ApplyIR();
            var actualIr = check.GetCompactIRString();
            Assert.AreEqual(expectedIr, actualIr);

            // Validate delegation warnings.
            // error.ToString() will capture warning status, message, and source span. 
            var errors = check.ApplyErrors();

            var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

            Assert.AreEqual(expectedWarnings.Length, errorList.Length);
            for (int i = 0; i < errorList.Length; i++)
            {
                Assert.AreEqual(expectedWarnings[i], errorList[i]);
            }

            var run = check.GetEvaluator();

            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            // To check error cases.
            if (expectedRows < 0)
            {
                Assert.IsInstanceOfType(result, typeof(ErrorValue));
            }
            else
            {
                Assert.IsInstanceOfType(result, typeof(TableValue));
                Assert.AreEqual(expectedRows, ((TableValue)result).Rows.Count());
            }
        }

        // Table 't1' has
        // 1st item with
        // Price = 100, Old_Price = 200,  Date = Date(2023, 6, 1), DateTime = DateTime(2023, 6, 1, 12, 0, 0)
        // 2nd item with
        // Price = 10
        // 3rd item with
        // Price = -10

        [TestMethod]

        //Basic case 
        [DataRow("Filter(t1, Price < 100)",
            2,
            "__retrieveMultiple(t1, __lt(t1, new_price, 100), 999)")]

        [DataRow("Filter(t1, Price <= 100)",
            3,
            "__retrieveMultiple(t1, __lte(t1, new_price, 100), 999)")]

        [DataRow("Filter(t1, Price = 100)",
            1,
            "__retrieveMultiple(t1, __eq(t1, new_price, 100), 999)")]

        [DataRow("Filter(t1, Price > 100)",
            0,
            "__retrieveMultiple(t1, __gt(t1, new_price, 100), 999)")]

        [DataRow("Filter(t1, Price >= 100)",
            1,
            "__retrieveMultiple(t1, __gte(t1, new_price, 100), 999)")]

        [DataRow("Filter(t1, Price < Float(120))",
            3,
            "__retrieveMultiple(t1, __lt(t1, new_price, Float(120)), 999)")]

        [DataRow("Filter(t1, Price < Decimal(20))",
            2,
            "__retrieveMultiple(t1, __lt(t1, new_price, Value(Decimal(20))), 999)")]

        [DataRow("Filter(t1, Price < Abs(-120))",
            3,
            "__retrieveMultiple(t1, __lt(t1, new_price, Abs(Coalesce(Negate(120), 0))), 999)")]

        // Date
        [DataRow("Filter(t1, Date = Date(2023, 6, 1))",
            1,
            "__retrieveMultiple(t1, __eq(t1, new_date, Date(2023, 6, 1)), 999)")]

        // DateTime with coercion
        [DataRow("Filter(t1, DateTime = Date(2023, 6, 1))",
            0,
            "__retrieveMultiple(t1, __eq(t1, new_datetime, DateToDateTime(Date(2023, 6, 1))), 999)")]

        //Order doesn't matter
        [DataRow("Filter(t1, 0 > Price)",
            1,
            "__retrieveMultiple(t1, __lt(t1, new_price, 0), 999)")]

        // Variable as arg 
        [DataRow("Filter(t1, Price > _count)",
            0,
            "__retrieveMultiple(t1, __gt(t1, new_price, _count), 999)")]

        // Function as arg 
        [DataRow("Filter(t1, Price > If(1<0,_count, 1))",
            2,
            "__retrieveMultiple(t1, __gt(t1, new_price, If(LtNumbers(1,0), (_count), (1))), 999)")]

        // Filter nested in another function both delegated.
        [DataRow("Filter(Filter(t1, Price > 0), Price < 100)",
            1,
            "__retrieveMultiple(t1, __and(__gt(t1, new_price, 0), __lt(t1, new_price, 100)), 999)")]

        // Basic case with And
        [DataRow("Filter(t1, Price < 120 And 90 < Price)",
            1,
            "__retrieveMultiple(t1, __and(__lt(t1, new_price, 120), __gt(t1, new_price, 90)), 999)")]

        // Basic case with Or
        [DataRow("Filter(t1, Price < 0 Or Price > 90)",
            2,
            "__retrieveMultiple(t1, __or(__lt(t1, new_price, 0), __gt(t1, new_price, 90)), 999)")]

        // Delegation Not Allowed 

        // predicate that uses function that is not delegable.
        [DataRow("Filter(t1, IsBlank(Price))",
            0,
            "Filter(t1, (IsBlank(new_price)))",
            "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        // predicate that uses function that is not delegable.
        [DataRow("Filter(t1, Price < 120 And IsBlank(_count))",
            0,
            "Filter(t1, (And(LtNumbers(new_price,120), (IsBlank(_count)))))",
            "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Filter nested in FirstN function. Only FirstN is delegated.
        [DataRow("Filter(FirstN(t1, 100), Price = 100)",
            1,
            "Filter(__retrieveMultiple(t1, __noFilter(), 100), (EqNumbers(new_price,100)))",
            "Warning 14-16: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Aliasing prevents delegation. 
        [DataRow("With({r : t1}, Filter(r, Price < 120))",
            3,
            "With({r:t1}, (Filter(r, (LtNumbers(new_price,120)))))",
            "Warning 10-12: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Comparing fields can't be delegated.
        [DataRow("Filter(t1, Price < Old_Price)",
            2,
            "Filter(t1, (LtNumbers(new_price,old_price)))",
            "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]

        // Error handling
        [DataRow("Filter(t1, Price < 1/0)",
            -1,
            "__retrieveMultiple(t1, __lt(t1, new_price, DivNumbers(1,0)), 999)")]

        // Blank handling
        [DataRow("Filter(t1, Price < Blank())",
            1,
            "__retrieveMultiple(t1, __lt(t1, new_price, Blank()), 999)")]

        [DataRow("Filter(t1, Price > Blank())",
            2,
            "__retrieveMultiple(t1, __gt(t1, new_price, Blank()), 999)")]

        [DataRow("Filter(t1, Price = Blank())",
            0,
            "__retrieveMultiple(t1, __eq(t1, new_price, Blank()), 999)")]

        [DataRow("Filter(t1, Price <> Blank())",
            3,
            "__retrieveMultiple(t1, __neq(t1, new_price, Blank()), 999)")]

        [DataRow("Filter(t1, Currency > 0)",
            1,
            "Filter(t1, (GtNumbers(new_currency,0)))",
            "Warning 7-9: This operation on table 'local' may not work if it has more than 999 rows.")]
        public void FilterDelegationFloat(string expr, int expectedRows, string expectedIr, params string[] expectedWarnings)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels(numberIsFloat: true);
            var tableT1 = dv.AddTable(displayName, logicalName);
            var tableT2 = dv.AddTable("t2", "remote");

            var opts = _parserAllowSideEffects_NumberIsFloat;
            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_count", FormulaValue.New(100));

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check.IsSuccess);

            // compare IR to verify the delegations are happening exactly where we expect 
            var irNode = check.ApplyIR();
            var actualIr = check.GetCompactIRString();
            Assert.AreEqual(expectedIr, actualIr);

            // Validate delegation warnings.
            // error.ToString() will capture warning status, message, and source span. 
            var errors = check.ApplyErrors();

            var errorList = errors.Select(x => x.ToString()).OrderBy(x => x).ToArray();

            Assert.AreEqual(expectedWarnings.Length, errorList.Length);
            for (int i = 0; i < errorList.Length; i++)
            {
                Assert.AreEqual(expectedWarnings[i], errorList[i]);
            }

            var run = check.GetEvaluator();

            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            // To check error cases.
            if (expectedRows < 0)
            {
                Assert.IsInstanceOfType(result, typeof(ErrorValue));
            }
            else
            {
                Assert.IsInstanceOfType(result, typeof(TableValue));
                Assert.AreEqual(expectedRows, ((TableValue)result).Rows.Count());
            }
        }

        [DataTestMethod]
        [DataRow("LookUp(t1, LocalId=If(Price>50, _g1, _gMissing)).Price",
            "Warning 22-27: Can't delegate LookUp: Expression compares multiple fields.")]
        [DataRow("LookUp(t1, LocalId=LocalId).Price",
            "Warning 18-19: Este predicado será sempre verdadeiro. Você quis usar ThisRecord ou [@ ]?",
            "Warning 19-26: Can't delegate LookUp: Expression compares multiple fields.")]
        [DataRow("LookUp(Filter(t1, 1=1), localid=_g1).Price",
            "Warning 14-16: Esta operação na tabela \"local\" poderá não funcionar se tiver mais de 999 linhas."
            )]
        public void LookUpDelegationWarningLocaleTest(string expr, params string[] expectedWarnings)
        {
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            var tableT1 = dv.AddTable(displayName, logicalName);

            var opts = _parserAllowSideEffects;
            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            engine1.UpdateVariable("_g1", FormulaValue.New(_g1)); // matches entity
            engine1.UpdateVariable("_gMissing", FormulaValue.New(Guid.Parse("00000000-0000-0000-9999-000000000001"))); // no match

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check.IsSuccess, string.Join("\r\n", check.Errors.Select(ee => ee.Message)));

            var errors_pt_br = check.GetErrorsInLocale(culture: CultureInfo.CreateSpecificCulture("pt-BR"));

            var errorList = errors_pt_br.Select(x => x.ToString()).OrderBy(x => x).ToArray();

            Assert.AreEqual(expectedWarnings.Length, errorList.Length);
            for (int i = 0; i < errorList.Length; i++)
            {
                Assert.AreEqual(expectedWarnings[i], errorList[i]);
            }
        }

        [DataTestMethod]
        // DV works by making a copy of the entity when retrieving it. In-memory works by reference.
        [DataRow("With({oldCount:CountRows(t1)},Collect(t1,{Price:200});CountRows(t1)-oldCount)", 1.0)]
        [DataRow("Collect(t1,{Price:110});CountRows(t1)", 4.0)]
        [DataRow("With({x:Collect(t1,{Price:77})}, Patch(t1,Last(t1),{Price:x.Price + 3});CountRows(t1))", 4.0)]
        [DataRow("With({x:Collect(t1,{Price:77}), y:Collect(t1,{Price:88})}, Remove(t1,x);Remove(t1,y);CountRows(t1))", 3.0)]
        public void CacheBug(string expr, double expected)
        {
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            var opts = _parserAllowSideEffects;
            var config = new PowerFxConfig();
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check.IsSuccess);

            var run = check.GetEvaluator();

            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.AreEqual(new decimal(expected), result.ToObject());
        }

        [DataTestMethod]
        // DV works by making a copy of the entity when retrieving it. In-memory works by reference.
        [DataRow("With({oldCount:CountRows(t1)},Collect(t1,{Price:200});CountRows(t1)-oldCount)", 1.0)]
        [DataRow("Collect(t1,{Price:110});CountRows(t1)", 4.0)]
        [DataRow("With({x:Collect(t1,{Price:77})}, Patch(t1,Last(t1),{Price:x.Price + 3});CountRows(t1))", 4.0)]
        [DataRow("With({x:Collect(t1,{Price:77}), y:Collect(t1,{Price:88})}, Remove(t1,x);Remove(t1,y);CountRows(t1))", 3.0)]
        public void CacheBugFloat(string expr, double expected)
        {
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels(numberIsFloat: true);
            dv.AddTable(displayName, logicalName);

            var opts = _parserAllowSideEffects_NumberIsFloat;
            var config = new PowerFxConfig();
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check.IsSuccess);

            var run = check.GetEvaluator();

            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.AreEqual(expected, result.ToObject());
        }

        [DataTestMethod]
        [DataRow("Patch(t1, First(t1), {Price:200})")]
        public void PatchFunctionLean(string expr)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            el._getTargetedColumnName = () => "new_price";

            var opts = _parserAllowSideEffects;
            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check.IsSuccess);

            var run = check.GetEvaluator();

            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.IsNotInstanceOfType(result, typeof(ErrorValue));
        }

        [DataTestMethod]
        [DataRow("Patch(t1, First(t1), {Price:1000})")]

        // Test case left as reference to future change. It should also fail.
        //[DataRow("Set(Price, 200); Price")]
        public void PatchWithUpdateInvalidFieldError(string expr)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            // Inject update invalid fields error
            el._getTargetedColumnName = () => "Foo";

            var rowScopeSymbols = dv.GetRowScopeSymbols(tableLogicalName: logicalName);

            var opts = _parserAllowSideEffects;
            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);

            var allSymbols = ReadOnlySymbolTable.Compose(rowScopeSymbols, dv.Symbols);
            var check = engine1.Check(expr, options: opts, symbolTable: allSymbols);
            Assert.IsTrue(check.IsSuccess);

            var run = check.GetEvaluator();

            var entity = el.GetFirstEntity(logicalName, dv, CancellationToken.None); // any record
            var record = dv.Marshal(entity);
            var rowScopeValues = ReadOnlySymbolValues.NewFromRecord(rowScopeSymbols, record);
            var allValues = allSymbols.CreateValues(rowScopeValues, dv.SymbolValues);


            var result = run.EvalAsync(CancellationToken.None, allValues).Result;

            Assert.IsInstanceOfType(result, typeof(ErrorValue));
        }

        [DataTestMethod]
        [DataRow("Patch(t1, First(t1), {Price:1000})", false)]
        [DataRow("Patch(t1, First(t1), {Price:50})", true)]
        public void PatchWithNumberOutOfRangeError(string expr, bool succeeds)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";
            var errorMessage = "Number out of range error injected";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();

            dv.AddTable(displayName, logicalName);

            // Inject number out of range error
            el._checkColumnRange = (key, value) =>
            {
                if (key == "new_price")
                {
                    var number = (decimal)value;

                    if (number > 100)
                    {
                        return errorMessage;
                    }
                }

                return null;
            };

            var opts = _parserAllowSideEffects;
            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check.IsSuccess);

            var run = check.GetEvaluator();
            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            if (succeeds)
            {
                Assert.IsNotInstanceOfType(result, typeof(ErrorValue));
            }
            else
            {
                Assert.IsInstanceOfType(result, typeof(ErrorValue));
                Assert.IsTrue(((ErrorValue)result).Errors.First().Message.Contains(errorMessage));
            }
        }

        [DataTestMethod]
        [DataRow("Remove(t1, LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\")) )", false)]
        [DataRow("Remove(t1, First(t1))", true)]
        public void RemoveFunction(string expr, bool injectError)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";
            var errorMessage = "My custom error message!";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            if (injectError)
            {
                el._getCustomErrorMessage = () => errorMessage;
            }

            var opts = _parserAllowSideEffects;
            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check.IsSuccess);

            var run = check.GetEvaluator();

            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            if (injectError)
            {
                Assert.IsInstanceOfType(result, typeof(ErrorValue));
                Assert.AreEqual(errorMessage, ((ErrorValue)result).Errors.First().Message);
            }
            else
            {
                Assert.IsNotInstanceOfType(result, typeof(ErrorValue));

                // Verify on expression - this may be old or no

                // verify on entity - this should always be updated 
                Assert.IsFalse(el.Exists(new EntityReference(logicalName, _g1)));
            }
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
            entity1.Attributes["otherid"] = null;

            var symbols = dv.GetRowScopeSymbols(tableLogicalName: logicalName);

            var engine1 = new RecalcEngine();

            // Eval it                        
            var record = el.ConvertEntityToRecordValue(logicalName, dv, CancellationToken.None); // any record
            var runtimeConfig = ReadOnlySymbolValues.NewFromRecord(symbols, record);

            var result = engine1.EvalAsync(expr, CancellationToken.None, runtimeConfig: runtimeConfig).Result;
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

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels(); // numberIsFloat: false
            dv.AddTable(displayName, logicalName);

            var symbols = dv.GetRowScopeSymbols(tableLogicalName: logicalName);

            var engine1 = new RecalcEngine();

            // Eval it                        
            var record = el.ConvertEntityToRecordValue(logicalName, dv, CancellationToken.None); // any record
            var runtimeConfig = ReadOnlySymbolValues.NewFromRecord(symbols, record);

            // Case 1: Succeed 
            el._onLookupRef = null;
            var result2 = engine1.EvalAsync(expr, CancellationToken.None, runtimeConfig: runtimeConfig).Result;
            Assert.AreEqual(200m, result2.ToObject());

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

        // Calls to Dataverse have 3 possible outcomes:
        // - Success
        // - "Soft" failure - these are translated into ErrorValues and "caught".  Eg: record not found, access denied, network down, 
        // - "Hard" failures - these are bugs in our code and their exceptions aborts the execution.  Eg: NullRef, StackOveflow, etc 
        [DataTestMethod]
        [DataRow("ThisRecord.Other.Data")] // Relationship 
        public void NetworkErrorsFloat(string expr)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels(numberIsFloat: true);
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
            (DataverseConnection dv, _) = CreateMemoryForRelationshipModels();  // numberIsFloat: false

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
            Assert.IsTrue(type is DecimalType);

            // Throws on missing field. 
            Assert.ThrowsException<InvalidOperationException>(
                () => r.GetFieldType("new_missing"));

            // fails, must be logical name 
            Assert.ThrowsException<InvalidOperationException>(
                () => dv.GetRecordType("Locals"));
        }

        [TestMethod]
        public void TestDataverseConnectionFloat()
        {
            (DataverseConnection dv, _) = CreateMemoryForRelationshipModels(numberIsFloat: true);

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
            (DataverseConnection dv1, _) = CreateMemoryForRelationshipModels(); // numberIsFloat: false
            (var dv2, var el2) = CreateMemoryForRelationshipModels(); // numberIsFloat: false

            // Two orgs, can also both have the same logical name 
            dv1.AddTable("T1", "local");
            dv2.AddTable("T2", "local");

            el2.LookupRefCore(_eRef1).Attributes["new_price"] = 200;

            var engine1 = new RecalcEngine();
            var s1 = dv1.SymbolValues;
            var s2 = dv2.SymbolValues;

            var s12 = ReadOnlySymbolValues.Compose(s1, s2);

            var result = await engine1.EvalAsync("First(T1).Price*1000 + First(T2).Price", CancellationToken.None, runtimeConfig: s12).ConfigureAwait(false);
            Assert.AreEqual(100 * 1000 + 200m, result.ToObject());
        }

        [TestMethod]
        public async Task TwoOrgsFloat()
        {
            (DataverseConnection dv1, _) = CreateMemoryForRelationshipModels(numberIsFloat: true);
            (var dv2, var el2) = CreateMemoryForRelationshipModels(numberIsFloat: true);

            // Two orgs, can also both have the same logical name 
            dv1.AddTable("T1", "local");
            dv2.AddTable("T2", "local");

            el2.LookupRefCore(_eRef1).Attributes["new_price"] = 200;

            var engine1 = new RecalcEngine();
            var s1 = dv1.SymbolValues;
            var s2 = dv2.SymbolValues;

            var s12 = ReadOnlySymbolValues.Compose(s1, s2);

            var result = await engine1.EvalAsync("First(T1).Price*1000 + First(T2).Price", CancellationToken.None, runtimeConfig: s12).ConfigureAwait(false);

            Assert.AreEqual(100 * 1000 + 200.0, result.ToObject());
        }

        [TestMethod]
        public void DataverseConnectionMarshal()
        {
            (DataverseConnection dv, _) = CreateMemoryForRelationshipModels(); // numberIsFloat = false

            dv.AddTable("variableName", "local");

            // Marshal 
            var entity1 = new Entity("local", _g1);
            entity1.Attributes["new_price"] = 100;
            entity1.Attributes["rating"] = new Xrm.Sdk.OptionSetValue(2); // Warm

            var val1 = dv.Marshal(entity1);

            RecordType r = val1.Type;
            Assert.IsTrue(r.GetFieldType("new_price") is DecimalType);

            // Can also get fields on metadata not present in attributes
            Assert.IsTrue(r.GetFieldType("new_quantity") is DecimalType);

            // Getting fields
            var x = val1.GetField("new_price");
            Assert.AreEqual(100m, x.ToObject());

            // Blanks - pulling from the metadata.
            x = val1.GetField("new_quantity");
            Assert.IsTrue(x is BlankValue);
            Assert.IsTrue(x.Type is DecimalType);

            // OptionSets. 
            var opt = val1.GetField("rating");

            Assert.AreEqual("Warm", opt.ToObject());
            Assert.AreEqual("OptionSetValue (2=Warm)", opt.ToString());
        }

        [TestMethod]
        public void DataverseConnectionMarshalFloat()
        {
            (DataverseConnection dv, _) = CreateMemoryForRelationshipModels(numberIsFloat: true);

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
        }

        [TestMethod]
        public async Task DataverseTableValueOperationWithSameBehaviorTest()
        {
            var logicalName = "local";
            var displayName = "t1";
            var loopupExpr = "LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\")).Price";

            (DataverseConnection dv, DataverseEntityCache _, EntityLookup el) = CreateMemoryForRelationshipModelsWithCache(); // numberIsFloat: false
            dv.AddTable(displayName, logicalName);

            var config = new PowerFxConfig();
            config.SymbolTable.EnableMutationFunctions();

            // New engines to simulate how Cards eval all expressions
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            var result1 = await engine1.EvalAsync(loopupExpr, CancellationToken.None, runtimeConfig: dv.SymbolValues).ConfigureAwait(false);
            Assert.AreEqual(100m, result1.ToObject());

            // Simulates a row being deleted by an external user
            // This will delete the inner entity, without impacting DataverseEntityCache's cache
            await el.DeleteAsync(logicalName, _g1).ConfigureAwait(false);

            // Evals the same expression by a new engine. As DataverseEntityCache's cache is intact, we'll return the cached value.
            var engine4 = new RecalcEngine(config);
            engine4.EnableDelegation(dv.MaxRows);
            var result4 = await engine4.EvalAsync(loopupExpr, CancellationToken.None, runtimeConfig: dv.SymbolValues).ConfigureAwait(false);
            Assert.AreEqual(100m, result4.ToObject());

            // Refresh connection cache.
            dv.RefreshCache();

            // Evals the same expression by a new engine. Sum should now return the refreshed value.
            var engine7 = new RecalcEngine(config);
            engine7.EnableDelegation(dv.MaxRows);
            var result7 = await engine7.EvalAsync(loopupExpr, CancellationToken.None, runtimeConfig: dv.SymbolValues).ConfigureAwait(false);
            Assert.IsInstanceOfType(result7, typeof(ErrorValue));

            ErrorValue ev7 = (ErrorValue)result7;
            Assert.AreEqual("Error attempting Entity lookup. Entity local:00000000-0000-0000-0000-000000000001 not found", ev7.Errors[0].Message);
        }

        [TestMethod]
        public async Task DataverseTableValueOperationWithSameBehaviorTestFloat()
        {
            var logicalName = "local";
            var displayName = "t1";
            var loopupExpr = "LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\")).Price";

            (DataverseConnection dv, DataverseEntityCache _, EntityLookup el) = CreateMemoryForRelationshipModelsWithCache(numberIsFloat: true);
            dv.AddTable(displayName, logicalName);

            var config = new PowerFxConfig();
            config.SymbolTable.EnableMutationFunctions();

            // New engines to simulate how Cards eval all expressions
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);
            var result1 = await engine1.EvalAsync(loopupExpr, CancellationToken.None, runtimeConfig: dv.SymbolValues).ConfigureAwait(false);
            Assert.AreEqual(100.0, result1.ToObject());

            // Simulates a row being deleted by an external user
            // This will delete the inner entity, without impacting DataverseEntityCache's cache
            await el.DeleteAsync(logicalName, _g1).ConfigureAwait(false);

            // Evals the same expression by a new engine. As DataverseEntityCache's cache is intact, we'll return the cached value.
            var engine4 = new RecalcEngine(config);
            engine4.EnableDelegation(dv.MaxRows);
            var result4 = await engine4.EvalAsync(loopupExpr, CancellationToken.None, runtimeConfig: dv.SymbolValues).ConfigureAwait(false);
            Assert.AreEqual(100.0, result4.ToObject());

            // Refresh connection cache.
            dv.RefreshCache();

            // Evals the same expression by a new engine. Sum should now return the refreshed value.
            var engine7 = new RecalcEngine(config);
            engine7.EnableDelegation(dv.MaxRows);
            var result7 = await engine7.EvalAsync(loopupExpr, CancellationToken.None, runtimeConfig: dv.SymbolValues).ConfigureAwait(false);
            Assert.IsInstanceOfType(result7, typeof(ErrorValue));

            ErrorValue ev7 = (ErrorValue)result7;
            Assert.AreEqual("Error attempting Entity lookup. Entity local:00000000-0000-0000-0000-000000000001 not found", ev7.Errors[0].Message);
        }

        [DataTestMethod]
        [DataRow("Collect(t1, {Int:Date(2023,2,27)})")]
        [DataRow("Collect(t1, {Int:Date(1889,12,31)})")]
        [DataRow("Collect(t1, {Int:Date(1,1,1)})")]
        [DataRow("With({new_number: Date(2023,2,27)}, Collect(t1, {Int:new_number}))")]
        public async Task DateNumberCoercionTest(string expr)
        {
            // create table "local"
            var logicalName = "allattributes";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForAllAttributeModel();
            dv.AddTable(displayName, logicalName);

            var engine = new RecalcEngine();

            engine.Config.SymbolTable.EnableMutationFunctions();

            var opts = _parserAllowSideEffects;
            var check = engine.Check(expr, symbolTable: dv.Symbols, options: opts);

            // Coercion in Collect() now allowed. Will coerce number/date. 
            Assert.IsTrue(check.IsSuccess);
        }

        [DataTestMethod]
        [DataRow("Collect(t1,{ DoesNotExist: 10})")]
        public async Task NullReferenceExceptionTest(string expr)
        {
            // create table "local"
            var logicalName = "allattributes";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForAllAttributeModel();
            dv.AddTable(displayName, logicalName);

            var engine = new RecalcEngine();

            engine.Config.SymbolTable.EnableMutationFunctions();

            var opts = _parserAllowSideEffects;
            var check = engine.Check(expr, symbolTable: dv.Symbols, options: opts);

            Assert.IsFalse(check.IsSuccess);
            Assert.IsTrue(check.Errors.First().Message.Contains("The specified column 'DoesNotExist' does not exist."));
        }

        [DataTestMethod]
        [DataRow("LookUp(t1, localid = GUID(\"00000000-0000-0000-9999-000000000001\"))")]
        public async Task LookUpMissingEntityReturnsBlank(string expr)
        {
            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable("t1", "local");

            var engine = new RecalcEngine();

            var opts = _parserAllowSideEffects;
            var check = engine.Check(expr, symbolTable: dv.Symbols, options: opts);
            FormulaValue result = await check.GetEvaluator().EvalAsync(CancellationToken.None, dv.SymbolValues).ConfigureAwait(false);

            // Failed lookup is blank
            Assert.IsNotNull(result as BlankValue);
        }

        [TestMethod]
        public async Task AllNotSupportedAttributesTest()
        {
            var baseExpr = "First(t1).{0}";
            var engine = new RecalcEngine();

            // Create new org (symbols) with both tables 
            (DataverseConnection dv, EntityLookup el) = CreateMemoryForAllAttributeModel();
            dv.AddTable("t1", "allattributes");

            var entity = el.RetrieveAsync("allattributes", _g1).Result.Response;

            // Hyperlink is a known type but not supported.
            var expectedErrors = new List<string>()
            {
                "Hyperlink column type not supported.",
                "Image column type not supported.",
                "File column type not supported.",
            };

            try
            {
                foreach (var attr in entity.Attributes)
                {
                    var expr = string.Format(baseExpr, attr.Key);
                    var result = engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues).Result;

                    if (result is ErrorValue errorValue)
                    {
                        Assert.IsTrue(expectedErrors.Contains(errorValue.Errors.First().Message));
                    }
                    else
                    {
                        Assert.IsNotInstanceOfType(result, typeof(BlankValue));
                    }
                }
            }
            catch (Exception ex)
            {
                Assert.IsTrue(false, ex.Message);
            }
        }

        [TestMethod]
        public void RetrieveAsyncErrorTst()
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";
            var errorMessage = "Something wrong happened when retrieving entity from server, after update.";
            var expr = "Patch(t1, First(t1), {Price:111})";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();

            dv.AddTable(displayName, logicalName);

            // Inject error when retrieving.
            el._getCustomErrorMessage = () => errorMessage;

            var opts = _parserAllowSideEffects;
            var engine = new RecalcEngine(new PowerFxConfig());

            engine.Config.SymbolTable.EnableMutationFunctions();

            var check = engine.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.IsTrue(check.IsSuccess);

            var run = check.GetEvaluator();
            var result = run.EvalAsync(CancellationToken.None, dv.SymbolValues).Result;

            Assert.IsTrue(((ErrorValue)result).Errors.First().Message.Contains(errorMessage));
        }

        [DataTestMethod]
        [DataRow("Set(x, First(t1))")]
        [DataRow("Set(t, Filter(t1,true))", true)]
        [DataRow("With({local:First(t1)}, Set(y, local))")]
        [DataRow("Set(x, First(Remote));Other.data")]
        [DataRow("Set(x, Collect(Remote, { Data : 99})); Other.Data")]
        [DataRow("With({r:First(t1)}, Set(x, { Price : r.Price, OtherData : r.Other.Data}))", false, true)]
        public void SetExpandableTypeNotAllowedTest(string expr, bool isTable = false, bool successful = false)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";
            var errorMessageKey = string.Empty;

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();

            dv.AddTable(displayName, logicalName);
            dv.AddTable("Remote", "remote");

            var opts = _parserAllowSideEffects;
            var engine = new RecalcEngine(new PowerFxConfig());

            engine.Config.SymbolTable.EnableMutationFunctions();
            engine.UpdateVariable("x", RecordValue.Empty());
            engine.UpdateVariable("y", RecordValue.Empty());
            engine.UpdateVariable("t", TableValue.NewTable(RecordType.Empty()));

            var check = engine.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.AreEqual(successful, check.IsSuccess);

            if (!successful)
            {
                if (isTable)
                {
                    errorMessageKey = "ErrSetVariableWithRelationshipNotAllowTable";
                }
                else
                {
                    errorMessageKey = "ErrSetVariableWithRelationshipNotAllowRecord";
                }

                Assert.IsTrue(check.Errors.Any(err => err.MessageKey == errorMessageKey));
            }
        }

        static readonly Guid _g1 = new Guid("00000000-0000-0000-0000-000000000001");
        static readonly Guid _g2 = new Guid("00000000-0000-0000-0000-000000000002");
        static readonly Guid _g3 = new Guid("00000000-0000-0000-0000-000000000003");
        static readonly Guid _g4 = new Guid("00000000-0000-0000-0000-000000000004");

        // static readonly EntityMetadata _localMetadata = DataverseTests.LocalModel.ToXrm();
        // static readonly EntityMetadata _remoteMetadata = DataverseTests.RemoteModel.ToXrm();

        static readonly EntityReference _eRef1 = new EntityReference("local", _g1);

        private (DataverseConnection, DataverseEntityCache, EntityLookup) CreateMemoryForRelationshipModelsWithCache(Policy policy = null, bool numberIsFloat = false)
        {
            (DataverseConnection dv, IDataverseServices ds, EntityLookup el) = CreateMemoryForRelationshipModelsInternal(policy, true, numberIsFloat: numberIsFloat);
            return (dv, ((DataverseEntityCache)ds), el);
        }

        private (DataverseConnection, EntityLookup) CreateMemoryForRelationshipModels(Policy policy = null, bool numberIsFloat = false)
        {
            (DataverseConnection dv, IDataverseServices _, EntityLookup el) = CreateMemoryForRelationshipModelsInternal(policy, numberIsFloat: numberIsFloat);
            return (dv, el);
        }

        // Create Entity objects to match DataverseTests.RelationshipModels;
        internal static (DataverseConnection, IDataverseServices, EntityLookup) CreateMemoryForRelationshipModelsInternal(Policy policy = null, bool cache = false, bool numberIsFloat = false)
        {
            var entity1 = new Entity("local", _g1);
            var entity2 = new Entity("remote", _g2);


            var entity3 = new Entity("local", _g3);
            entity3.Attributes["new_price"] = Convert.ToDecimal(10);

            var entity4 = new Entity("local", _g4);
            entity4.Attributes["new_price"] = Convert.ToDecimal(-10);

            entity1.Attributes["new_price"] = Convert.ToDecimal(100);
            entity1.Attributes["old_price"] = Convert.ToDecimal(200);
            entity1.Attributes["new_date"] = new DateTime(2023, 6, 1);
            entity1.Attributes["new_datetime"] = new DateTime(2023, 6, 1, 12, 0, 0);
            entity1.Attributes["new_currency"] = new Money(100);
            
            // Dataverse BigInt is a C# long type
            entity1.Attributes["new_bigint"] = Convert.ToInt64(long.MaxValue);

            // IR for field access for Relationship will generate the relationship name ("refg"), from ReferencingEntityNavigationPropertyName.
            // DataverseRecordValue has to decode these at runtime to match back to real field.
            entity1.Attributes["otherid"] = entity2.ToEntityReference();
            entity1.Attributes["rating"] = new Xrm.Sdk.OptionSetValue(2); // Warm

            // entity1.new_quantity is intentionally blank. 

            entity2.Attributes["data"] = Convert.ToDecimal(200);

            MockXrmMetadataProvider xrmMetadataProvider = new MockXrmMetadataProvider(DataverseTests.RelationshipModels);
            EntityLookup entityLookup = new EntityLookup(xrmMetadataProvider);
            entityLookup.Add(CancellationToken.None, entity1, entity2, entity3, entity4);
            IDataverseServices ds = cache ? new DataverseEntityCache(entityLookup) : entityLookup;

            CdsEntityMetadataProvider metadataCache = policy is SingleOrgPolicy policy2
                ? new CdsEntityMetadataProvider(xrmMetadataProvider, policy2.AllTables) { NumberIsFloat = numberIsFloat }
                : new CdsEntityMetadataProvider(xrmMetadataProvider) { NumberIsFloat = numberIsFloat };

            var dvConnection = new DataverseConnection(policy, ds, metadataCache, maxRows: 999);
            return (dvConnection, ds, entityLookup);
        }

        // Create Entity objects to match DataverseTests.AllAttributeModel;
        private (DataverseConnection, EntityLookup) CreateMemoryForAllAttributeModel(Policy policy = null, bool metadataNumberIsFloat = true)
        {
            var entity1 = new Entity("allattributes", _g1);

            entity1.Attributes["money"] = new Money(123);
            entity1.Attributes["hyperlink"] = "teste_url";
            entity1.Attributes["email"] = "joe@doe.com";
            entity1.Attributes["Memo"] = "lorem\nipsum";
            entity1.Attributes["boolean"] = new Xrm.Sdk.OptionSetValue() { Value = 1 };
            entity1.Attributes["image"] = "/Image/download.aspx?Entity=cr100_pfxcolumn&Attribute=cr100_aaimage2&Id=a2538543-c1cc-ed11-b594-0022482a3eb0&Timestamp=638169207737754720";
            entity1.Attributes["bigint"] = long.MaxValue; // 9223372036854775807
            entity1.Attributes["double"] = 1d / 3d;
            entity1.Attributes["new_field"] = 1m / 3m;

            MockXrmMetadataProvider xrmMetadataProvider = new MockXrmMetadataProvider(DataverseTests.AllAttributeModels);
            EntityLookup entityLookup = new EntityLookup(xrmMetadataProvider);

            entityLookup.Add(CancellationToken.None, entity1);

            CdsEntityMetadataProvider metadataCache;
            if (policy is SingleOrgPolicy policy2)
            {
                metadataCache = new CdsEntityMetadataProvider(xrmMetadataProvider, policy2.AllTables)
                {
                    NumberIsFloat = metadataNumberIsFloat
                };
            }
            else
            {
                metadataCache = new CdsEntityMetadataProvider(xrmMetadataProvider)
                {
                    NumberIsFloat = metadataNumberIsFloat
                };
            }

            var dvConnection = new DataverseConnection(policy, entityLookup, metadataCache);

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

    public static class Helpers
    {
        public static double ToDouble(this FormulaValue value)
        {
            if (value is NumberValue num)
            {
                return num.Value;
            }
            if (value is DecimalValue dec)
            {
                return (double)dec.Value;
            }
            throw new InvalidOperationException($"Not a number: {value.GetType().FullName}");
        }

        public static decimal ToDecimal(this FormulaValue value)
        {
            if (value is NumberValue num)
            {
                return (decimal)num.Value;
            }
            if (value is DecimalValue dec)
            {
                return dec.Value;
            }
            throw new InvalidOperationException($"Not a decimal: {value.GetType().FullName}");
        }

        // .Net won't allow us to encode Decimal in a Theory/InlineData attribute.  So we use double.
        // So we need a n easy conversion to double. 
        public static object ToDoubleOrObject(this FormulaValue value)
        {
            if (value is DecimalValue dec)
            {
                return (double)dec.Value;
            }
            return value.ToObject();
        }
    }
}
