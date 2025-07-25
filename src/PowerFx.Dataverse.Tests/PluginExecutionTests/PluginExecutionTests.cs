﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.ServiceModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Dataverse.EntityMock;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    // Excercise runing dataverse compiler from a plugin
    // In a plugin:
    // - use PowerFx2SqlEngine to convert EntityMetadata into RecordType and generate IR.
    // - wrap Entity as RecordValue (via XrmRecordValue)
    // - exceute the IR via the interpreter (rather than compiling to SQL).

    public class PluginExecutionTests
    {
        internal static readonly Guid _g1 = new Guid("00000000-0000-0000-0000-000000000001");

        internal static readonly Guid _g2 = new Guid("00000000-0000-0000-0000-000000000002");

        internal static readonly Guid _g3 = new Guid("00000000-0000-0000-0000-000000000003");

        internal static readonly Guid _g4 = new Guid("00000000-0000-0000-0000-000000000004");

        internal static readonly Guid _g5 = new Guid("00000000-0000-0000-0000-000000000005");

        internal static readonly Guid _g6 = new Guid("00000000-0000-0000-0000-000000000006");

        internal static readonly Guid _g7 = new Guid("00000000-0000-0000-0000-000000000007");

        internal static readonly Guid _g8 = new Guid("00000000-0000-0000-0000-000000000008");

        internal static readonly Guid _g9 = new Guid("00000000-0000-0000-0000-000000000009");

        internal static readonly Guid _g10 = new Guid("00000000-0000-0000-0000-00000000000A");

        internal static readonly Guid _g11 = new Guid("00000000-0000-0000-0000-00000000000B");

        internal static readonly Guid _g12 = new Guid("00000000-0000-0000-0000-00000000000C");

        internal static readonly ParserOptions _parserAllowSideEffects = new ParserOptions
        {
            AllowsSideEffects = true
        };

        internal static readonly ParserOptions _parserAllowSideEffects_NumberIsFloat = new ParserOptions
        {
            AllowsSideEffects = true,
            NumberIsFloat = true // testcases using this ParserOptions are run with NumberIsFloat as true
        };

        internal readonly EntityMetadataModel _trivialModel = new EntityMetadataModel
        {
            Attributes = new AttributeMetadataModel[]
            {
                new AttributeMetadataModel
                {
                    LogicalName = "new_field",
                    DisplayName = "field",
                    AttributeType = AttributeTypeCode.Decimal
                },
            }
        };

        // Verify we can convert EntityMetadata to RecordType
        [Fact]
        public void ConvertMetadata()
        {
            var rawProvider = new MockXrmMetadataProvider(_trivialModel);
            var provider = new CdsEntityMetadataProvider(rawProvider);  // NumberIsFloat = false

            var recordType = provider.GetRecordType(_trivialModel.LogicalName);

            Assert.Equal("![new_field:w]", recordType._type.ToString());

            var field = recordType.GetFieldTypes().First();
            Assert.Equal("new_field", field.Name);
            Assert.Equal(FormulaType.Decimal, field.Type);

            // $$$ Fails?
            // Assert.Equal("field", field.DisplayName);
        }

        // Verify we can convert EntityMetadata to RecordType
        [Fact]
        public void ConvertMetadataFloat()
        {
            var rawProvider = new MockXrmMetadataProvider(_trivialModel);
            var provider = new CdsEntityMetadataProvider(rawProvider) { NumberIsFloat = true };

            var recordType = provider.GetRecordType(_trivialModel.LogicalName);

            Assert.Equal("![new_field:n]", recordType._type.ToString());

            var field = recordType.GetFieldTypes().First();
            Assert.Equal("new_field", field.Name);
            Assert.Equal(FormulaType.Number, field.Type);

            // $$$ Fails?
            // Assert.Equal("field", field.DisplayName);
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
        [Fact]
        public void ConvertMetadataLazy()
        {
            var localName = MockModels.LocalModel.LogicalName;

            var rawProvider = new TrackingXrmMetadataProvider(new MockXrmMetadataProvider(MockModels.RelationshipModels));

            // Passing in a display dictionary avoids unecessary calls to to metadata lookup.
            var disp = new Dictionary<string, string>
            {
                { "local", "Locals" },
                { "remote", "Remotes" }
            };

            var provider = new CdsEntityMetadataProvider(rawProvider, disp);  // NumberIsFloat = false

            // Shouldn't access any other metadata (via relationships).
            var reqs = rawProvider._requests;

            var recordType1 = provider.GetRecordType(localName);

            // Should not have requested anything else.
            Assert.Single(reqs);
            Assert.Equal("local", reqs[0]);

            reqs.Clear();

            // 2nd attempt hits the cache
            provider.GetRecordType(localName);
            Assert.Empty(reqs);

            // Now accessing Remote succeeds.
            var recordType2 = (RecordType)recordType1.GetFieldType("refg"); // name for "Other" field.
            Assert.Single(reqs);
            Assert.Equal("remote", reqs[0]);

            var field2 = recordType2.GetFieldType("data"); // number
            Assert.IsType<DecimalType>(field2);
        }

        // Verify we can convert EntityMetadata to RecordType
        [Fact]
        public void ConvertMetadataLazyFloat()
        {
            var localName = MockModels.LocalModel.LogicalName;

            var rawProvider = new TrackingXrmMetadataProvider(new MockXrmMetadataProvider(MockModels.RelationshipModels));

            // Passing in a display dictionary avoids unecessary calls to to metadata lookup.
            var disp = new Dictionary<string, string>
            {
                { "local", "Locals" },
                { "remote", "Remotes" }
            };

            var provider = new CdsEntityMetadataProvider(rawProvider, disp) { NumberIsFloat = true };

            // Shouldn't access any other metadata (via relationships).
            var reqs = rawProvider._requests;

            var recordType1 = provider.GetRecordType(localName);

            // Should not have requested anything else.
            Assert.Single(reqs);
            Assert.Equal("local", reqs[0]);

            reqs.Clear();

            // 2nd attempt hits the cache
            provider.GetRecordType(localName);
            Assert.Empty(reqs);

            // Now accessing Remote succeeds.
            var recordType2 = (RecordType)recordType1.GetFieldType("refg"); // name for "Other" field.
            Assert.Single(reqs);
            Assert.Equal("remote", reqs[0]);

            var field2 = recordType2.GetFieldType("data"); // number
            Assert.IsType<NumberType>(field2);
        }

        // Lookup missing field
        [Fact]
        public void MetadataChecksMissingField()
        {
            var metadata = _trivialModel.ToXrm();
            var ok = metadata.TryGetRelationship("missing", out var attr);
            Assert.False(ok);
            Assert.Null(attr);

            ok = metadata.TryGetAttribute("missing", out var amd);
            Assert.False(ok);
            Assert.Null(amd);
        }

        [Fact]
        public void ModelArrayToMetadataArrayTest()
        {
            var xrmArray = ModelExtensions.ToXrm(MockModels.RelationshipModels);

            foreach (var metadata in xrmArray)
            {
                Assert.IsType<EntityMetadata>(metadata);
            }
        }

        [Fact]
        public void MetadataChecks()
        {
            var localName = MockModels.LocalModel.LogicalName;

            var rawProvider = new TrackingXrmMetadataProvider(new MockXrmMetadataProvider(MockModels.RelationshipModels));

            // Passing in a display dictionary avoids unecessary calls to to metadata lookup.
            var disp = new Dictionary<string, string>
            {
                { "local", "Locals" },
                { "remote", "Remotes" }
            };

            var provider = new CdsEntityMetadataProvider(rawProvider, disp);
            var ok = provider.TryGetXrmEntityMetadata(localName, out var entityMetadata);
            Assert.True(ok);
            Assert.NotNull(entityMetadata);

            // Lookup non-relationship field
            var logicalName = "new_price";
            var displayName = "Price";
            ok = entityMetadata.TryGetAttribute(logicalName, out var amd);
            Assert.True(ok);
            Assert.Equal(amd.LogicalName, logicalName);

            ok = entityMetadata.TryGetAttribute(logicalName.ToUpper(), out amd);
            Assert.False(ok, "case sensitive lookup");
            Assert.Null(amd);

            ok = entityMetadata.TryGetAttribute(displayName, out amd);
            Assert.Null(amd);
            Assert.False(ok, "only logical names, Not display names");

            // Relationships.
            // "refg" is the relationship for "otherid" attribute.
            ok = entityMetadata.TryGetAttribute("otherid", out _);
            Assert.True(ok);

            ok = entityMetadata.TryGetRelationship("otherid", out var relationshipName);
            Assert.False(ok, "Attribute is not a relationship");
            Assert.Null(relationshipName);

            ok = entityMetadata.TryGetRelationship("refg", out relationshipName);
            Assert.True(ok);
            Assert.Equal("otherid", relationshipName);

            ok = entityMetadata.TryGetRelationship("Refg", out relationshipName);
            Assert.False(ok, "not case sensitive");
            Assert.Null(relationshipName);
        }

        [Fact]
        public void Check()
        {
            var rawProvider = new MockXrmMetadataProvider(_trivialModel);
            var provider = new CdsEntityMetadataProvider(rawProvider);

            var metadata = _trivialModel.ToXrm();
            var engine = new DataverseEngine(metadata, provider, new PowerFxConfig());

            var check = engine.Check("1 + ThisRecord.field");

            Assert.True(check.IsSuccess);
            Assert.Equal(FormulaType.Decimal, check.ReturnType);

            Assert.NotNull(check.Binding);
        }

        // Ensure a custom function shows up in intellisense.
        [Fact]
        public void IntellisenseWithCustomFuncs()
        {
            var config = new PowerFxConfig();
            config.AddFunction(new DoubleItFunction());
            var engine = new DataverseEngine(null, null, config);

            var results = engine.Suggest("DoubleI", 7);
            var list = results.Suggestions.ToArray();
            Assert.Single(list);

            var x = list[0];
            Assert.Equal(SuggestionKind.Function, x.Kind);
            Assert.Equal("DoubleIt", x.DisplayText.Text);
        }

        // Verify we can exceute an IR from the Sql compiler,
        // and add custom functions.
        [Fact]
        public void CompileBasic()
        {
            var rawProvider = new MockXrmMetadataProvider(_trivialModel);
            var provider = new CdsEntityMetadataProvider(rawProvider);

            var metadata = _trivialModel.ToXrm();

            var config = new PowerFxConfig();
            config.AddFunction(new DoubleItFunction());
            var engine = new DataverseEngine(metadata, provider, config);

            var check = engine.Check("DoubleIt(ThisRecord.field) + 10");

            Assert.True(check.IsSuccess);
            Assert.Equal(FormulaType.Number, check.ReturnType);
        }

        // $$$ OptionSets don't work:
        // Expression:   First(t1).Rating
        //  actual :      local_rating_optionSet.2     !!!
        //  expected: 'Rating (Locals)'.Warm     // but this won't parse, needs metadata.

        [Theory]
        [InlineData("First(t1).Price", "Decimal(100)")] // trivial
        [InlineData("t1", "t1")] // table
        [InlineData("First(t1)", "LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\"))")] // record
        [InlineData("LookUp(t1, false)", "If(false,First(FirstN(t1,0)))")] // blank
        [InlineData("First(t1).LocalId", "GUID(\"00000000-0000-0000-0000-000000000001\")")] // Guid
        public async Task TestSerialize(string expr, string expectedSerialized)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup _) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            var engine = new RecalcEngine();
            engine.EnableDelegation();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues);

            // Test the serializer!
            var serialized = result.ToExpression();

            Assert.Equal(expectedSerialized, serialized);

            // Deserialize.
            _ = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues);
        }

        [Theory]
        [InlineData("First(t1).Price", "Float(100)")] // trivial
        [InlineData("t1", "t1")] // table
        [InlineData("First(t1)", "LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\"))")] // record
        [InlineData("LookUp(t1, false)", "If(false,First(FirstN(t1,0)))")] // blank
        [InlineData("First(t1).LocalId", "GUID(\"00000000-0000-0000-0000-000000000001\")")] // Guid
        public async Task TestSerializeFloat(string expr, string expectedSerialized)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup _) = CreateMemoryForRelationshipModels(numberIsFloat: true);
            dv.AddTable(displayName, logicalName);

            var engine = new RecalcEngine();
            engine.EnableDelegation();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues);

            // Test the serializer!
            var serialized = result.ToExpression();

            Assert.Equal(expectedSerialized, serialized);

            // Deserialize.
            _ = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues);
        }

        [Fact]
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
            Assert.Equal("LookUp(t1, localid=GUID(\"" + id + "\"))", expr);

            // Deserialize
            var engine = new RecalcEngine();
            engine.EnableDelegation();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues);

            var entity = (Entity)result.ToObject();
            Assert.NotNull(entity); // such as if Lookup() failed and we got blank

            Assert.Equal(entityOriginal.LogicalName, entity.LogicalName);
            Assert.Equal(entityOriginal.Id, entity.Id);
        }

        [Fact]
        public async Task TestSerializeBasic2()
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            var id = "00000000-0000-0000-0000-000000000001";
            var entityOriginal = el.LookupRef(new EntityReference(logicalName, Guid.Parse(id)), CancellationToken.None);
            RecordValue record = await dv.RetrieveAsync(logicalName, Guid.Parse(id), columns: null) as RecordValue;

            // Test the serializer!
            var expr = record.ToExpression();

            // Should be short form - not flattened.
            Assert.Equal("LookUp(t1, localid=GUID(\"" + id + "\"))", expr);

            // Deserialize
            var engine = new RecalcEngine();
            engine.EnableDelegation();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues);

            var entity = (Entity)result.ToObject();
            Assert.NotNull(entity); // such as if Lookup() failed and we got blank

            Assert.Equal(entityOriginal.LogicalName, entity.LogicalName);
            Assert.Equal(entityOriginal.Id, entity.Id);
        }

        [Fact]
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
            Assert.Equal("LookUp(t1, localid=GUID(\"" + id + "\"))", expr);

            // Deserialize
            var engine = new RecalcEngine();
            engine.EnableDelegation();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues);

            var entity = (Entity)result.ToObject();
            Assert.NotNull(entity); // such as if Lookup() failed and we got blank

            Assert.Equal(entityOriginal.LogicalName, entity.LogicalName);
            Assert.Equal(entityOriginal.Id, entity.Id);
        }

        [Fact]
        public async Task TestSerializeBlank()
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup _) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            RecordType type = dv.GetRecordType(logicalName);
            var blank = FormulaValue.NewBlank(type);

            // Test the serializer!
            var expr = blank.ToExpression();

            // Should be short form - not flattened.
            Assert.Equal("If(false,First(FirstN(t1,0)))", expr);

            // Deserialize
            var engine = new RecalcEngine();
            engine.EnableDelegation();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues);

            var entity = (Entity)result.ToObject();
            Assert.Null(entity);
        }

        // Serialize the entire table
        [Fact]
        public async Task TestSerializeTable()
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup _) = CreateMemoryForRelationshipModels();
            TableValue table = dv.AddTable(displayName, logicalName);

            // Test the serializer!
            var expr = table.ToExpression();

            // Should be short form - not Table() or some other literal.
            Assert.Equal("t1", expr);

            // Deserialize
            var engine = new RecalcEngine();
            engine.EnableDelegation();
            var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues);

            Assert.IsType<DataverseTableValue>(result);
        }

        // Serialize the entire table
        [Fact]
        public void TestSerializeRelationship()
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            var logicalName2 = "remote";
            var displayName2 = "t2";

            (DataverseConnection dv, EntityLookup _) = CreateMemoryForRelationshipModels();
            _ = dv.AddTable(displayName, logicalName);

            var engine = new RecalcEngine();
            engine.EnableDelegation();
            Func<string, DataverseConnection, FormulaValue> eval =
                (expr, dv) => engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues).Result;

            // Relationship refers to a type that we didn't AddTable for.
            var result = eval("First(t1).Other", dv); // reference to Remote (t2)

            var expr1Serialized = result.ToExpression();
            Assert.Equal("LookUp(remote, remoteid=GUID(\"00000000-0000-0000-0000-000000000002\"))", expr1Serialized);

            // Now add the table.
            _ = dv.AddTable(displayName2, logicalName2);

            // Reserializing will fetch from the connection and use the updated name.
            var expr1bSerialized = result.ToExpression();
            Assert.Equal("LookUp(t2, remoteid=GUID(\"00000000-0000-0000-0000-000000000002\"))", expr1bSerialized);

            // Compare relationship to direct lookup
            var expr3 = "First(t2)";
            var result3 = eval(expr3, dv);
            var expr3Serialized = result3.ToExpression();

            Assert.Equal("LookUp(t2, remoteid=GUID(\"00000000-0000-0000-0000-000000000002\"))", expr3Serialized);
        }

        // Run with 2 tables registered.
        // Deimcal values can't be used in test DataRow, converted to decimal at the test
        [Theory]
        [InlineData("First(t1).Other.Data", 200.0)]
        [InlineData("First(t2).Data", 200.0)]
        [InlineData("First(t1).Other.remoteid = First(t2).remoteid", true)] // same Id
        [InlineData("If(true, First(t1).Other, First(t2)).Data", 200.0)] // Compatible for comparison
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
            (DataverseConnection dv2, EntityLookup _) = CreateMemoryForRelationshipModels();  // numberIsFloat: false
            dv2.AddTable(displayName, logicalName);
            dv2.AddTable(displayName2, logicalName2);

            var result = eval(expr, dv2);
            Assert.Equal(expected is double exp ? new decimal(exp) : expected, result.ToObject());
        }

        // Run with 2 tables registered.
        [Theory]
        [InlineData("First(t1).Other.Data", 200.0)]
        [InlineData("First(t2).Data", 200.0)]
        [InlineData("First(t1).Other.remoteid = First(t2).remoteid", true)] // same Id
        [InlineData("If(true, First(t1).Other, First(t2)).Data", 200.0)] // Compatible for comparison
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
            (DataverseConnection dv2, EntityLookup _) = CreateMemoryForRelationshipModels(numberIsFloat: true);
            dv2.AddTable(displayName, logicalName);
            dv2.AddTable(displayName2, logicalName2);

            var result = eval(expr, dv2);
            Assert.Equal(expected, result.ToObject());
        }

        // Run with 2 tables registered.
        [Theory]
        [InlineData("First(t1).money", 123.0)]
        [InlineData("With({x:First(t1).money, y:First(t1)}, x + y.money)", 246.0)]
        [InlineData("With({x:Collect(t1,{money:40})}, x.money + First(t1).money)", 163.0)]
        [InlineData("Patch(t1, First(t1), {money:321});First(t1).money", 321.0)]
        public async Task ExtractPrimitiveValueTest(string expr, object expected)
        {
            // create table "local"
            var logicalName = "allattributes";
            var displayName = "t1";

            var engine = new RecalcEngine();
            engine.EnableDelegation();

            // Create new org (symbols) with both tables
            (DataverseConnection dv, EntityLookup _) = CreateMemoryForAllAttributeModel();
            dv.AddTable(displayName, logicalName);

            engine.Config.SymbolTable.EnableMutationFunctions();

            var opts = _parserAllowSideEffects;
            var check = engine.Check(expr, symbolTable: dv.Symbols, options: opts);

            var run = check.GetEvaluator();
            var result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.Equal(expected, result.ToDouble());
        }

        [Theory]
        [InlineData(true, "Number")]
        [InlineData(false, "Decimal")]
        public void TestMoney(bool numberIsFloat, string retTypeStr)
        {
            // create table "local"
            var logicalName = "allattributes";
            var displayName = "t1";

            var engine = new RecalcEngine();
            engine.EnableDelegation();

            // numberIsFloat controls how metadata parser handles currency.
            (DataverseConnection dv, EntityLookup _) = CreateMemoryForAllAttributeModel(metadataNumberIsFloat: numberIsFloat);
            dv.AddTable(displayName, logicalName);

            var expr = "First(t1).money"; // field of Currency type

            var check = engine.Check(expr, symbolTable: dv.Symbols);
            Assert.True(check.IsSuccess);
            var retType = check.ReturnType.ToString();
            Assert.Equal(retTypeStr, retType);
        }

        [Theory]
        [InlineData("Patch(t1, First(t1), {Memo:\"LOREM\nIPSUM\nDOLOR\nSIT\nAMET\"})")]
        [InlineData("First(t1).Memo")]
        public async Task SupportAllColumnTypesTest(string expr)
        {
            // create table "local"
            var logicalName = "allattributes";
            var displayName = "t1";

            var engine = new RecalcEngine();
            engine.EnableDelegation();

            // Create new org (symbols) with both tables
            (DataverseConnection dv, EntityLookup _) = CreateMemoryForAllAttributeModel();
            dv.AddTable(displayName, logicalName);

            engine.Config.SymbolTable.EnableMutationFunctions();

            var opts = _parserAllowSideEffects;
            var check = engine.Check(expr, symbolTable: dv.Symbols, options: opts);

            var run = check.GetEvaluator();
            var result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.IsNotType<ErrorValue>(result);
        }

        [Theory]
        [InlineData("If(First(t1).boolean, \"YES\", \"NO\")", "YES")]
        [InlineData("If(First(t1).boolean = allattributes_boolean_optionSet.'1', \"YES\", \"NO\")", "YES")]
        [InlineData("If(First(t1).boolean = 'Boolean (All Attributes)'.'1', \"YES\", \"NO\")", "YES")]
        [InlineData("Text(First(t1).boolean)", "Yes")]
        [InlineData("Text(First(t1).boolean) & \"Maybe\"", "YesMaybe")]
        [InlineData("Patch(t1, First(t1), {boolean:allattributes_boolean_optionSet.'0',email:\"dummy@email.com\"});First(t1).email", "dummy@email.com")]
        [InlineData("With({before: First(t1).boolean}, Patch(t1, First(t1), {boolean:'Boolean (All Attributes)'.'0'});If(First(t1).boolean <> before, \"good\", \"bad\"))", "good")]
        [InlineData("Collect(t1, {boolean:allattributes_boolean_optionSet.'1',email:\"dummy1@email.com\"});LookUp(t1, email = \"dummy1@email.com\").email", "dummy1@email.com")]
        [InlineData("Collect(t1, {boolean:allattributes_boolean_optionSet.'1',email:\"dummy2@email.com\"});If(LookUp(t1, email = \"dummy2@email.com\").boolean, \"Affirmitive\", \"Nope\")", "Affirmitive")]
        public async Task BooleanOptionSetCoercionTest(string expr, string expected)
        {
            // create table "local"
            var logicalName = "allattributes";
            var displayName = "t1";

            var engine = new RecalcEngine();
            engine.EnableDelegation();

            // Create new org (symbols) with both tables
            (DataverseConnection dv, EntityLookup _) = CreateMemoryForAllAttributeModel();
            dv.AddTable(displayName, logicalName);

            engine.Config.SymbolTable.EnableMutationFunctions();

            var opts = _parserAllowSideEffects;
            var check = engine.Check(expr, symbolTable: dv.Symbols, options: opts);

            var run = check.GetEvaluator();
            var result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.Equal(expected, result.ToObject());
        }

        [Theory]
        [InlineData("Patch(t1, First(t1), {boolean:true,email:\"dummy@email.com\"});First(t1).email")]
        [InlineData("With({before: First(t1).boolean}, Patch(t1, First(t1), {boolean:true});If(First(t1).boolean <> before, \"good\", \"bad\"))")]
        [InlineData("Collect(t1, {boolean:true,email:\"dummy1@email.com\"});LookUp(t1, email = \"dummy1@email.com\").email")]
        [InlineData("Collect(t1, {boolean:true,email:\"dummy2@email.com\"});If(LookUp(t1, email = \"dummy2@email.com\").boolean, \"Affirmitive\", \"Nope\")")]
        public void BooleanOptionSetCoercionNotAllowedTest(string expr)
        {
            // create table "local"
            var logicalName = "allattributes";
            var displayName = "t1";

            var engine = new RecalcEngine();
            engine.EnableDelegation();

            // Create new org (symbols) with both tables
            (DataverseConnection dv, EntityLookup _) = CreateMemoryForAllAttributeModel();
            dv.AddTable(displayName, logicalName);

            engine.Config.SymbolTable.EnableMutationFunctions();

            var opts = _parserAllowSideEffects;
            var check = engine.Check(expr, symbolTable: dv.Symbols, options: opts);

            Assert.False(check.IsSuccess);
        }

        // https://github.com/microsoft/Power-Fx-Dataverse/issues/102
        [Theory]
        [InlineData("Collect(t1, {Price:111, Other: LookUp(Remote, RemoteId = GUID(\"00000000-0000-0000-0000-000000000002\"))});Text(Last(t1).Price)", "111")]
        [InlineData("With({remote: LookUp(Remote, RemoteId = GUID(\"00000000-0000-0000-0000-000000000002\"))}, Collect(t1, {Price:111, Other: remote});Text(Last(t1).Price))", "111")]
        [InlineData("Patch(t1, LookUp(t1, LocalId = GUID(\"00000000-0000-0000-0000-000000000001\")), {Other: LookUp(Remote, RemoteId = GUID(\"00000000-0000-0000-0000-000000000002\")), Price: 222});Text(LookUp(t1, LocalId = GUID(\"00000000-0000-0000-0000-000000000001\")).Price)", "222")]
        [InlineData("With({local: LookUp(t1, LocalId = GUID(\"00000000-0000-0000-0000-000000000001\")), remote: LookUp(Remote, RemoteId = GUID(\"00000000-0000-0000-0000-000000000002\"))}, Patch(t1, local, {Other: remote, Price: 222});Text(LookUp(t1, LocalId = GUID(\"00000000-0000-0000-0000-000000000001\")).Price))", "222")]
        public async Task CardsRegressionRelationshipModelsTest(string expr, string expected)
        {
            var engine = new RecalcEngine();
            engine.EnableDelegation();

            // Create new org (symbols) with both tables
            (DataverseConnection dv, EntityLookup _) = CreateMemoryForRelationshipModels();
            dv.AddTable("t1", "local");
            dv.AddTable("Remote", "remote");

            engine.Config.SymbolTable.EnableMutationFunctions();

            var opts = _parserAllowSideEffects;
            var check = engine.Check(expr, symbolTable: dv.Symbols, options: opts);

            var run = check.GetEvaluator();
            var result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.Equal(expected, result.ToObject());
        }

        // Hyperlink types are imported as String
        [Theory]
        [InlineData("First(t1).hyperlink")]
        [InlineData("With({x:First(t1)}, x.hyperlink)")]
        public async Task HyperlinkIsString(string expr)
        {
            // create table "local"
            var logicalName = "allattributes";
            var displayName = "t1";

            var engine = new RecalcEngine();
            engine.EnableDelegation();

            // Create new org (symbols) with both tables
            (DataverseConnection dv, EntityLookup _) = CreateMemoryForAllAttributeModel();
            dv.AddTable(displayName, logicalName);

            engine.Config.SymbolTable.EnableMutationFunctions();

            var opts = _parserAllowSideEffects;
            var check = engine.Check(expr, symbolTable: dv.Symbols, options: opts);

            var run = check.GetEvaluator();
            var result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.IsType<StringValue>(result);
            Assert.Equal(FormulaType.String, result.Type);

            Assert.Equal("teste_url", result.ToObject());
        }

        // Ensure a custom function shows up in intellisense.
        [Fact]
        public void IntellisenseWithWholeOrgPolicy()
        {
            // Everything policy
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "xxxt1"); // unique display name
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv2, EntityLookup _) = CreateMemoryForRelationshipModels(policy);

            var engine = new RecalcEngine();
            engine.EnableDelegation();

            // Intellisense doesn't return anything on pure empty,
            // needs at least first char of identifier.

            var check = engine.Check("xx", symbolTable: dv2.Symbols);

            var results = engine.Suggest(check, 2);
            var list = results.Suggestions.ToArray();

            Assert.Single(list);
            Assert.Equal("xxxt1", list[0].DisplayText.Text);

            // Triggers a lazy load
            var check2 = engine.Check("First(xxxt1)", symbolTable: dv2.Symbols);
            Assert.True(check2.IsSuccess);

            // After lazily load, now the symbol table is populated and we see the symbols.
            var results2 = engine.Suggest(check, 2);
            var list2 = results2.Suggestions.ToArray();

            Assert.Single(list2);
            Assert.Equal("xxxt1", list2[0].DisplayText.Text);
        }

        [Fact]
        public void SingleOrgPolicyTest()
        {
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "Remote");
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, EntityLookup _) = CreateMemoryForRelationshipModels(policy);

            foreach (var name in new string[] { "local", "t1", "remote", "Remote" })
            {
                var ok = dv.Symbols.TryLookupSlot(name, out var s1);
                Assert.True(ok);
                Assert.NotNull(s1);
            }
        }

        // Ensure lazy loaded symbols are available on first use.
        [Fact]
        public async Task SingleOrgPolicyLazyEval()
        {
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "Remote");
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, EntityLookup _) = CreateMemoryForRelationshipModels(policy);

            var engine = new RecalcEngine();

            // Ensure first call gets correct answer.
            var result = await engine.EvalAsync("CountRows(local)", default, dv.SymbolValues);
            Assert.Equal(3.0, result.ToDouble());

            // 2nd call better be correct.
            var result2 = await engine.EvalAsync("CountRows(local)", default, dv.SymbolValues);
            Assert.Equal(3.0, result2.ToDouble());
        }

        // When using WholeOrg policy, we're using display names,
        // which are converted to invariant.
        [Theory]
        [InlineData("new_price + 10", "Price + 10")]
        [InlineData("ThisRecord.new_price + 10", "ThisRecord.Price + 10")]
        [InlineData("First(local).new_price", "First(t1).Price")]
        [InlineData("ThisRecord.refg.data", "ThisRecord.Other.Data")] // relationships
        [InlineData("First(remote).data", "First(Remote).Data")]
        [InlineData("Set(refg, First(remote));refg.data", "Set(Other, First(Remote));Other.Data")] // relationships
        [InlineData("new_price * new_quantity", "Price * Quantity", "new_price * Quantity")]
        public void WholeOrgConversions(string logical, string display, string mixed = null)
        {
            var logicalName = "local";

            // Everything policy
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "Remote");
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, EntityLookup _) = CreateMemoryForRelationshipModels(policy);
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
                Assert.True(check.IsSuccess);

                var invariant = check.ApplyGetInvariant();
                Assert.Equal(invariant, logical);

                // Get display
                var display2 = engine.GetDisplayExpression(expr, symbols);
                Assert.Equal(display, display2);
            }
        }

        [Theory]
        [InlineData("1+2", "")] // none
        [InlineData("ThisRecord.Price * Quantity", "Entity local: new_price, new_quantity;")] // basic read
        [InlineData("Price%", "Entity local: new_price;")] // unary op
        [InlineData("ThisRecord", "Entity local: ;")] // whole scope
        [InlineData("First(Remote).Data", "Entity remote: data;")] // other table

        // $$$ https://github.com/microsoft/Power-Fx/issues/1659
        //[InlineData("Set(Price, 200)", "Write local: new_price;")] // set,
        [InlineData("Set(Price, Quantity)", "Entity local: new_price, new_quantity;")] // set,
        [InlineData("Set(Price, Price + 1)", "Entity local: new_price;")] // set,
        [InlineData("ThisRecord.Other.Data", "Entity local: otherid; Entity remote: data;")] //relationship
        [InlineData("{x:5}.x", "")] // non dataverse record
        [InlineData("With({x : ThisRecord}, x.Price)", "Entity local: new_price;")] // alias
        [InlineData("With({Price : 5}, Price + Quantity)", "Entity local: new_quantity;")] // Price is shadowed
        [InlineData("With({Price : 5}, ThisRecord.Price)", "")] // shadowed
        [InlineData("LookUp(t1,Price=255)", "Entity local: new_price;")] // Lookup and RowScope
        [InlineData("Filter(t1,Price > 200)", "Entity local: new_price;")] // Lookup and RowScope
        [InlineData("First(t1)", "Entity local: ;")]
        [InlineData("Last(t1)", "Entity local: ;")]
        [InlineData("t1", "Entity local: ;")] // whole table
        [InlineData("12 & true & \"abc\" ", "")] // walker ignores literals
        [InlineData("12;Price;12", "Entity local: new_price;")] // chaining
        [InlineData("ParamLocal1.Price", "Entity local: new_price;")] // basic read
        [InlineData("First(t1).Price + First(Remote).'Other Other'.'Data Two'", "Entity local: new_price; Entity remote: otherotherid; Entity doubleremote: data2;")] // 3 entities
        [InlineData("Collect(t1, { Price : 200})", "Entity local: new_price;")] // collect , does not write to t1.
        [InlineData("Collect(t1,{ Other : First(Remote)})", "Entity local: otherid; Entity remote: ;")]
        [InlineData("Remove(t1,{ Other : First(Remote)})", "Entity local: otherid; Entity remote: ;")]
        [InlineData("ClearCollect(t1,{ Other : First(Remote)})", "Entity local: otherid; Entity remote: ;")]

        // polymorphic comparisons.
        [InlineData("Filter(t1, PolymorphicLookup <> First(Remote))", "Entity local: new_polyfield; Entity remote: ;")]
        [InlineData("LookUp(t1, PolymorphicLookup <> First(Remote))", "Entity local: new_polyfield; Entity remote: ;")]
        [InlineData("Filter(t1, AsType(PolymorphicLookup, Remote).Data = 200)", "Entity local: new_polyfield; Entity remote: data;")]
        [InlineData("LookUp(t1, AsType(PolymorphicLookup, Remote).Data = 200)", "Entity local: new_polyfield; Entity remote: data;")]
        [InlineData("Collect(t1, {PolymorphicLookup: First(Remote)}); AsType(Last(t1).PolymorphicLookup, Remote)", "Entity local: new_polyfield; Entity remote: ;")]
        [InlineData("AsType(LookUp(t1, false).PolymorphicLookup, Remote).Data", "Entity local: new_polyfield; Entity remote: data;")]

        // 1:N relationships, 1 Degree drilled.
        [InlineData("Filter(t1, virtual.'Virtual Data' = 10)", "Entity local: virtualid; Entity virtualremote: vdata;")]
        [InlineData("LookUp(t1, virtual.'Virtual Data' = 10)", "Entity local: virtualid; Entity virtualremote: vdata;")]

        // Inside with.
        [InlineData("With({r: t1}, Filter(r, Currency > 0))", "Entity local: new_currency;")]
        [InlineData("With({r: t1}, LookUp(r, Currency > 0))", "Entity local: new_currency;")]

        // Option set.
        [InlineData("Filter(t1, Rating <> 'Rating (Locals)'.Hot)", "Entity local: rating;")]
        [InlineData("LookUp(t1, Rating <> 'Rating (Locals)'.Hot)", "Entity local: rating;")]
        [InlineData("Filter(Distinct(ShowColumns(t1, 'new_quantity', 'old_price'), new_quantity), Value < 20)", "Entity local: new_quantity, old_price;")]
        [InlineData("Distinct(t1, Price)", "Entity local: new_price;")]
        [InlineData("Set(NewRecord.Price, 8)", "Entity local: new_price;")]

        // Summarize is special, becuase of ThisGroup.
        // Summarize that's delegated.
        [InlineData("Summarize(t1, Name, Sum(ThisGroup, Price) As TPrice)", "Entity local: new_name, new_price;")]

        // Summarize that's not delegated.
        [InlineData("Summarize(t1, Name, Sum(ThisGroup, Price * 2) As TPrice)", "Entity local: new_name, new_price;")]

        // Join
        [InlineData("Join(remote As l, local As r, l.remoteid = r.rtid, JoinType.Inner, r.new_name As other2)", "Entity remote: remoteid; Entity local: rtid, new_name;")]
        [InlineData("Join(local, remote, LeftRecord.new_price = RightRecord.data, JoinType.Inner, RightRecord.other As other)", "Entity local: new_price; Entity remote: data, other;")]
        public void GetDependencies(string expr, string expected)
        {
            var logicalName = "local";

            // Everything policy
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "Remote");
            map.Add("doubleremote", "Remote2");
            map.Add("virtualremote", "VRemote");
            var policy = new SingleOrgPolicy(map);

            var config = new PowerFxConfig();
            config.SymbolTable.EnableMutationFunctions();
            config.EnableJoinFunction();

            (DataverseConnection dv, EntityLookup _) = CreateMemoryForRelationshipModels(policy);

            // Simulate a parameter
            var parameterSymbols = new SymbolTable { DebugName = "Parameters " };
            parameterSymbols.AddVariable("ParamLocal1", dv.GetRecordType("local"), mutable: true);
            parameterSymbols.AddVariable("NewRecord", dv.GetRecordType("local"), new SymbolProperties() { CanMutate = false, CanSet = false, CanSetMutate = true });

            var rowScopeSymbols = dv.GetRowScopeSymbols(tableLogicalName: logicalName);
            var symbols = ReadOnlySymbolTable.Compose(rowScopeSymbols, dv.Symbols, parameterSymbols);

            // Without delegation transformation.
            var engine = new RecalcEngine(config);
            var check = engine.Check(expr, options: _parserAllowSideEffects, symbolTable: symbols);
            var info = check.ApplyDependencyInfoScan(dv.MetadataCache);
            var actual = info.ToString().Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();

            Assert.Equal<object>(expected, actual);

            // With delegation transformation.
            engine.EnableDelegation();
            check = engine.Check(expr, options: _parserAllowSideEffects, symbolTable: symbols);
            info = check.ApplyDependencyInfoScan(dv.MetadataCache);
            actual = info.ToString().Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();

            Assert.Equal<object>(expected, actual);
        }

        [Fact]
        public async Task RefreshDataverseConnectionSingleOrgPolicyTest()
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
            Assert.True(check1.IsSuccess);

            var run1 = check1.GetEvaluator();
            var result1 = await run1.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.Equal(100m, result1.ToObject());

            // Simulates a row being deleted by an external user
            await el.DeleteAsync(logicalName, _g1);
            await el.DeleteAsync(logicalName, _g3);
            await el.DeleteAsync(logicalName, _g4);

            // Evals the same expression by a new engine. Should return a wrong result.
            var engine2 = new RecalcEngine(config);
            engine2.EnableDelegation();
            var check2 = engine2.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.True(check2.IsSuccess);

            var run2 = check2.GetEvaluator();
            var result2 = await run2.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.NotEqual(0, result2.ToObject());

            // Refresh connection cache.
            dv.RefreshCache();

            // Evals the same expression by a new engine. Sum should now return the refreshed value.
            var engine3 = new RecalcEngine(config);
            engine3.EnableDelegation();
            var check3 = engine3.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.True(check3.IsSuccess);

            var run3 = check3.GetEvaluator();
            var result3 = await run3.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.IsType<BlankValue>(result3);
        }

        [Fact]
        public async Task RefreshDataverseConnectionSingleOrgPolicyTestFloat()
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
            Assert.True(check1.IsSuccess);

            var run1 = check1.GetEvaluator();
            var result1 = await run1.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.Equal(100.0, result1.ToObject());

            // Simulates a row being deleted by an external user
            await el.DeleteAsync(logicalName, _g1);
            await el.DeleteAsync(logicalName, _g3);
            await el.DeleteAsync(logicalName, _g4);

            // Evals the same expression by a new engine. Should return a wrong result.
            var engine2 = new RecalcEngine(config);
            engine2.EnableDelegation();
            var check2 = engine2.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.True(check2.IsSuccess);

            var run2 = check2.GetEvaluator();
            var result2 = await run2.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.NotEqual(0, result2.ToObject());

            // Refresh connection cache.
            dv.RefreshCache();

            // Evals the same expression by a new engine. Sum should now return the refreshed value.
            var engine3 = new RecalcEngine(config);
            engine3.EnableDelegation();
            var check3 = engine3.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.True(check3.IsSuccess);

            var run3 = check3.GetEvaluator();
            var result3 = await run3.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.IsType<BlankValue>(result3);
        }

        [Fact]
        public async Task RefreshDataverseConnectionMultiOrgPolicyTest()
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
            Assert.True(check1.IsSuccess);

            var run1 = check1.GetEvaluator();
            var result1 = await run1.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.Equal(100m, result1.ToObject());

            // Simulates a row being deleted by an external force
            await el.DeleteAsync(logicalName, _g1);
            await el.DeleteAsync(logicalName, _g3);
            await el.DeleteAsync(logicalName, _g4);

            // Evals the same expression by a new engine. Should return a wrong result.
            var engine2 = new RecalcEngine(config);
            engine2.EnableDelegation();
            var check2 = engine2.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.True(check2.IsSuccess);

            var run2 = check2.GetEvaluator();
            var result2 = await run2.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.NotEqual(0, result2.ToObject());

            // Refresh connection cache.
            dv.RefreshCache();

            // Evals the same expression by a new engine. Sum should now return the refreshed value.
            var engine3 = new RecalcEngine(config);
            engine3.EnableDelegation();
            var check3 = engine3.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.True(check3.IsSuccess);

            var run3 = check3.GetEvaluator();
            var result3 = await run3.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.IsType<BlankValue>(result3);
        }

        [Fact]
        public async Task RefreshDataverseConnectionMultiOrgPolicyTestFloat()
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
            Assert.True(check1.IsSuccess);

            var run1 = check1.GetEvaluator();
            var result1 = await run1.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.Equal(100.0, result1.ToObject());

            // Simulates a row being deleted by an external force
            await el.DeleteAsync(logicalName, _g1);
            await el.DeleteAsync(logicalName, _g3);
            await el.DeleteAsync(logicalName, _g4);

            // Evals the same expression by a new engine. Should return a wrong result.
            var engine2 = new RecalcEngine(config);
            engine2.EnableDelegation();
            var check2 = engine2.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.True(check2.IsSuccess);

            var run2 = check2.GetEvaluator();
            var result2 = await run2.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.NotEqual(0, result2.ToObject());

            // Refresh connection cache.
            dv.RefreshCache();

            // Evals the same expression by a new engine. Sum should now return the refreshed value.
            var engine3 = new RecalcEngine(config);
            engine3.EnableDelegation();
            var check3 = engine3.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.True(check3.IsSuccess);

            var run3 = check3.GetEvaluator();
            var result3 = await run3.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.IsType<BlankValue>(result3);
        }

        // Dependency finder.
        [Theory]
        [InlineData("1+2", "")] // none
        [InlineData("First(t1).Price", "local")]
        [InlineData("First(Remote)", "remote")]
        [InlineData("First(t1).Price & IsBlank(First(Remote))", "local,remote")]
        [InlineData("EntityRef.Price", "local")]
        [InlineData("entityRef.Price", "local")]
        public void TableDependencyFinder(string expression, string listTables)
        {
            var logicalName = "local";

            // Everything policy
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "Remote");
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, EntityLookup _) = CreateMemoryForRelationshipModels(policy);
            var rowScopeSymbols = dv.GetRowScopeSymbols(tableLogicalName: logicalName);
            var parametersRecord = RecordType.Empty().Add("entityRef", dv.GetRecordType("local"), "EntityRef");
            var parametersSymbols = ReadOnlySymbolTable.NewFromRecord(parametersRecord);
            var symbols = ReadOnlySymbolTable.Compose(rowScopeSymbols, dv.Symbols, parametersSymbols);

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

            Assert.Equal(listTables, x);
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

        [Theory]

        // Row Scope
        [InlineData("new_price + 10", 110.0)] // Basic field lookup (RowScope) w/ logical names
        [InlineData("new_price + new_quantity", 120.0)] // new_quantity is 20.
        [InlineData("Price + 10", 110.0, true)] //using Display name for Price
        [InlineData("ThisRecord.Other.Data", 200.0)] // Relationship
        [InlineData("ThisRecord.Other.remoteid = GUID(\"00000000-0000-0000-0000-000000000002\")", true)] // Relationship
        [InlineData("ThisRecord.Price + 10", 110.0, true)] // Basic field lookup (RowScope)
        [InlineData("ThisRecord.Rating = 'Rating (Locals)'.Warm", true)] // Option Sets
        [InlineData("Value(ThisRecord.Rating) = 1", false, true)]
        [InlineData("Value(ThisRecord.Rating) = 2", true, true)]

        // Single Global record
        [InlineData("First(t1).new_price", 100.0, false)]
        [InlineData("First(t1).Price", 100.0, false)]

        // Aggregates
        [InlineData("CountRows(Filter(t1, ThisRecord.Price > 50))", 1.0, false)] // Filter
        [InlineData("Sum(Filter(t1, ThisRecord.Price > 50), ThisRecord.Price)", 100.0, false)] // Filter
        [InlineData("Sum(Filter(t1, ThisRecord.Price > 50) As X, X.Price)", 100.0, false)] // with Alias
        public async Task ExecuteViaInterpreter2(string expr, object expected, bool rowScope = true)
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
                Assert.True(check.IsSuccess);

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
                var result = await run.EvalAsync(CancellationToken.None, runtimeConfig);

                Assert.Equal(expected, result.ToDoubleOrObject());
            }
        }

        // Set() function against entity fields in RowScope
        [Theory]
        [InlineData("Set(Price, 200); Price", 200.0)]
        public async Task LocalSet(string expr, object expected)
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
            Assert.True(check.IsSuccess, string.Join("\r\n", check.Errors.Select(ee => ee.Message)));

            var run = check.GetEvaluator();

            var entity = el.GetFirstEntity(logicalName, dv, CancellationToken.None); // any record
            var record = dv.Marshal(entity);
            var rowScopeValues = ReadOnlySymbolValues.NewFromRecord(rowScopeSymbols, record);
            var allValues = allSymbols.CreateValues(rowScopeValues, dv.SymbolValues);

            var result = await run.EvalAsync(CancellationToken.None, allValues);

            Assert.Equal(new decimal((double)expected), result.ToObject());

            // Extra validation that recordValue is updated .
            if (expr.StartsWith("Set(Price, 200)"))
            {
                Assert.Equal(200m, record.GetField("new_price").ToObject());
                Assert.Equal(200m, entity.Attributes["new_price"]);

                // verify on entity
                var e2 = el.LookupRef(entity.ToEntityReference(), CancellationToken.None);
                Assert.Equal(200m, e2.Attributes["new_price"]);
            }
        }

        // Set() function against entity fields in RowScope
        [Theory]
        [InlineData("Set(Price, 200); Price", 200.0)]
        public async Task LocalSetFloat(string expr, object expected)
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
            Assert.True(check.IsSuccess);

            var run = check.GetEvaluator();

            var entity = el.GetFirstEntity(logicalName, dv, CancellationToken.None); // any record
            var record = dv.Marshal(entity);
            var rowScopeValues = ReadOnlySymbolValues.NewFromRecord(rowScopeSymbols, record);
            var allValues = allSymbols.CreateValues(rowScopeValues, dv.SymbolValues);

            var result = await run.EvalAsync(CancellationToken.None, allValues);

            Assert.Equal(expected, result.ToObject());

            // Extra validation that recordValue is updated .
            if (expr.StartsWith("Set(Price, 200)"))
            {
                Assert.Equal(200.0, record.GetField("new_price").ToObject());

                Assert.Equal(200m, entity.Attributes["new_price"]);

                // verify on entity
                var e2 = el.LookupRef(entity.ToEntityReference(), CancellationToken.None);
                Assert.Equal(200m, e2.Attributes["new_price"]);
            }
        }

        // Patch() function against entity fields in RowScope
        // Decimal is not allowed as a value in DataRow, cast to Decimal during test
        [Theory]
        [InlineData("Patch(t1, First(t1), { Price : 200}); First(t1).Price", 200.0)]
        [InlineData("With( { x : First(t1)}, Patch(t1, x, { Price : 200}); x.Price)", 100.0)] // Expected, x.Price is still old value!
        [InlineData("Patch(t1, First(t1), { Price : 200}).Price", 200.0)]
        [InlineData("Collect(t1, { Price : 200}).Price", 200.0)]
        [InlineData("With( {oldCount : CountRows(t1)}, Collect(t1, { Price : 200});CountRows(t1)-oldCount)", 1.0)]
        [InlineData("Collect(t1, { Price : 255}); LookUp(t1,Price=255).Price", 255.0)]
        [InlineData("Patch(t1, First(t1), { Price : Blank()}); First(t1).Price", null)] // Set to blank will clear it out
        [InlineData("Patch(t1, {localid:GUID(\"00000000-0000-0000-0000-000000000001\")}, { Price : 200}).Price", 200.0)]
        public async Task PatchFunctionFloat(string expr, double? expected)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            foreach (var numberIsFloat in new bool[] { false, true })
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
                Assert.True(check.IsSuccess);

                var run = check.GetEvaluator();

                var result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

                if (numberIsFloat)
                {
                    Assert.Equal(expected, result.ToObject());
                }
                else
                {
                    Assert.Equal(expected is not null ? new decimal((double)expected) : expected, result.ToObject());
                }

                // verify on entity - this should always be updated
                if (expr.Contains("Patch("))
                {
                    var r2 = await engine1.EvalAsync("First(t1)", CancellationToken.None, runtimeConfig: dv.SymbolValues);
                    var entity = (Entity)r2.ToObject();
                    var e2 = el.LookupRef(entity.ToEntityReference(), CancellationToken.None);
                    var actualValue = e2.Attributes["new_price"];
                    if (expected.HasValue)
                    {
                        Assert.Equal(200m, actualValue);
                    }
                    else
                    {
                        Assert.Null(actualValue);
                    }
                }
            }
        }

        [Theory]

        // DV works by making a copy of the entity when retrieving it. In-memory works by reference.
        [InlineData("With({oldCount:CountRows(t1)},Collect(t1,{Price:200});CountRows(t1)-oldCount)", 1.0)]
        [InlineData("Collect(t1,{Price:110});CountRows(t1)", 4.0)]
        [InlineData("With({x:Collect(t1,{Price:77})}, Patch(t1,Last(t1),{Price:x.Price + 3});CountRows(t1))", 4.0)]
        [InlineData("With({x:Collect(t1,{Price:77}), y:Collect(t1,{Price:88})}, Remove(t1,x);Remove(t1,y);CountRows(t1))", 3.0)]
        public async Task CacheBug(string expr, double expected)
        {
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup _) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            var opts = _parserAllowSideEffects;
            var config = new PowerFxConfig();
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.True(check.IsSuccess);

            var run = check.GetEvaluator();

            var result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.Equal(new decimal(expected), result.ToObject());
        }

        [Theory]

        // DV works by making a copy of the entity when retrieving it. In-memory works by reference.
        [InlineData("With({oldCount:CountRows(t1)},Collect(t1,{Price:200});CountRows(t1)-oldCount)", 1.0)]
        [InlineData("Collect(t1,{Price:110});CountRows(t1)", 4.0)]
        [InlineData("With({x:Collect(t1,{Price:77})}, Patch(t1,Last(t1),{Price:x.Price + 3});CountRows(t1))", 4.0)]
        [InlineData("With({x:Collect(t1,{Price:77}), y:Collect(t1,{Price:88})}, Remove(t1,x);Remove(t1,y);CountRows(t1))", 3.0)]
        public async Task CacheBugFloat(string expr, double expected)
        {
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup _) = CreateMemoryForRelationshipModels(numberIsFloat: true);
            dv.AddTable(displayName, logicalName);

            var opts = _parserAllowSideEffects_NumberIsFloat;
            var config = new PowerFxConfig();
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);

            var check = engine1.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.True(check.IsSuccess);

            var run = check.GetEvaluator();

            var result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.Equal(expected, result.ToObject());
        }

        [Theory]
        [InlineData("Patch(t1, First(t1), {Price:200})")]
        public async Task PatchFunctionLean(string expr)
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
            Assert.True(check.IsSuccess);

            var run = check.GetEvaluator();

            var result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.IsNotType<ErrorValue>(result);
        }

        [Theory]
        [InlineData("Patch(t1, First(t1), {Price:1000})")]
        public async Task PatchWithUpdateInvalidFieldError(string expr)
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
            Assert.True(check.IsSuccess);

            var run = check.GetEvaluator();

            var entity = el.GetFirstEntity(logicalName, dv, CancellationToken.None); // any record
            var record = dv.Marshal(entity);
            var rowScopeValues = ReadOnlySymbolValues.NewFromRecord(rowScopeSymbols, record);
            var allValues = allSymbols.CreateValues(rowScopeValues, dv.SymbolValues);

            var result = await run.EvalAsync(CancellationToken.None, allValues);

            Assert.IsType<ErrorValue>(result);
        }

        [Theory]
        [InlineData("Patch(t1, First(t1), {Price:1000})", false)]
        [InlineData("Patch(t1, First(t1), {Price:50})", true)]
        public async Task PatchWithNumberOutOfRangeError(string expr, bool succeeds)
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
            Assert.True(check.IsSuccess);

            var run = check.GetEvaluator();
            var result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

            if (succeeds)
            {
                Assert.IsNotType<ErrorValue>(result);
            }
            else
            {
                Assert.IsType<ErrorValue>(result);
                Assert.Contains(errorMessage, ((ErrorValue)result).Errors.First().Message);
            }
        }

        [Theory]
        [InlineData("Remove(t1, LookUp(t1, localid=GUID(\"00000000-0000-0000-0000-000000000001\")) )", false)]
        [InlineData("Remove(t1, First(t1))", true)]
        public async Task RemoveFunction(string expr, bool injectError)
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
            Assert.True(check.IsSuccess);

            var run = check.GetEvaluator();

            var result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

            if (injectError)
            {
                Assert.IsType<ErrorValue>(result);
                Assert.Equal(errorMessage, ((ErrorValue)result).Errors.First().Message);
            }
            else
            {
                Assert.IsNotType<ErrorValue>(result);

                // Verify on expression - this may be old or no

                // verify on entity - this should always be updated
                Assert.False(el.Exists(new EntityReference(logicalName, _g1)));
            }
        }

        [Fact]
        public void BasicSymbols()
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup _) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            // We get same symbols back - this is important since Check / Eval need to match.
            var sym1 = dv.GetRowScopeSymbols(tableLogicalName: logicalName);
            var sym2 = dv.GetRowScopeSymbols(tableLogicalName: logicalName);

            Assert.Same(sym1, sym2);
        }

        // Test blank references.
        [Theory]
        [InlineData("ThisRecord.Other.Data")] // Relationship
        public async Task RecordBlank(string expr)
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

            var result = await engine1.EvalAsync(expr, CancellationToken.None, runtimeConfig: runtimeConfig);
            Assert.True(result is BlankValue);
        }

        // Calls to Dataverse have 3 possible outcomes:
        // - Success
        // - "Soft" failure - these are translated into ErrorValues and "caught".  Eg: record not found, access denied, network down,
        // - "Hard" failures - these are bugs in our code and their exceptions aborts the execution.  Eg: NullRef, StackOveflow, etc
        [Theory]
        [InlineData("ThisRecord.Other.Data")] // Relationship
        public async Task NetworkErrors(string expr)
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
            var result2 = await engine1.EvalAsync(expr, CancellationToken.None, runtimeConfig: runtimeConfig);
            Assert.Equal(200m, result2.ToObject());

            // Case 2: Soft error:
            // After we have the initial Record, simulate failure.
            // Most exceptions from the IOrganizationService will get caught and converted to ErrorValue.
            // IOrganizationService doesn't actually specify which exceptions it produces on failure.
            var exceptionMessage = "Inject test failure";
            el._onLookupRef = (er) => throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault(), new FaultReason(exceptionMessage));

            var result = await engine1.EvalAsync(expr, CancellationToken.None, runtimeConfig: runtimeConfig);
            Assert.True(result is ErrorValue);
            var error = (ErrorValue)result;
            var errorList = error.Errors;
            Assert.Single(errorList);
            Assert.Contains(exceptionMessage, errorList[0].Message);

            // Case 3: Hard error:
            // "Fatal" errors can propagated exception up.
            el._onLookupRef = (er) => throw new NullReferenceException("Fake nullref");

            await Assert.ThrowsAsync<NullReferenceException>(() => engine1.EvalAsync(expr, CancellationToken.None, runtimeConfig: runtimeConfig));
        }

        // Calls to Dataverse have 3 possible outcomes:
        // - Success
        // - "Soft" failure - these are translated into ErrorValues and "caught".  Eg: record not found, access denied, network down,
        // - "Hard" failures - these are bugs in our code and their exceptions aborts the execution.  Eg: NullRef, StackOveflow, etc
        [Theory]
        [InlineData("ThisRecord.Other.Data")] // Relationship
        public async Task NetworkErrorsFloat(string expr)
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
            var result2 = await engine1.EvalAsync(expr, CancellationToken.None, runtimeConfig: runtimeConfig);
            Assert.Equal(200.0, result2.ToObject());

            // Case 2: Soft error:
            // After we have the initial Record, simulate failure.
            // Most exceptions from the IOrganizationService will get caught and converted to ErrorValue.
            // IOrganizationService doesn't actually specify which exceptions it produces on failure.
            var exceptionMessage = "Inject test failure";
            el._onLookupRef = (er) => throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault(), new FaultReason(exceptionMessage));

            var result = await engine1.EvalAsync(expr, CancellationToken.None, runtimeConfig: runtimeConfig);
            Assert.True(result is ErrorValue);
            var error = (ErrorValue)result;
            var errorList = error.Errors;
            Assert.Single(errorList);
            Assert.Contains(exceptionMessage, errorList[0].Message);

            // Case 3: Hard error:
            // "Fatal" errors can propagated exception up.
            el._onLookupRef = (er) => throw new NullReferenceException("Fake nullref");

            await Assert.ThrowsAsync<NullReferenceException>(() => engine1.EvalAsync(expr, CancellationToken.None, runtimeConfig: runtimeConfig));
        }

        [Fact]
        public void TestDataverseConnection()
        {
            (DataverseConnection dv, _) = CreateMemoryForRelationshipModels();  // numberIsFloat: false

            // Table wasn't added yet.
            Assert.Throws<InvalidOperationException>(() => dv.GetRecordType("local")); // "Must first call AddTable for local"

            var table = dv.AddTable("variableName", "local");
            Assert.Equal("variableName", table.Type.TableSymbolName);

            // 2nd add will fail
            Assert.Throws<InvalidOperationException>(() => dv.AddTable("variableName2", "local")); // "Table with logical name 'local' was already added as variableName."
            Assert.Throws<InvalidOperationException>(() => dv.AddTable("variableName", "remote")); // "Table with variable name 'variableName' was already added as local."
            Assert.Throws<InvalidOperationException>(() => dv.AddTable("variableName", "local")); // "Table with logical name 'local' was already added as variableName."

            RecordType r = dv.GetRecordType("local");
            Assert.Equal("variableName", r.TableSymbolName);

            var type = r.GetFieldType("new_price");
            Assert.True(type is DecimalType);

            // Throws on missing field.
            Assert.Throws<InvalidOperationException>(() => r.GetFieldType("new_missing"));

            // fails, must be logical name
            Assert.Throws<InvalidOperationException>(() => dv.GetRecordType("Locals"));
        }

        [Fact]
        public void TestDataverseConnectionFloat()
        {
            (DataverseConnection dv, _) = CreateMemoryForRelationshipModels(numberIsFloat: true);

            // Table wasn't added yet.
            Assert.Throws<InvalidOperationException>(() => dv.GetRecordType("local")); // "Must first call AddTable for local"

            var table = dv.AddTable("variableName", "local");
            Assert.Equal("variableName", table.Type.TableSymbolName);

            // 2nd add will fail
            Assert.Throws<InvalidOperationException>(() => dv.AddTable("variableName2", "local")); // "Table with logical name 'local' was already added as variableName."
            Assert.Throws<InvalidOperationException>(() => dv.AddTable("variableName", "remote")); // "Table with variable name 'variableName' was already added as local."

            Assert.Throws<InvalidOperationException>(() => dv.AddTable("variableName", "local")); // "Table with logical name 'local' was already added as variableName."

            RecordType r = dv.GetRecordType("local");
            Assert.Equal("variableName", r.TableSymbolName);

            var type = r.GetFieldType("new_price");
            Assert.True(type is NumberType);

            // Throws on missing field.
            Assert.Throws<InvalidOperationException>(() => r.GetFieldType("new_missing"));

            // fails, must be logical name
            Assert.Throws<InvalidOperationException>(() => dv.GetRecordType("Locals"));
        }

        // Verify that a single engine can access two Dataverse orgs simultanously.
        // Since logical names are just scoped to an org, different orgs can conflict on logical names.
        [Fact]
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

            var result = await engine1.EvalAsync("First(T1).Price*1000 + First(T2).Price", CancellationToken.None, runtimeConfig: s12);
            Assert.Equal((100 * 1000) + 200m, result.ToObject());
        }

        [Fact]
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

            var result = await engine1.EvalAsync("First(T1).Price*1000 + First(T2).Price", CancellationToken.None, runtimeConfig: s12);

            Assert.Equal((100 * 1000) + 200.0, result.ToObject());
        }

        [Fact]
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
            Assert.True(r.GetFieldType("new_price") is DecimalType);

            // Can also get fields on metadata not present in attributes
            Assert.True(r.GetFieldType("new_quantity") is DecimalType);

            // Getting fields
            var x = val1.GetField("new_price");
            Assert.Equal(100m, x.ToObject());

            // Blanks - pulling from the metadata.
            x = val1.GetField("new_quantity");
            Assert.True(x is BlankValue);
            Assert.True(x.Type is DecimalType);

            // OptionSets.
            var opt = val1.GetField("rating");

            Assert.Equal(2.0, opt.ToObject());
            Assert.Equal("OptionSetValue (2=Warm)", opt.ToString());
        }

        [Fact]
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
            Assert.True(r.GetFieldType("new_price") is NumberType);

            // Can also get fields on metadata not present in attributes
            Assert.True(r.GetFieldType("new_quantity") is NumberType);

            // Getting fields
            var x = val1.GetField("new_price");
            Assert.Equal(100.0, x.ToObject());

            // Blanks - pulling from the metadata.
            x = val1.GetField("new_quantity");
            Assert.True(x is BlankValue);
            Assert.True(x.Type is NumberType);

            // OptionSets.
            var opt = val1.GetField("rating");

            Assert.Equal(2.0, opt.ToObject());
            Assert.Equal("OptionSetValue (2=Warm)", opt.ToString());
        }

        [Theory]
        [InlineData("Price + 10")] // 110.0 // Basic field lookup
        [InlineData("Rating = 'Rating (Locals)'.Warm")] // true // Option Sets
        [InlineData("ThisRecord.Price + Other.Data")] // 300.0
        public void ExecuteViaInterpreter(string expr)
        {
            (var _, var entityLookup) = CreateMemoryForRelationshipModels();

            var thisRecordName = "local"; // table only has 1 entity.

            // Create context to simulate evaluating on entity in thisRecordName table.
            _ = entityLookup.ConvertEntityToRecordValue(thisRecordName, null, CancellationToken.None);
            var metadata = entityLookup.LookupMetadata(thisRecordName, CancellationToken.None);
            var engine = new DataverseEngine(metadata, new CdsEntityMetadataProvider(entityLookup._rawProvider), new PowerFxConfig());

            var check = engine.Check(expr);
            Assert.True(check.IsSuccess);
            check.ThrowOnErrors();
        }

        // Test with other metadata
        // - schema names != logicalName
        // - calculated columns
        [Fact]
        public void TestSchema()
        {
            // BaselineMetadata
            var lookup = CreateMemoryForBaselineMetadata();

            var thisRecordName = "account"; // table only has 1 entity.

            // Create context to simulate evaluating on entity in thisRecordName table.
            _ = lookup.ConvertEntityToRecordValue(thisRecordName, null, CancellationToken.None);
            var metadata = lookup.LookupMetadata(thisRecordName, CancellationToken.None);
            var engine = new DataverseEngine(metadata, lookup._provider, new PowerFxConfig());

            var expr = "ThisRecord.CurrencyPrice + Calc";
            var check = engine.Check(expr);
            Assert.True(check.IsSuccess);
            check.ThrowOnErrors();
        }

        [Fact]
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
            var result1 = await engine1.EvalAsync(loopupExpr, CancellationToken.None, runtimeConfig: dv.SymbolValues);
            Assert.Equal(100m, result1.ToObject());

            // Simulates a row being deleted by an external user
            // This will delete the inner entity, without impacting DataverseEntityCache's cache
            await el.DeleteAsync(logicalName, _g1);

            // Evals the same expression by a new engine. As DataverseEntityCache's cache is intact, we'll return the cached value.
            var engine4 = new RecalcEngine(config);
            engine4.EnableDelegation(dv.MaxRows);
            var result4 = await engine4.EvalAsync(loopupExpr, CancellationToken.None, runtimeConfig: dv.SymbolValues);
            Assert.Equal(100m, result4.ToObject());

            // Refresh connection cache.
            dv.RefreshCache();

            // Evals the same expression by a new engine. Sum should now return the refreshed value.
            var engine7 = new RecalcEngine(config);
            engine7.EnableDelegation(dv.MaxRows);
            var result7 = await engine7.EvalAsync(loopupExpr, CancellationToken.None, runtimeConfig: dv.SymbolValues);
            Assert.IsType<BlankValue>(result7);            
        }

        [Fact]
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
            var result1 = await engine1.EvalAsync(loopupExpr, CancellationToken.None, runtimeConfig: dv.SymbolValues);
            Assert.Equal(100.0, result1.ToObject());

            // Simulates a row being deleted by an external user
            // This will delete the inner entity, without impacting DataverseEntityCache's cache
            await el.DeleteAsync(logicalName, _g1);

            // Evals the same expression by a new engine. As DataverseEntityCache's cache is intact, we'll return the cached value.
            var engine4 = new RecalcEngine(config);
            engine4.EnableDelegation(dv.MaxRows);
            var result4 = await engine4.EvalAsync(loopupExpr, CancellationToken.None, runtimeConfig: dv.SymbolValues);
            Assert.Equal(100.0, result4.ToObject());

            // Refresh connection cache.
            dv.RefreshCache();

            // Evals the same expression by a new engine. Sum should now return the refreshed value.
            var engine7 = new RecalcEngine(config);
            engine7.EnableDelegation(dv.MaxRows);
            var result7 = await engine7.EvalAsync(loopupExpr, CancellationToken.None, runtimeConfig: dv.SymbolValues);
            Assert.IsType<BlankValue>(result7);            
        }

        [Theory]
        [InlineData("Collect(t1, {Int:Date(2023,2,27)})")]
        [InlineData("Collect(t1, {Int:Date(1889,12,31)})")]
        [InlineData("Collect(t1, {Int:Date(1,1,1)})")]
        [InlineData("With({new_number: Date(2023,2,27)}, Collect(t1, {Int:new_number}))")]
        public async Task DateNumberCoercionTest(string expr)
        {
            // create table "local"
            var logicalName = "allattributes";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup _) = CreateMemoryForAllAttributeModel();
            dv.AddTable(displayName, logicalName);

            var engine = new RecalcEngine();

            engine.Config.SymbolTable.EnableMutationFunctions();

            var opts = _parserAllowSideEffects;
            var check = engine.Check(expr, symbolTable: dv.Symbols, options: opts);

            // Coercion in Collect() now allowed. Will coerce number/date.
            Assert.True(check.IsSuccess);
        }

        [Theory]
        [InlineData("Collect(t1,{ DoesNotExist: 10})")]
        public async Task NullReferenceExceptionTest(string expr)
        {
            // create table "local"
            var logicalName = "allattributes";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup _) = CreateMemoryForAllAttributeModel();
            dv.AddTable(displayName, logicalName);

            var engine = new RecalcEngine();

            engine.Config.SymbolTable.EnableMutationFunctions();

            var opts = _parserAllowSideEffects;
            var check = engine.Check(expr, symbolTable: dv.Symbols, options: opts);

            Assert.False(check.IsSuccess);
            Assert.Contains("The specified column 'DoesNotExist' does not exist.", check.Errors.Last().Message);
        }

        [Theory]
        [InlineData("LookUp(t1, localid = GUID(\"00000000-0000-0000-9999-000000000001\"))")]
        public async Task LookUpMissingEntityReturnsBlank(string expr)
        {
            (DataverseConnection dv, EntityLookup _) = CreateMemoryForRelationshipModels();
            dv.AddTable("t1", "local");

            var engine = new RecalcEngine();

            var opts = _parserAllowSideEffects;
            var check = engine.Check(expr, symbolTable: dv.Symbols, options: opts);
            FormulaValue result = await check.GetEvaluator().EvalAsync(CancellationToken.None, dv.SymbolValues);

            // Failed lookup is blank
            Assert.NotNull(result as BlankValue);
        }

        [Fact]
        public async Task AllNotSupportedAttributesTest()
        {
            var baseExpr = "First(t1).{0}";
            var engine = new RecalcEngine();

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForAllAttributeModel();
            dv.AddTable("t1", "allattributes");

            var entity = (await el.RetrieveAsync("allattributes", _g1, columns: null)).Response;

            var expectedErrors = new List<string>()
            {
                "ImageType column type not supported.",
                "FileType column type not supported.",
            };

            try
            {
                foreach (var attr in entity.Attributes)
                {
                    var expr = string.Format(baseExpr, attr.Key);
                    var result = await engine.EvalAsync(expr, CancellationToken.None, runtimeConfig: dv.SymbolValues);

                    if (result is ErrorValue errorValue)
                    {
                        Assert.Contains(errorValue.Errors.First().Message, expectedErrors);
                    }
                    else
                    {
                        Assert.IsNotType<BlankType>(result);
                    }
                }
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.Message);
            }
        }

        [Fact]
        public async Task RetrieveAsyncErrorTst()
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
            Assert.True(check.IsSuccess);

            var run = check.GetEvaluator();
            var result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.Contains(errorMessage, ((ErrorValue)result).Errors.First().Message);
        }

        [Theory]
        [InlineData("Set(x, First(t1))")]
        [InlineData("Set(t, Filter(t1,true))", true)]
        [InlineData("With({local:First(t1)}, Set(y, local))")]
        [InlineData("Set(x, First(Remote));Other.data")]
        [InlineData("Set(x, Collect(Remote, { Data : 99})); Other.Data")]
        [InlineData("With({r:First(t1)}, Set(x, { Price : r.Price, OtherData : r.Other.Data}))", false, true)]
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
            Assert.Equal(successful, check.IsSuccess);

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

                Assert.Contains(check.Errors, err => err.MessageKey == errorMessageKey);
            }
        }

        [Theory]
        [InlineData("Set(x, First(t1))", "Set(#$firstname$#, First(#$fne$#))")]
        [InlineData("Set(t, Filter(t1,true))", "Set(#$firstname$#, Filter(#$fne$#, #$boolean$#))")]
        [InlineData("With({local:First(t1)}, Set(y, local))", "With({ #$fieldname$#:First(#$fne$#) }, Set(#$firstname$#, #$fne$#))")]
        [InlineData("Set(x, First(Remote));Other.data", "Set(#$firstname$#, First(#$fne$#)) ; #$firstname$#.#$righthandid$#")]
        [InlineData("Set(x, Collect(Remote, { Data : 99})); Other.Data", "Set(#$firstname$#, Collect(#$fne$#, { #$fieldname$#:#$decimal$# })) ; #$firstname$#.#$righthandid$#")]
        [InlineData("With({r:First(t1)}, Set(x, { Price : r.Price, OtherData : r.Other.Data}))", "With({ #$fieldname$#:First(#$fne$#) }, Set(#$firstname$#, { #$fieldname$#:#$fne$#.#$righthandid$#, #$fieldname$#:#$fne$#.#$righthandid$#.#$righthandid$# }))")]
        public void LoggingExpandableSymbolsTest(string expr, string expected)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup _) = CreateMemoryForRelationshipModels();

            dv.AddTable(displayName, logicalName);
            dv.AddTable("Remote", "remote");

            var opts = _parserAllowSideEffects;
            var engine = new RecalcEngine(new PowerFxConfig());

            engine.Config.SymbolTable.EnableMutationFunctions();

            var check = engine.Check(expr, options: opts, symbolTable: dv.Symbols);
            var logging = check.ApplyGetLogging();

            Assert.Equal(expected, logging);
        }

        [Fact]
        public void SerializeEntity()
        {
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "Local"); // unique display name
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels(policy);

            var entityRecordType = dv.GetRecordType("local");

            Func<string, RecordType> logicalToRecord = (logicalName) => dv.GetRecordType(logicalName);
            var option = new JsonSerializerOptions();
            var setting = new FormulaTypeSerializerSettings(logicalToRecord);
            var converter = new FormulaTypeJsonConverter(setting);
            option.Converters.Add(converter);

            // serialization of DV RecordType
            var json = JsonSerializer.Serialize<FormulaType>(entityRecordType, option);
            Assert.Equal(@"{""Type"":{""Name"":""CustomType""},""CustomTypeName"":""local""}", json);

            var deSerializedRecordType = JsonSerializer.Deserialize<FormulaType>(json, option);
            Assert.Equal(entityRecordType, deSerializedRecordType);

            // serialization of DV TableType
            var entityTableType = entityRecordType.ToTable();
            json = JsonSerializer.Serialize<FormulaType>(entityTableType, option);
            Assert.Equal(@"{""Type"":{""Name"":""CustomType"",""IsTable"":true},""CustomTypeName"":""local""}", json);

            var deSerializedTableType = JsonSerializer.Deserialize<FormulaType>(json, option);
            Assert.Equal(entityTableType, deSerializedTableType);
        }

        [Theory]
        [InlineData("First(t1).M|", 11)]
        [InlineData("First(t1).|", 10)]
        [InlineData("ForAll(t1, |", 11)]
        public void MultiSelectIntellisenseTest(string expression, int cursorPosition)
        {
            var logicalName = "allattributes";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup _) = CreateMemoryForAllAttributeModel();
            dv.AddTable(displayName, logicalName);

            var engine = new RecalcEngine();
            var check = engine.Check(expression, symbolTable: dv.Symbols);
            var intellisense = engine.Suggest(check, cursorPosition);

            Assert.True(intellisense.Suggestions.Count() > 0);
            Assert.Contains(intellisense.Suggestions, sgst => sgst.DisplayText.Text == "MultiSelect");
        }

        [Theory]
        [InlineData("Concat(First(t1).multiSelect, Value)", "EightNine")]
        [InlineData("First(First(t1).multiSelect).Value & \" options\"", "Eight options")]
        [InlineData("'MultiSelect (All Attributes)'.'Eight' & \" options\"", "Eight options")]
        [InlineData("Text('MultiSelect (All Attributes)'.'Eight' = First(First(t1).multiSelect).Value)", "true")]
        [InlineData("Text(Text('MultiSelect (All Attributes)'.'Eight') = Text(First(First(t1).multiSelect).Value))", "true")]
        [InlineData("If(First(First(t1).multiSelect).Value =  'MultiSelect (All Attributes)'.'Eight', \"Worked\")", "Worked")]
        public async Task MultiSelectFieldTest(string expression, string expected)
        {
            var logicalName = "allattributes";
            var displayName = "t1";

            (DataverseConnection dv, _) = CreateMemoryForAllAttributeModel();
            dv.AddTable(displayName, logicalName);

            var engine = new RecalcEngine();
            var check = engine.Check(expression, symbolTable: dv.Symbols);
            var result = await check.GetEvaluator().EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.IsType<StringValue>(result);
            Assert.Equal(expected, ((StringValue)result).Value);
        }

        [Theory]
        [InlineData("['MultiSelect (All Attributes)'.'Eight']", 1)]
        [InlineData("['MultiSelect (All Attributes)'.'Eight', 'MultiSelect (All Attributes)'.'Nine']", 2)]
        [InlineData("['MultiSelect (All Attributes)'.'Eight', 'MultiSelect (All Attributes)'.'Eight']", 1)]
        [InlineData("['MultiSelect (All Attributes)'.'Eight', Error({Kind:ErrorKind.Custom})]", 1)]
        [InlineData("['MultiSelect (All Attributes)'.'Eight', 'MultiSelect (All Attributes)'.'Nine', 'MultiSelect (All Attributes)'.'Eight']", 2)]
        [InlineData("[]", 0)]
        [InlineData("[Blank(),Blank()]", 0)]
        public async Task MultiSelectMutationTest(string optionValueSetExpression, int counter)
        {
            var logicalName = "allattributes";
            var displayName = "t1";

            // Base expression + options from inlinedata
            var expression = $"With({{x:Patch(t1, First(t1), {{MultiSelect:{optionValueSetExpression}}})}}, CountRows(x.MultiSelect))";

            (DataverseConnection dv, _) = CreateMemoryForAllAttributeModel();
            dv.AddTable(displayName, logicalName);

            var powerFxConfig = new PowerFxConfig();

            powerFxConfig.SymbolTable.EnableMutationFunctions();

            var opt = new ParserOptions() { AllowsSideEffects = true };
            var engine = new RecalcEngine(powerFxConfig);
            var check = engine.Check(expression, options: opt, symbolTable: dv.Symbols);
            var result = await check.GetEvaluator().EvalAsync(CancellationToken.None, dv.SymbolValues);

            Assert.IsType<DecimalValue>(result);
            Assert.Equal(counter, ((DecimalValue)result).Value);
        }

        [Theory]
        [InlineData("Patch(t1, First(t1), {MultiSelect:[1]})", false)]
        [InlineData("Patch(t1, First(t1), {MultiSelect:[{Value: 'MultiSelect (All Attributes)'.'Eight'}, Error({Kind:ErrorKind.Custom})]})", true)]
        public async Task MultiSelectWrongArgsTest(string expression, bool checkIsSuccess)
        {
            var logicalName = "allattributes";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForAllAttributeModel();
            dv.AddTable(displayName, logicalName);

            var powerFxConfig = new PowerFxConfig();

            powerFxConfig.SymbolTable.EnableMutationFunctions();

            var opt = new ParserOptions() { AllowsSideEffects = true };
            var engine = new RecalcEngine(powerFxConfig);
            var check = engine.Check(expression, options: opt, symbolTable: dv.Symbols);

            Assert.Equal(checkIsSuccess, check.IsSuccess);

            if (check.IsSuccess)
            {
                var result = await check.GetEvaluator().EvalAsync(CancellationToken.None, dv.SymbolValues);
                Assert.IsType<ErrorValue>(result);
            }
            else
            {
                var errorsString = string.Join(",", check.Errors.Select(e => e.Message));
                Assert.Contains("Invalid argument type. Expecting a Table value, but of a different schema", errorsString);
            }
        }

        [Theory]
        [InlineData("Collect(t1, {PolymorphicLookup: First(t2)})", false)]
        [InlineData("Collect(t1, {PolymorphicLookup: First(t1)})", false)]
        [InlineData("Collect(t1, {PolymorphicLookup: {test:1}})", true)]
        public async Task PolymorphicMutationTestAsync(string expr, bool isErrorValue)
        {
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, _) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);
            dv.AddTable("t2", "remote");
            dv.AddTable("t3", "doubleremote");

            var engine = new RecalcEngine();
            engine.EnableDelegation();
            engine.Config.SymbolTable.EnableMutationFunctions();

            var opts = _parserAllowSideEffects;

            var check = engine.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.True(check.IsSuccess);

            var run = check.GetEvaluator();
            var result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

            if (isErrorValue)
            {
                Assert.IsAssignableFrom<ErrorValue>(result);
                return;
            }

            var resultRecord = Assert.IsAssignableFrom<RecordValue>(result);

            var updatedPolyField = Assert.IsAssignableFrom<RecordValue>(await resultRecord.GetFieldAsync("_new_polyfield_value", CancellationToken.None));

            Assert.NotNull(updatedPolyField);
        }

        [Fact]
        public async Task StatusTypeOptionSetTest()
        {
            var logicalName = "local";
            var displayName = "t1";

            var expr = "Patch(t1, First(t1), {Status: Status.Resolved})";

            (DataverseConnection dv, EntityLookup _) = CreateMemoryForRelationshipModels();

            dv.AddTable(displayName, logicalName);

            var opts = _parserAllowSideEffects;
            var engine = new RecalcEngine(new PowerFxConfig());

            engine.Config.SymbolTable.EnableMutationFunctions();

            var check = engine.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.True(check.IsSuccess);

            var run = check.GetEvaluator();
            var result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

            var resultRecord = Assert.IsAssignableFrom<RecordValue>(result);
            Assert.Equal(_g1, ((GuidValue)resultRecord.GetField("localid")).Value);
        }

        [Theory]
        [InlineData("WhoAmI():GUID = First(t1).localid;", "WhoAmI()")]
        public void UDF(string script, string expr)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup _) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            var engine = new RecalcEngine(new PowerFxConfig());
            engine.AddUserDefinedFunction(script, CultureInfo.InvariantCulture, dv.Symbols);

            var check = engine.Check(expr, symbolTable: dv.Symbols);
            var result = check.GetEvaluator().Eval(dv.SymbolValues);

            Assert.IsNotType<ErrorValue>(result);
        }

        [Theory]
        [InlineData("GetPrice():Decimal = First(t1).Price;", "GetPrice()")]
        [InlineData("ApplyDiscount(x:Decimal):Decimal = First(t1).Price * (1 - x/100) ;", "ApplyDiscount(10)")]
        public void UDFWithRestrictedTypesTest(string script, string unused)
        {
            // create table "local"
            var logicalName = "local";
            var displayName = "t1";

            (DataverseConnection dv, EntityLookup el) = CreateMemoryForRelationshipModels();
            dv.AddTable(displayName, logicalName);

            var engine = new RecalcEngine(new PowerFxConfig());

            // Decimal is a restricted type in UDF.
            // https://github.com/microsoft/Power-Fx/pull/2141, https://github.com/microsoft/Power-Fx/pull/2559
            DefinitionsCheckResult dcr = engine.AddUserDefinedFunction(script, CultureInfo.InvariantCulture, dv.Symbols);
            Assert.False(dcr.IsSuccess);
        }

        [Theory]
        [InlineData("Patch(t1, First(t1), {allid:GUID(\"00000000-0000-0000-0000-000000000001\")})")]
        [InlineData("Collect(t1, {allid:GUID(\"00000000-0000-0000-0000-000000000001\")})")]
        [InlineData("Collect(t1, {fullname:\"new full name\"})")]
        public void ReadOnlyFieldsTest(string expr)
        {
            // create table "local"
            var logicalName = "allattributes";
            var displayName = "t1";
            var errorMessageContains = "The specified column is read-only and can't be modified:";

            (DataverseConnection dv, _) = CreateMemoryForAllAttributeModel();
            dv.AddTable(displayName, logicalName);

            var powerFxConfig = new PowerFxConfig();

            powerFxConfig.SymbolTable.EnableMutationFunctions();

            var opt = new ParserOptions() { AllowsSideEffects = true };
            var engine = new RecalcEngine(powerFxConfig);
            var check = engine.Check(expr, options: opt, symbolTable: dv.Symbols);

            Assert.False(check.IsSuccess);
            Assert.Contains(errorMessageContains, check.Errors.First().Message);
        }

        [Theory]
        [InlineData("Environment.Variables", "![jsonvar:O,textvar:s,numbervar:w,booleanvar:b]")]
        [InlineData("Environment.Variables.textvar", "s")]
        [InlineData("Environment.Variables.booleanvar", "b")]
        [InlineData("Environment.Variables.jsonvar", "O")]
        [InlineData("Environment.Variables.numbervar", "w")]
        [InlineData("Environment.Variables.'JSON var'", "O")]
        [InlineData("Environment.Variables.'Number var'", "w")]
        [InlineData("Environment.Variables.'Boolean var'", "b")]
        [InlineData("Environment.Variables.'Text var'", "s")]

        public async Task EnvironmentVariablesTestAsync(string expression, string typeStr)
        {
            (DataverseConnection dv, EntityLookup el) = CreateEnvironmentVariableDefinitionAndValueModel();

            var symbolValues = new SymbolValues();
            symbolValues.AddEnvironmentVariables(await el.GetEnvironmentVariablesAsync());

            var engine = new RecalcEngine();
            var check = engine.Check(expression, symbolTable: ReadOnlySymbolTable.Compose(dv.Symbols, symbolValues.SymbolTable));
            Assert.True(check.IsSuccess, check.Errors.Any() ? check.Errors.First().Message : string.Empty);

            DType.TryParse(typeStr, out DType type);

            if (check.ReturnType is DataverseEnvironmentVariablesRecordType dataverseEnvironmentVariablesRecordType)
            {
                KnownRecordType knownRecordType = (KnownRecordType)FormulaType.Build(type);

                var firstNotSecond = knownRecordType.FieldNames.Except(dataverseEnvironmentVariablesRecordType.FieldNames).ToList();
                var secondNotFirst = dataverseEnvironmentVariablesRecordType.FieldNames.Except(knownRecordType.FieldNames).ToList();

                Assert.True(!firstNotSecond.Any() && !secondNotFirst.Any());
            }
            else
            {
                Assert.Equal(FormulaType.Build(type), check.ReturnType);
            }

            var result = check.GetEvaluator().Eval(ReadOnlySymbolValues.Compose(dv.SymbolValues, symbolValues));

            switch (type.Kind)
            {
                case DKind.UntypedObject:
                    Assert.IsAssignableFrom<UntypedObjectValue>(result);
                    break;
                case DKind.Boolean:
                    Assert.IsAssignableFrom<BooleanValue>(result);
                    break;
                case DKind.String:
                    Assert.IsAssignableFrom<StringValue>(result);
                    break;
                case DKind.Decimal:
                    Assert.IsAssignableFrom<DecimalValue>(result);
                    break;
                case DKind.Record:
                    Assert.IsAssignableFrom<RecordValue>(result);
                    break;
            }
        }

        [Theory]
        [InlineData("Environment.Variables.mismatchtype")]
        [InlineData("Environment.Variables.twin1")]
        [InlineData("Environment.Variables.notsupported", true)]
        [InlineData("Environment.Variables.nodefaultvalue", true)]
        public async Task EnvironmentVariablesErrorsTestAsync(string expression, bool compilationError = false)
        {
            (DataverseConnection dv, EntityLookup el) = CreateEnvironmentVariableDefinitionAndValueErrorsModel();

            var symbolValues = new SymbolValues();
            symbolValues.AddEnvironmentVariables(await el.GetEnvironmentVariablesAsync());

            var engine = new RecalcEngine();
            var check = engine.Check(expression, symbolTable: ReadOnlySymbolTable.Compose(dv.Symbols, symbolValues.SymbolTable));
            Assert.True(check.IsSuccess ^ compilationError, !check.IsSuccess && check.Errors.Any() ? check.Errors.First().Message : string.Empty);

            if (check.IsSuccess)
            {
                var result = check.GetEvaluator().Eval(ReadOnlySymbolValues.Compose(dv.SymbolValues, symbolValues));
                Assert.IsAssignableFrom<ErrorValue>(result);
            }
        }

        // static readonly EntityMetadata _localMetadata = DataverseTests.LocalModel.ToXrm();
        // static readonly EntityMetadata _remoteMetadata = DataverseTests.RemoteModel.ToXrm();

        private static readonly EntityReference _eRef1 = new EntityReference("local", _g1);

        private (DataverseConnection, DataverseEntityCache, EntityLookup) CreateMemoryForRelationshipModelsWithCache(Policy policy = null, bool numberIsFloat = false)
        {
            (DataverseConnection dv, IDataverseServices ds, EntityLookup el) = CreateMemoryForRelationshipModelsInternal(policy, true, numberIsFloat: numberIsFloat);
            return (dv, (DataverseEntityCache)ds, el);
        }

        internal static (DataverseConnection, EntityLookup) CreateMemoryForRelationshipModels(Policy policy = null, bool numberIsFloat = false, bool withExtraEntity = false)
        {
            (DataverseConnection dv, IDataverseServices _, EntityLookup el) = CreateMemoryForRelationshipModelsInternal(policy, numberIsFloat: numberIsFloat, withExtraEntity: withExtraEntity);
            return (dv, el);
        }

        // Create Entity objects to match DataverseTests.RelationshipModels;
        internal static (DataverseConnection, IDataverseServices, EntityLookup) CreateMemoryForRelationshipModelsInternal(Policy policy = null, bool cache = false, bool numberIsFloat = false, bool withExtraEntity = false)
        {
            var remote2 = new Entity("remote", _g2);
            remote2.Attributes["remoteid"] = _g2;

            var doubleremote5 = new Entity("doubleremote", _g5);

            var local3 = new Entity("local", _g3);
            local3.Attributes["localid"] = _g3;
            local3.Attributes["new_price"] = Convert.ToDecimal(10);
            local3.Attributes["old_price"] = null;
            local3.Attributes["rating"] = new Xrm.Sdk.OptionSetValue(1); // Hot;
            local3.Attributes["new_polyfield"] = null;
            local3.Attributes["new_quantity"] = Convert.ToDecimal(10);
            local3.Attributes["new_name"] = "p1";
            local3.Attributes["rtid"] = _g10;

            var local4 = new Entity("local", _g4);
            local4.Attributes["localid"] = _g4;
            local4.Attributes["new_price"] = Convert.ToDecimal(-10);
            local4.Attributes["rating"] = new Xrm.Sdk.OptionSetValue(1); // Hot;
            local4.Attributes["new_polyfield"] = null;
            local4.Attributes["new_quantity"] = Convert.ToDecimal(10);
            local4.Attributes["new_name"] = "row4";
            local4.Attributes["rtid"] = _g11;

            var local5 = new Entity("local", _g5);
            local5.Attributes["localid"] = _g5;
            local5.Attributes["new_price"] = Convert.ToDecimal(10);
            local5.Attributes["rating"] = new Xrm.Sdk.OptionSetValue(1); // Hot;
            local5.Attributes["new_polyfield"] = null;
            local5.Attributes["new_quantity"] = Convert.ToDecimal(12);
            local5.Attributes["rtid"] = _g10;

            var local1 = new Entity("local", _g1);
            local1.Attributes["localid"] = _g1;
            local1.Attributes["new_price"] = Convert.ToDecimal(100);
            local1.Attributes["old_price"] = Convert.ToDecimal(200);
            local1.Attributes["new_date"] = new DateTime(2023, 6, 1);
            local1.Attributes["new_datetime"] = new DateTime(2023, 6, 1, 12, 0, 0);
            local1.Attributes["new_currency"] = new Money(100);
            local1.Attributes["new_name"] = "row1";
            local1.Attributes["new_status"] = new Xrm.Sdk.OptionSetValue() { Value = 1 };
            local1.Attributes["new_polyfield"] = remote2.ToEntityReference();
            local1.Attributes["new_quantity"] = Convert.ToDecimal(20);
            local1.Attributes["new_state"] = new Xrm.Sdk.OptionSetValue() { Value = 1 };
            local1.Attributes["rtid"] = _g12;
            local1.Attributes["new_name"] = "row1";

            // IR for field access for Relationship will generate the relationship name ("refg"), from ReferencingEntityNavigationPropertyName.
            // DataverseRecordValue has to decode these at runtime to match back to real field.
            local1.Attributes["otherid"] = remote2.ToEntityReference();
            local1.Attributes["rating"] = new Xrm.Sdk.OptionSetValue(2); // Warm

            // entity1.new_quantity is intentionally blank.

            remote2.Attributes["data"] = Convert.ToDecimal(200);

            var virtualremote6 = new Entity("virtualremote", _g6);
            virtualremote6.Attributes["vdata"] = Convert.ToDecimal(10);
            local1.Attributes["virtualid"] = virtualremote6.ToEntityReference();

            var elastictable7 = new Entity("elastictable", _g7);
            elastictable7.Attributes["etid"] = _g7;
            elastictable7.Attributes["field1"] = Convert.ToDecimal(200);
            elastictable7.Attributes["partitionid"] = "p1";
            local1.Attributes["elastic_ref"] = elastictable7.ToEntityReference();

            var elastictable8 = new Entity("elastictable", _g8);
            elastictable8.Attributes["etid"] = _g8;
            elastictable8.Attributes["field1"] = Convert.ToDecimal(200);
            elastictable8.Attributes["partitionid"] = "p2";

            var elastictable9 = new Entity("elastictable", _g9);
            elastictable9.Attributes["etid"] = _g9;
            elastictable9.Attributes["field1"] = Convert.ToDecimal(100);
            elastictable9.Attributes["partitionid"] = null;

            var remote10 = new Entity("remote", _g10);
            remote10.Attributes["data"] = Convert.ToDecimal(10);
            remote10.Attributes["other"] = Convert.ToDouble(49);
            remote10.Attributes["remoteid"] = _g10;

            var remote11 = new Entity("remote", _g11);
            remote11.Attributes["data"] = Convert.ToDecimal(-10);
            remote11.Attributes["other"] = Convert.ToDouble(44);
            remote11.Attributes["remoteid"] = _g11;

            var xrmMetadataProvider = new MockXrmMetadataProvider(MockModels.RelationshipModels);
            EntityLookup entityLookup = new EntityLookup(xrmMetadataProvider);

            if (withExtraEntity)
            {
                entityLookup.Add(CancellationToken.None, local1, remote2, local3, local4, local5, doubleremote5, virtualremote6, elastictable7, elastictable8, elastictable9, remote10, remote11);
            }
            else
            {
                entityLookup.Add(CancellationToken.None, local1, remote2, local3, local4, doubleremote5, virtualremote6, elastictable7, elastictable8, elastictable9);
            }

            IDataverseServices ds = cache ? new DataverseEntityCache(entityLookup) : entityLookup;

            var globalOptionSet = GetGlobalOptionSets(MockModels.RelationshipModels);

            CdsEntityMetadataProvider metadataCache = policy is SingleOrgPolicy policy2
                ? new CdsEntityMetadataProvider(
                    xrmMetadataProvider,
                    new Dictionary<string, string>(policy2.AllTables.LogicalToDisplayPairs.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value.ToString())),
                    globalOptionSet)
                { NumberIsFloat = numberIsFloat }
                : new CdsEntityMetadataProvider(xrmMetadataProvider) { NumberIsFloat = numberIsFloat };

            var dvConnection = new DataverseConnection(policy, ds, metadataCache, maxRows: 999);
            return (dvConnection, ds, entityLookup);
        }

        private static List<OptionSetMetadata> GetGlobalOptionSets(EntityMetadataModel[] models)
        {
            var globalOptionSet = new List<OptionSetMetadata>();
            foreach (var model in models)
            {
                foreach (var attribute in model.Attributes)
                {
                    if ((attribute.AttributeType == AttributeTypeCode.Picklist || attribute.AttributeType == AttributeTypeCode.Status) &&
                        attribute.OptionSet.IsGlobal)
                    {
                        var mockOptions = attribute.OptionSet;

                        var optionsList = new List<OptionMetadata>();

                        foreach (var option in mockOptions.Options)
                        {
                            optionsList.Add(new OptionMetadata { Label = new Label(new LocalizedLabel(option.Label, 1033), new LocalizedLabel[0]), Value = option.Value });
                        }

                        var optionSet = new OptionSetMetadata(new OptionMetadataCollection(optionsList))
                        {
                            IsGlobal = mockOptions.IsGlobal,
                            Name = attribute.LogicalName,
                            DisplayName = new Label(new LocalizedLabel($"{attribute.DisplayName} ({model.DisplayCollectionName})", 1033), new LocalizedLabel[0])
                        };

                        globalOptionSet.Add(optionSet);
                    }
                }
            }

            return globalOptionSet;
        }

        private static readonly OptionSetValueCollection _listOptionSetValueCollection = new OptionSetValueCollection(
            new List<Xrm.Sdk.OptionSetValue>() { new Xrm.Sdk.OptionSetValue(value: 8), new Xrm.Sdk.OptionSetValue(value: 9) });

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
            entity1.Attributes["bigint"] = 934157136952;
            entity1.Attributes["double"] = 1d / 3d;
            entity1.Attributes["new_field"] = 1m / 3m;
            entity1.Attributes["userlocaldatetime"] = DateTime.Now;
            entity1.Attributes["int"] = 1;
            entity1.Attributes["picklist"] = new Xrm.Sdk.OptionSetValue() { Value = 1 };
            entity1.Attributes["statecode"] = new Xrm.Sdk.OptionSetValue() { Value = 1 };
            entity1.Attributes["statuscode"] = new Xrm.Sdk.OptionSetValue() { Value = 1 };
            entity1.Attributes["string"] = "string value";
            entity1.Attributes["guid"] = _g1;
            entity1.Attributes["multiSelect"] = _listOptionSetValueCollection;

            var xrmMetadataProvider = new MockXrmMetadataProvider(MockModels.AllAttributeModels);
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

        private (DataverseConnection, EntityLookup) CreateEnvironmentVariableDefinitionAndValueModel(Policy policy = null, bool metadataNumberIsFloat = true)
        {
            var entityDefinition1 = new Entity("environmentvariabledefinition", Guid.NewGuid());
            entityDefinition1.Attributes["schemaname"] = "jsonvar";
            entityDefinition1.Attributes["displayname"] = "JSON var";
            entityDefinition1.Attributes["type"] = new Xrm.Sdk.OptionSetValue() { Value = 100000003 };

            var entityDefinition2 = new Entity("environmentvariabledefinition", Guid.NewGuid());
            entityDefinition2.Attributes["schemaname"] = "textvar";
            entityDefinition2.Attributes["displayname"] = "Text var";
            entityDefinition2.Attributes["type"] = new Xrm.Sdk.OptionSetValue() { Value = 100000000 };

            var entityDefinition3 = new Entity("environmentvariabledefinition", Guid.NewGuid());
            entityDefinition3.Attributes["schemaname"] = "numbervar";
            entityDefinition3.Attributes["displayname"] = "Number var";
            entityDefinition3.Attributes["type"] = new Xrm.Sdk.OptionSetValue() { Value = 100000001 };

            var entityDefinition4 = new Entity("environmentvariabledefinition", Guid.NewGuid());
            entityDefinition4.Attributes["schemaname"] = "booleanvar";
            entityDefinition4.Attributes["displayname"] = "Boolean var";
            entityDefinition4.Attributes["type"] = new Xrm.Sdk.OptionSetValue() { Value = 100000002 };

            // Not yet supported. Comments were left for future reference.
            //var entityDefinition5 = new Entity("environmentvariabledefinition", Guid.NewGuid());
            //entityDefinition5.Attributes["schemaname"] = "datasourcevar";
            //entityDefinition5.Attributes["displayname"] = "Data source var";
            //entityDefinition5.Attributes["type"] = new Xrm.Sdk.OptionSetValue() { Value = 100000004 };

            //var entityDefinition6 = new Entity("environmentvariabledefinition", Guid.NewGuid());
            //entityDefinition6.Attributes["schemaname"] = "secretvar";
            //entityDefinition6.Attributes["displayname"] = "Secret var";
            //entityDefinition6.Attributes["type"] = new Xrm.Sdk.OptionSetValue() { Value = 100000005 };

            var entityValue1 = new Entity("environmentvariablevalue", Guid.NewGuid());
            entityValue1.Attributes["environmentvariabledefinitionid"] = entityDefinition1.ToEntityReference();
            entityValue1.Attributes["value"] = "[{\"a\":1},{\"a\":2}]";

            var entityValue2 = new Entity("environmentvariablevalue", Guid.NewGuid());
            entityValue2.Attributes["environmentvariabledefinitionid"] = entityDefinition2.ToEntityReference();
            entityValue2.Attributes["value"] = @"Lorem ipsum dolor sit amet, consectetur adipiscing elit. Maecenas egestas est lobortis tempus finibus. Duis imperdiet molestie velit eget vehicula. 
                                                 Donec vel purus est. Nullam aliquet nisl in augue tempor fringilla. Suspendisse nec nisi malesuada diam tincidunt eleifend id in metus. 
                                                 Cras iaculis mauris non neque cursus, vel congue magna lacinia. Integer imperdiet est ut lacus volutpat cursus.";

            var entityValue3 = new Entity("environmentvariablevalue", Guid.NewGuid());
            entityValue3.Attributes["environmentvariabledefinitionid"] = entityDefinition3.ToEntityReference();
            entityValue3.Attributes["value"] = "99";

            var entityValue4 = new Entity("environmentvariablevalue", Guid.NewGuid());
            entityValue4.Attributes["environmentvariabledefinitionid"] = entityDefinition4.ToEntityReference();
            entityValue4.Attributes["value"] = "no";

            // Secret and Data source types are not supported.

            var xrmMetadataProvider = new MockXrmMetadataProvider(MockModels.EnvironmentVariableDefinition, MockModels.EnvironmentVariableValue);
            EntityLookup entityLookup = new EntityLookup(xrmMetadataProvider);

            entityLookup.Add(CancellationToken.None, entityDefinition1);
            entityLookup.Add(CancellationToken.None, entityDefinition2);
            entityLookup.Add(CancellationToken.None, entityDefinition3);
            entityLookup.Add(CancellationToken.None, entityDefinition4);
            entityLookup.Add(CancellationToken.None, entityValue1);
            entityLookup.Add(CancellationToken.None, entityValue2);
            entityLookup.Add(CancellationToken.None, entityValue3);
            entityLookup.Add(CancellationToken.None, entityValue4);

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

        private (DataverseConnection, EntityLookup) CreateEnvironmentVariableDefinitionAndValueErrorsModel()
        {
            (DataverseConnection dv, EntityLookup ev) = CreateEnvironmentVariableDefinitionAndValueModel();

            var entityDefinition1 = new Entity("environmentvariabledefinition", Guid.NewGuid());
            entityDefinition1.Attributes["schemaname"] = "mismatchtype";
            entityDefinition1.Attributes["displayname"] = "Mismatch type";
            entityDefinition1.Attributes["type"] = new Xrm.Sdk.OptionSetValue() { Value = 100000001 };

            var entityDefinition2 = new Entity("environmentvariabledefinition", Guid.NewGuid());
            entityDefinition2.Attributes["schemaname"] = "twin1";
            entityDefinition2.Attributes["displayname"] = "Mismatch type";
            entityDefinition2.Attributes["type"] = new Xrm.Sdk.OptionSetValue() { Value = 100000001 };

            // Data source type.
            var entityDefinition3 = new Entity("environmentvariabledefinition", Guid.NewGuid());
            entityDefinition3.Attributes["schemaname"] = "notsupported";
            entityDefinition3.Attributes["displayname"] = "Not supported";
            entityDefinition3.Attributes["type"] = new Xrm.Sdk.OptionSetValue() { Value = 100000004 };

            var entityDefinition4 = new Entity("environmentvariabledefinition", Guid.NewGuid());
            entityDefinition4.Attributes["schemaname"] = "nodefaultvalue";
            entityDefinition4.Attributes["displayname"] = "No default value";
            entityDefinition4.Attributes["type"] = new Xrm.Sdk.OptionSetValue() { Value = 100000000 };

            // Type mismatch. Dataverse stores all variables values as text.
            var entityValue1 = new Entity("environmentvariablevalue", Guid.NewGuid());
            entityValue1.Attributes["environmentvariabledefinitionid"] = entityDefinition1.ToEntityReference();
            entityValue1.Attributes["value"] = "mismatch";

            // Cant have two values for the same variable definition.
            var entityValue2 = new Entity("environmentvariablevalue", Guid.NewGuid());
            entityValue2.Attributes["environmentvariabledefinitionid"] = entityDefinition2.ToEntityReference();
            entityValue2.Attributes["value"] = "twin 1";

            var entityValue3 = new Entity("environmentvariablevalue", Guid.NewGuid());
            entityValue3.Attributes["environmentvariabledefinitionid"] = entityDefinition2.ToEntityReference();
            entityValue3.Attributes["value"] = "twin 2";

            var entityValue4 = new Entity("environmentvariablevalue", Guid.NewGuid());
            entityValue4.Attributes["environmentvariabledefinitionid"] = entityDefinition3.ToEntityReference();
            entityValue4.Attributes["value"] = "???";

            var entityValue5 = new Entity("environmentvariablevalue", Guid.NewGuid());
            entityValue5.Attributes["environmentvariabledefinitionid"] = entityDefinition4.ToEntityReference();
            entityValue5.Attributes["value"] = null;

            ev.Add(CancellationToken.None, entityDefinition1);
            ev.Add(CancellationToken.None, entityDefinition2);
            ev.Add(CancellationToken.None, entityDefinition3);
            ev.Add(CancellationToken.None, entityValue1);
            ev.Add(CancellationToken.None, entityValue2);
            ev.Add(CancellationToken.None, entityValue3);
            ev.Add(CancellationToken.None, entityValue4);

            return (dv, ev);
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
            Assert.Equal("CurrencyPrice", attr.DisplayName);
            Assert.Equal("new_CurrencyPrice_Schema", attr.SchemaName);

            // Calculated field test
            entity1.Attributes["new_Calc"] = 150;
            var attr2 = metadata.Attributes.Where(x => x.LogicalName == "new_Calc").First();
            Assert.Equal(3, attr2.SourceType); // this is a calc filed.

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
