//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AttributeTypeCode = Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    [TestClass]
    public class DataverseTests
    {
        [TestMethod]
        public void CheckCompile1()
        {
            var expr = "\t\t\nfield    *\n2.0\t";

            var model = new EntityMetadataModel
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

            var metadata = model.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);
            var result = engine.Compile(expr, new SqlCompileOptions());

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.SqlFunction);
            Assert.IsNotNull(result.SqlCreateRow);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(0, result.Errors.Count());
            Assert.IsNull(result.Expression);

            Assert.IsTrue(result.ReturnType is NumberType);
            Assert.AreEqual(1, result.TopLevelIdentifiers.Count);
            Assert.AreEqual("new_field", result.TopLevelIdentifiers.First());
            Assert.AreEqual("\t\t\nnew_field    *\n2.0\t", result.LogicalFormula);
        }

        // baseline parameters for compilation
        public const string BaselineFormula = "new_CurrencyPrice + Calc + Latitude";
        public static EntityMetadataModel BaselineMetadata = new EntityMetadataModel
        {
            LogicalName = "account",
            DisplayCollectionName = "Accounts",
            PrimaryIdAttribute = "accountid",

            Attributes = new AttributeMetadataModel[]
                {
                    AttributeMetadataModel.NewDecimal("new_CurrencyPrice", "CurrencyPrice").SetSchemaName("new_CurrencyPrice_Schema"),
                    AttributeMetadataModel.NewDouble("new_Quantity", "Quantity"),
                    AttributeMetadataModel.NewString("new_Name", "Name"),
                    AttributeMetadataModel.NewDecimal("new_Calc", "Calc", "new_Calc_Schema").SetSchemaName("new_Calc_Schema").SetCalculated(),
                    AttributeMetadataModel.NewGuid("accountid","accountid").SetSchemaName("AccountId"),
                    AttributeMetadataModel.NewDecimal("address1_latitude", "Latitude").SetLogical()
                }
        }.SetSchemaName("Account");

        public const string BaselineFunction = @"CREATE FUNCTION fn_testUdf1(
    @v0 decimal(23,10), -- new_CurrencyPrice
    @v2 uniqueidentifier -- accountid
) RETURNS decimal(23,10)
AS BEGIN
    DECLARE @v1 decimal(23,10)
    DECLARE @v4 decimal(23,10)
    DECLARE @v3 decimal(38,10)
    DECLARE @v5 decimal(38,10)
    SELECT TOP(1) @v1 = [new_Calc_Schema],@v4 = [address1_latitude] FROM [dbo].[Account] WHERE[AccountId] = @v2

    -- expression body
    SET @v3 = (CAST(ISNULL(@v0,0) AS decimal(38,10)) + CAST(ISNULL(@v1,0) AS decimal(38,10)))
    IF(@v3<-100000000000 OR @v3>100000000000) BEGIN RETURN NULL END
    SET @v5 = (CAST(ISNULL(@v3,0) AS decimal(38,10)) + CAST(ISNULL(@v4,0) AS decimal(38,10)))
    -- end expression body

    IF(@v5<-100000000000 OR @v5>100000000000) BEGIN RETURN NULL END
    RETURN ROUND(@v5, 10)
END
";
        public const string BaselineCreateRow = @"fn_testUdf1([new_CurrencyPrice_Schema],[AccountId])
";
        public const string BaselineLogicalFormula = "new_CurrencyPrice + new_Calc + address1_latitude";

        [TestMethod]
        public void CheckCompileBaseline()
        {
            // Can use both Display or Sql names. 
            var exprStr = BaselineFormula;

            var options = new SqlCompileOptions
            {
                CreateMode = SqlCompileOptions.Mode.Create,
                UdfName = "fn_testUdf1"
            };

            var engine = new PowerFx2SqlEngine(BaselineMetadata.ToXrm());
            var result = engine.Compile(exprStr, options);

            Assert.AreEqual("address1_latitude,new_Calc,new_CurrencyPrice", ToStableString(result.TopLevelIdentifiers));

            Assert.AreEqual(BaselineFunction, result.SqlFunction);

            Assert.AreEqual(BaselineCreateRow, result.SqlCreateRow);
            Assert.AreEqual(BaselineLogicalFormula, result.LogicalFormula);
        }

        [TestMethod]
        public void CheckCompileAllAttributeTypes()
        {
            var expr = "field * Int - Money + If(Boolean || Picklist = 'Picklist (All Attributes)'.One, Value(String), 2)";

            var metadata = AllAttributeModel.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);
            var result = engine.Compile(expr, new SqlCompileOptions());

            Assert.IsNotNull(result);
            Assert.AreEqual(true, result.IsSuccess);
            Assert.AreEqual("new_field * int - money + If(boolean || picklist = allattributes_picklist_optionSet.'1', Value(string), 2)", result.LogicalFormula);
        }

        [TestMethod]
        public void CheckFailureLookupNoProvider()
        {
            var expr = "field * Lookup.other";

            var model = new EntityMetadataModel
            {
                Attributes = new AttributeMetadataModel[]
                {
                    new AttributeMetadataModel
                     {
                         LogicalName= "new_field",
                         DisplayName = "field",
                         AttributeType = AttributeTypeCode.Decimal
                     },
                    new AttributeMetadataModel
                    {
                        LogicalName = "new_lookup",
                        DisplayName = "Lookup",
                        AttributeType = AttributeTypeCode.Lookup,
                        Targets = new string[] { "othertable" }
                    }
                },
                ManyToOneRelationships = new OneToManyRelationshipMetadataModel[]
                {
                    new OneToManyRelationshipMetadataModel
                    {
                        ReferencedAttribute = "otherid",
                        ReferencedEntity = "othertable",
                        ReferencingAttribute = "new_lookup",
                        ReferencingEntity = "placeholder",
                        ReferencedEntityNavigationPropertyName = "refd",
                        ReferencingEntityNavigationPropertyName = "refg",
                        SchemaName = "lookup"
                    }
                }
            };

            var metadata = model.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);
            var result = engine.Check(expr);

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Errors);
            var errors = result.Errors.ToArray();
            Assert.AreEqual(2, errors.Length);
            Assert.AreEqual("Name isn't valid. 'Lookup' isn't recognized.", errors[0].Message);
        }

        [TestMethod]
        public void CheckNullRef()
        {
            var engine = new PowerFx2SqlEngine();
            Assert.ThrowsException<ArgumentNullException>(() => engine.Check(null));
        }

        [TestMethod]
        public void CheckSuggestionFailure()
        {
            var engine = new PowerFx2SqlEngine();
            var intellisense = engine.Suggest("foo + ", cursorPosition: 6);

            Assert.IsNotNull(intellisense);
            Assert.IsNull(intellisense.Exception);
            Assert.AreEqual(0, intellisense.Suggestions.Count());
        }

        [TestMethod]
        public void CheckSuccess()
        {
            var engine = new PowerFx2SqlEngine();
            var result = engine.Check("3*2");

            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(result.ReturnType is NumberType);
        }

        [TestMethod]
        public void CheckParseError()
        {
            var engine = new PowerFx2SqlEngine();
            var result = engine.Check("3*1+");

            Assert.IsFalse(result.IsSuccess);
            var errors = result.Errors.ToArray();
            Assert.IsTrue(errors.Length> 1);
            Assert.IsTrue(errors[0].ToString().StartsWith(
                "Error 4-4: Expected an operand"));
            Assert.AreEqual(TexlStrings.ErrOperandExpected.Key, errors[0].MessageKey);
        }

        [DataTestMethod]
        [DataRow("3+foo+2", "Error 2-5: Name isn't valid. 'foo' isn't recognized.", "ErrInvalidName", DisplayName = "Invalid field")]
        [DataRow("3+foo(2)", "Error 2-8: 'foo' is an unknown or unsupported function.", "ErrUnknownFunction", DisplayName = "Invalid function")]
        public void CheckBindError(string expr, string message, string key)
        {
            var engine = new PowerFx2SqlEngine();
            var result = engine.Check(expr); // foo is undefined 

            Assert.IsFalse(result.IsSuccess);
            var errors = result.Errors.ToArray();
            Assert.AreEqual(1, errors.Length);
            Assert.AreEqual(message, errors[0].ToString());
            Assert.AreEqual(key, errors[0].MessageKey);
        }

        // Verify error messages in other locales
        [DataTestMethod]
        [DataRow("3+", "Opérande attendu. La formule ou l’expression attend un opérande valide", "ErrOperandExpected", DisplayName = "Parse error")]
        public void CheckLocaleErrorMssage(string expr, string message, string key)
        {
            var culture = new CultureInfo("fr-FR");
            var engine = new PowerFx2SqlEngine(culture: culture);
            var result = engine.Check(expr); // foo is undefined 

            Assert.IsFalse(result.IsSuccess);
            var errors = result.Errors.ToArray();            
            Assert.IsTrue(errors[0].ToString().Contains(message));
            Assert.AreEqual(key, errors[0].MessageKey);
        }

        [TestMethod]
        public void CompileTypeHint()
        {
            var expr = "field * 2.0";

            var model = new EntityMetadataModel
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

            var metadata = model.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);
            var options = new SqlCompileOptions { TypeHints = new SqlCompileOptions.TypeDetails { TypeHint = AttributeTypeCode.Integer } };
            var result = engine.Compile(expr, options);

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.SqlFunction);
            Assert.IsNotNull(result.SqlCreateRow);
            Assert.AreEqual(0, result.Errors.Count());

            Assert.IsTrue(result.ReturnType is SqlIntType);
        }

        [TestMethod]
        public void CompilePassthruTypeHint()
        {
            var expr = "\"foo\"";
            var engine = new PowerFx2SqlEngine();
            var options = new SqlCompileOptions { TypeHints = new SqlCompileOptions.TypeDetails { TypeHint = AttributeTypeCode.String } };
            var result = engine.Compile(expr, options);

            Assert.AreEqual(0, result.Errors.Count());
            Assert.IsTrue(result.ReturnType is StringType);
        }

        [TestMethod]
        public void CompileInvalidTypeHint()
        {
            var expr = "2 + 2";

            var engine = new PowerFx2SqlEngine();
            var options = new SqlCompileOptions { TypeHints = new SqlCompileOptions.TypeDetails { TypeHint = AttributeTypeCode.String } };
            var result = engine.Compile(expr, options);

            var errors = result.Errors.ToArray();
            Assert.AreEqual(1, errors.Length);
            Assert.AreEqual("The result type for this formula is expected to be String, but the actual result type is Number. The result type of a formula column cannot be changed.", errors[0].Message);
            Assert.AreEqual(SqlCompileException.ResultTypeMustMatch.Key, errors[0].MessageKey);
        }

        [DataTestMethod]
        [DataRow("Lookup", "Error 0-6: The result type Record is not supported in formula columns.", DisplayName = "Lookup")]
        [DataRow("If(true, 'Self Reference', Lookup)", "Error 0-34: The result type Record is not supported in formula columns.", DisplayName = "Polymorphic Lookup")]
        [DataRow("Blank()", "Error 0-7: The result type ObjNull is not supported in formula columns.", DisplayName = "Blank")]
        [DataRow("Guid", "Error 0-4: The result type Guid is not supported in formula columns.", DisplayName = "Guid")]
        [DataRow("Owner", "Error 0-5: The result type Record is not supported in formula columns.", DisplayName = "Owner")]
        [DataRow("Customer", "Error 0-8: The result type Record is not supported in formula columns.", DisplayName = "Customer")]
        [DataRow("BigInt", "Error 0-6: Columns of type BigInt are not supported in formula columns.", DisplayName = "BigInt")]
        [DataRow("Email", "Error 0-5: Columns of type String with format Email are not supported in formula columns.", DisplayName = "Email")]
        [DataRow("Ticker", "Error 0-6: Columns of type String with format TickerSymbol are not supported in formula columns.", DisplayName = "Ticker")]
        [DataRow("Hyperlink", "Error 0-9: Columns of type String with format Url are not supported in formula columns.", DisplayName = "Hyperlink")]
        [DataRow("If(true, Hyperlink)", "Error 9-18: Columns of type String with format Url are not supported in formula columns.", DisplayName = "Hyperlink in If")]
        [DataRow("Left(Hyperlink, 2)", "Error 5-14: Columns of type String with format Url are not supported in formula columns.", DisplayName = "Hyperlink in Left")]
        [DataRow("Duration", "Error 0-8: Columns of type Integer with format Duration are not supported in formula columns.", DisplayName = "Duration")]
        [DataRow("TimeZone", "Error 0-8: Columns of type Integer with format TimeZone are not supported in formula columns.", DisplayName = "TimeZone")]
        [DataRow("Image", "Error 0-5: Columns of type Virtual are not supported in formula columns.", DisplayName = "Image")]
        [DataRow("IsBlank(Image)", "Error 8-13: Columns of type Virtual are not supported in formula columns.", DisplayName = "Image in IsBlank")]
        [DataRow("File", "Error 0-4: Name isn't valid. 'File' isn't recognized.", DisplayName = "File not added to entity")]
        [DataRow("Picklist", "Error 0-8: The result type OptionSetValue is not supported in formula columns.", DisplayName = "Picklist")]
        [DataRow("MultiSelect", "Error 0-11: The result type OptionSetValue is not supported in formula columns.", DisplayName = "Multi Select Picklist")]
        [DataRow("If(IsBlank(String), 'Picklist (All Attributes)'.One, 'Picklist (All Attributes)'.Two)", "Error 0-85: The result type OptionSetValue is not supported in formula columns.", DisplayName = "Built picklist")]
        [DataRow("If(IsBlank(String), 'MultiSelect (All Attributes)'.Eight, 'MultiSelect (All Attributes)'.Ten)", "Error 0-93: The result type OptionSetValue is not supported in formula columns.", DisplayName = "Built hybrid picklist")]
        public void CompileInvalidTypes(string expr, string error)
        {
            var provider = new MockXrmMetadataProvider(AllAttributeModels);
            var engine = new PowerFx2SqlEngine(AllAttributeModels[0].ToXrm(), new CdsEntityMetadataProvider(provider));

            var checkResult = engine.Check(expr);
            Assert.AreEqual(false, checkResult.IsSuccess);
            Assert.IsNotNull(checkResult.Errors);
            var errors = checkResult.Errors.ToArray();
            Assert.AreEqual(1, errors.Length);
            Assert.AreEqual(error, errors[0].ToString());

            var compileResult = engine.Compile(expr, new SqlCompileOptions());
            Assert.AreEqual(false, compileResult.IsSuccess);
            Assert.IsNotNull(compileResult.Errors);
            errors = checkResult.Errors.ToArray();
            Assert.AreEqual(1, errors.Length, 1);
            Assert.AreEqual(error, errors[0].ToString());
            Assert.IsNotNull(compileResult.SanitizedFormula);
        }

        [DataTestMethod]
        [DataRow("field", typeof(SqlDecimalType), DisplayName = "Decimal")]
        [DataRow("1.1", typeof(SqlDecimalType), DisplayName = "Numeric literal returns Decimal")]
        [DataRow("Money", typeof(SqlDecimalType), DisplayName = "Money returns Decimal")]
        [DataRow("Int", typeof(SqlDecimalType), DisplayName = "Int returns Decimal")]
        [DataRow("String", typeof(StringType), DisplayName = "String")]
        [DataRow("\"foo\"", typeof(StringType), DisplayName = "String literal returns String")]
        [DataRow("Boolean", typeof(BooleanType), DisplayName = "Boolean")]
        [DataRow("true", typeof(BooleanType), DisplayName = "Boolean literal returns Boolean")]
        [DataRow("Mod(int, int)", typeof(SqlDecimalType), DisplayName = "Int from function returns decimal")]
        public void CompileValidReturnType(string expr, Type returnType)
        {
            var provider = new MockXrmMetadataProvider(AllAttributeModels);
            var engine = new PowerFx2SqlEngine(AllAttributeModels[0].ToXrm(), new CdsEntityMetadataProvider(provider));

            AssertReturnType(engine, expr, returnType);
        }

        [DataTestMethod]
        [DataRow("", DisplayName = "Empty")]
        [DataRow("    ", DisplayName = "Spaces")]
        [DataRow("\n\t  \r\n  \t\n\r\n", DisplayName = "Whitespace")]
        public void CheckEmptyFormula(string expr)
        {
            var engine = new PowerFx2SqlEngine();

            var result = engine.Check(expr);
            Assert.AreEqual(true, result.IsSuccess);
            Assert.AreEqual(typeof(BlankType), result.ReturnType.GetType());
        }

        // Verify that AllAttributeModel has an attribute of each type
        [TestMethod]
        public void VerifyAllAttributes()
        {
            var set = new HashSet<AttributeTypeCode>();
            foreach (AttributeTypeCode val in Enum.GetValues(typeof(AttributeTypeCode)))
            {
                set.Add(val);
            }

            foreach (var attr in AllAttributeModel.Attributes)
            {
                var code = attr.AttributeType.Value;
                set.Remove(code);
            }

            string remaining = string.Join(",", set.OrderBy(x => x.ToString()).ToArray());

            string untested = "CalendarRules,ManagedProperty,PartyList"; // should be empty 
            Assert.AreEqual(untested, remaining);
        }


        // Create expressions to consume the field.
        // Always return a "safe" known type (such as bool) since we're testing consumption, not production.
        // May return multiple expressions. 
        private static string[] GetConsumingExpressions(AttributeMetadataModel attr)
        {
            List<string> expressions = new List<string>();

            bool addIdentityCheck = true;

            // IsBlank should accept any type, and always return a boolean.
            expressions.Add($"IsBlank({attr.LogicalName})");

            var code = attr.AttributeType.Value;
            switch (code)
            {
                // Should be able to use arithmetic operator on any numerical type
                case AttributeTypeCode.BigInt:
                case AttributeTypeCode.Decimal:
                case AttributeTypeCode.Double:
                case AttributeTypeCode.Integer:
                case AttributeTypeCode.Money:
                    expressions.Add($"{attr.LogicalName} > 0");
                    break;

                case AttributeTypeCode.Boolean:
                    expressions.Add($"Not({attr.LogicalName})");
                    break;

                case AttributeTypeCode.Picklist:
                case AttributeTypeCode.Status:
                case AttributeTypeCode.State:
                    // Generate comparison to constant, like: 
                    //   'State' = 'State (All Attributes)'.'Active'
                    var os = attr.OptionSet;
                    var osDisplayName = $"'{os.DisplayName} (All Attributes)'"; // See GetOptionSetDisplayName()
                    expressions.Add($"{attr.LogicalName} = {osDisplayName}.'{os.Options[0].Label}'");
                    break;

                case AttributeTypeCode.Virtual:
                case AttributeTypeCode.Owner:
                    addIdentityCheck = false; // Owner is a record, can't compare records
                    break;

            };

            // Identity check. 
            if (addIdentityCheck)
            {
                expressions.Add($"{attr.LogicalName} = '{attr.DisplayName}'");
            }

            return expressions.ToArray();
        }

        static void AssertResult(CheckResult checkResult, Dictionary<string, string> expectedErrors, string fieldName, string expr)
        {
            if (checkResult.IsSuccess)
            {
                Assert.IsFalse(expectedErrors.ContainsKey(fieldName), $"Type {fieldName} should not be supported");
            }
            else
            {
                Assert.IsTrue(expectedErrors.ContainsKey(fieldName), $"{expr} fails to compile.");
                string expectedError = expectedErrors[fieldName];
                var foundError = checkResult.Errors.FirstOrDefault(error => error.Message.Contains(expectedError, StringComparison.OrdinalIgnoreCase));
                var actualError = checkResult.Errors.First().Message;
                Assert.IsNotNull(foundError, $"For {fieldName}, expected error message: {expectedError}\r\nActualError: {actualError}");
            }
        }

        // For each attribute type x, verify we can consume it.
        // Explicitly iterate over every attribute in AllAttributeModels (to ensure we're being comprehensive.
        // And if we can't consume it, verify the error. 
        [TestMethod]
        public void VerifyProduceAndConsumeAllTypes()
        {
            // mapping of field's logicalName --> fragment of Error received when trying to consume the type. 
            var unsupportedConsumer = new Dictionary<string, string>
            {
                { "double", "Columns of type Double are not supported in formula columns." },
                { "duration", "Columns of type Integer with format Duration are not supported in formula columns" },
                { "new_lookup", "Name isn't valid. 'new_lookup' isn't recognized."  },
                { "selfid", "Name isn't valid. 'selfid' isn't recognized." },
                { "hyperlink", "Columns of type String with format Url are not supported in formula columns." },
                { "email", "Columns of type String with format Email are not supported in formula columns." },
                { "ticker", "Columns of type String with format TickerSymbol are not supported in formula columns." },
                { "timezone", "Columns of type Integer with format TimeZone are not supported in formula columns." },
                { "bigint", "Columns of type BigInt are not supported in formula columns." },
                { "EntityName", "Name isn't valid. 'EntityName' isn't recognized." }, // AttributeTypeCode.EntityName are not imported 				
                { "file", "Name isn't valid. 'file' isn't recognized."},
                { "customerid", "Name isn't valid. 'customerid' isn't recognized." },

                // Different test expressions may give different errors:
                // image='Image' fails since BinaryOpKind.EqImage isn't implemented. 
                // IsBlank(image) fails accessing the image field (Virtual). 
                { "image", "Columns of type Virtual are not supported in formula columns."}
            };

            const string errCantProduceOptionSets = "The result type OptionSetValue is not supported in formula columns.";
            var unsupportedProducer = new Dictionary<string, string>
            {
                { "guid", "The result type Guid is not supported in formula columns." },
                { "allid", "The result type Guid is not supported in formula columns." },
                { "ownerid", "The result type Record is not supported in formula columns." },
                { "statecode", errCantProduceOptionSets },
                { "statuscode", errCantProduceOptionSets },
                { "picklist", errCantProduceOptionSets },
                { "multiSelect", errCantProduceOptionSets }
            };

            var provider = new MockXrmMetadataProvider(AllAttributeModels);
            var engine = new PowerFx2SqlEngine(AllAttributeModels[0].ToXrm(), new CdsEntityMetadataProvider(provider));

            foreach (var attr in AllAttributeModel.Attributes)
            {
                if (!attr.AttributeType.HasValue) { continue; }

                var exprs = GetConsumingExpressions(attr);

                foreach (var expr in exprs)
                {
                    var checkResult = engine.Check(expr);
                    AssertResult(checkResult, unsupportedConsumer, attr.LogicalName, expr);
                }


                // Verify producers
                if (!unsupportedConsumer.ContainsKey(attr.LogicalName))
                {
                    // To test producers, we just pass the field straight through. This means we must be able to consume the field first. 
                    // There are types we could produce that we can't consume (such as if a function or operator returned the type)
                    // but we don't have an automatic way of testing that. 
                    var expr = $"{attr.LogicalName}";
                    var checkResult = engine.Check(expr);

                    AssertResult(checkResult, unsupportedProducer, attr.LogicalName, expr);
                }
            }
        }

        // Test if we get passed metadata we don't recognize. 
        [TestMethod]
        public void FutureUnsupportedType()
        {
            var model = new EntityMetadataModel
            {
                Attributes = new AttributeMetadataModel[]
                {
                  AttributeMetadataModel.NewFutureUnsupported("future", "Future")
                }
            };

            // The ctor will do an initial round of parsing and throws an internal exception. 
            // BUG - should add as as an "unrecognized" symbol. 
            // https://dynamicscrm.visualstudio.com/DefaultCollection/OneCRM/_workitems/edit/2624282
            Assert.ThrowsException<AppMagic.Authoring.Importers.DataDescription.ParseException>(
                () => new PowerFx2SqlEngine(model.ToXrm()));
        }

        // Model where names conflict on case. 
        EntityMetadataModel ModelWithCasing = new EntityMetadataModel
        {
            Attributes = new AttributeMetadataModel[]
              {
                  AttributeMetadataModel.NewDecimal("field1", "FIELD DISPLAY"),
                  AttributeMetadataModel.NewString("Field1", "field display")
              }
        };

        // Test that we can handle casing overloads on fields. 
        // Dataverse fields are case *sensitive*. 
        [DataTestMethod]
        [DataRow("'FIELD DISPLAY'", typeof(SqlDecimalType))]
        [DataRow("field1", typeof(SqlDecimalType))]
        [DataRow("'field display'", typeof(StringType))]
        [DataRow("Field1", typeof(StringType))]
        public void CheckCasing(string expr, Type returnType)
        {
            var metadata = ModelWithCasing.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);

            AssertReturnType(engine, expr, returnType);
        }

        // Verify the expression has the given return type (specified as a FormulaType). 
        static void AssertReturnType(PowerFx2SqlEngine engine, string expr, Type returnType)
        {
            Assert.IsTrue(typeof(FormulaType).IsAssignableFrom(returnType));

            var checkResult = engine.Check(expr);
            Assert.AreEqual(true, checkResult.IsSuccess);
            Assert.AreEqual(returnType, checkResult.ReturnType.GetType());

            var compileResult = engine.Compile(expr, new SqlCompileOptions());
            Assert.AreEqual(true, compileResult.IsSuccess);
            Assert.AreEqual(returnType, compileResult.ReturnType.GetType());
        }

        static void AssertReturnTypeOrError(PowerFx2SqlEngine engine, string expr, bool success, Type returnType, params string[] errors)
        {
            if (success)
            {
                Assert.IsTrue(typeof(FormulaType).IsAssignableFrom(returnType));
            }

            var checkResult = engine.Check(expr);
            Assert.AreEqual(success, checkResult.IsSuccess);
            if (success)
            {
                Assert.AreEqual(returnType, checkResult.ReturnType.GetType());
                Assert.AreEqual(0, checkResult.Errors.Count());
            }
            else
            {
                Assert.IsNotNull(checkResult.Errors);
                var actualErrors = checkResult.Errors.Select(error => error.ToString()).ToArray();
                CollectionAssert.AreEqual(errors, actualErrors);
            }

            var options = new SqlCompileOptions();
            var compileResult = engine.Compile(expr, options);
            Assert.AreEqual(success, compileResult.IsSuccess);

            if (success)
            {
                Assert.AreEqual(returnType, checkResult.ReturnType.GetType());
                Assert.AreEqual(0, compileResult.Errors.Count());
            }
            else
            {
                Assert.IsNotNull(compileResult.Errors);
                var actualErrors = compileResult.Errors.Select(error => error.ToString()).ToArray();
                CollectionAssert.AreEqual(errors, actualErrors);
                Assert.IsNotNull(compileResult.SanitizedFormula);
            }
        }

        // Has conflicting _display_ names
        EntityMetadataModel ModelWithConflict = new EntityMetadataModel
        {
            Attributes = new AttributeMetadataModel[]
              {
                    new AttributeMetadataModel
                    {
                        LogicalName = "conflict1",
                        DisplayName = "Conflict",
                        AttributeType = AttributeTypeCode.Decimal
                    },
                    new AttributeMetadataModel
                    {
                        LogicalName = "conflict2",
                        DisplayName = "Conflict",
                        AttributeType = AttributeTypeCode.Decimal
                    }
              }
        };

        [TestMethod]
        public void CheckFieldConflict()
        {
            var expr = "Conflict - conflict2";

            var metadata = ModelWithConflict.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);
            var result = engine.Check(expr);

            Assert.IsNotNull(result.Errors);
            var errors = result.Errors.ToArray();
            Assert.AreEqual(errors.Length, 1);
            Assert.AreEqual("Error 0-8: Name isn't valid. 'Conflict' isn't recognized.", errors[0].ToString());
        }

        [DataTestMethod]
        [DataRow("conflict1 + conflict2", DisplayName = "LogicalNames")]
        [DataRow("'Conflict (conflict1)' + 'Conflict (conflict2)'", DisplayName = "Disambiguation")]
        public void CompileFieldConflictResolved(string expr)
        {
            var metadata = ModelWithConflict.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);
            var options = new SqlCompileOptions();
            var result = engine.Compile(expr, options);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(0, result.Errors.Count());            
            Assert.AreEqual("conflict1 + conflict2", result.LogicalFormula);
        }

        [TestMethod]
        public void CompileThisRecord()
        {
            var expr = "ThisRecord.a + ThisRecord.B + A + b";

            var model = new EntityMetadataModel
            {
                Attributes = new AttributeMetadataModel[]
                {
                    new AttributeMetadataModel
                    {
                        LogicalName = "a",
                        DisplayName = "A",
                        AttributeType = AttributeTypeCode.Decimal
                    },
                    new AttributeMetadataModel
                    {
                        LogicalName = "b",
                        DisplayName = "B",
                        AttributeType = AttributeTypeCode.Decimal
                    }
                }
            };

            var metadata = model.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);
            var options = new SqlCompileOptions();
            var result = engine.Compile(expr, options);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("ThisRecord.a + ThisRecord.b + a + b", result.LogicalFormula);
        }

        [TestMethod]
        public void CompileInvalidFormula()
        {
            var expr = new string('a', 1001);
            var error = "Error 0-1001: Formulas can't be longer than 1000 characters.";

            var engine = new PowerFx2SqlEngine();
            var options = new SqlCompileOptions();

            var checkResult = engine.Check(expr);
            Assert.AreEqual(false, checkResult.IsSuccess);
            Assert.IsNotNull(checkResult.Errors);
            Assert.AreEqual(1, checkResult.Errors.Count());
            Assert.AreEqual(error, checkResult.Errors.First().ToString());

            var compileResult = engine.Compile(expr, new SqlCompileOptions());
            Assert.AreEqual(false, compileResult.IsSuccess);
            Assert.IsNotNull(compileResult.Errors);
            Assert.AreEqual(1, compileResult.Errors.Count(), 1);
            Assert.AreEqual(error, compileResult.Errors.First().ToString());
            Assert.AreEqual(SqlCompileException.FormulaTooLong.Key, compileResult.Errors.First().MessageKey);
            Assert.IsNotNull(compileResult.SanitizedFormula);
        }

        [DataTestMethod]
        [DataRow("Price * Quantity", "new_price,new_quantity", DisplayName = "Main Entity")]
        [DataRow("ThisRecord.Price + Quantity", "new_price,new_quantity", DisplayName = "Main Entity ThisRecord")]
        [DataRow("Price + Other.Data", "new_price,otherid", "remote=>data", "local=>local_remote", DisplayName = "Lookup")]
        [DataRow("Other.'Other Other'.'Data Two' + Other.'Other Other'.'Other Other Other'.'Data Three'",
            "otherid",
            "remote=>otherotherid|doubleremote=>data2,otherotherotherid|tripleremote=>data3",
            "local=>local_remote|remote=>remote_doubleremote|doubleremote=>doubleremote_tripleremote",
            DisplayName = "Multiple levels of lookup")]
        [DataRow("'Self Reference'.Price + Other.Data",
            "new_price,otherid,selfid",
            "remote=>data",
            "local=>local_remote,self",
            DisplayName = "Multiple lookups")]
        [DataRow("'Logical Lookup'.Data",
            "logicalid",
            "remote=>data",
            "local=>logical",
            DisplayName = "Logical Lookup")]
        [DataRow("7 + 2", "", DisplayName = "Literals")]
        public void CompileIdentifiers(string expr, string topLevelFields, string relatedFields = null, string relationships = null)
        {
            var provider = new MockXrmMetadataProvider(RelationshipModels);
            var engine = new PowerFx2SqlEngine(RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider));
            var options = new SqlCompileOptions();
            var result = engine.Compile(expr, options);

            Assert.IsNotNull(result);
            Assert.AreEqual(topLevelFields, ToStableString(result.TopLevelIdentifiers));
            if (relatedFields != null)
            {
                Assert.AreEqual(relatedFields, string.Join('|', result.RelatedIdentifiers.ToArray().Select(pair => pair.Key + "=>" + ToStableString(pair.Value))));
            }
            else
            {
                Assert.AreEqual(0, result.RelatedIdentifiers.Count);
            }
            if (relationships != null)
            {
                Assert.AreEqual(relationships, string.Join('|', result.DependentRelationships.ToArray().Select(pair => pair.Key + "=>" + ToStableString(pair.Value))));
            }
            else
            {
                Assert.AreEqual(0, result.DependentRelationships.Count);
            }
        }

        [DataTestMethod]
        [DataRow("a in b", "Error 0-1: Only a literal value is supported for this argument.", DisplayName = "in")]
        [DataRow("a exactin b", "Error 0-1: Only a literal value is supported for this argument.", DisplayName = "exactin")]
        public void CheckInNonLiteral(string expr, string error)
        {
            var a = AttributeMetadataModel.NewString("a", "A");
            var b = AttributeMetadataModel.NewString("b", "B");
            var metadata = new EntityMetadataModel
            {
                LogicalName = "this",
                DisplayCollectionName = "Thises",
                Attributes = new AttributeMetadataModel[] { a, b }
            };

            var engine = new PowerFx2SqlEngine(metadata.ToXrm());
            var result = engine.Check(expr);

            Assert.IsNotNull(result.Errors);
            Assert.AreEqual(1, result.Errors.Count());
            Assert.AreEqual(error, result.Errors.First().ToString());
            Assert.AreEqual(SqlCompileException.LiteralArgRequired.Key, result.Errors.First().MessageKey);
        }

        [DataTestMethod]
        [DataRow("1 - UTCToday()", false, "Error 4-14: This argument cannot be passed as type Date in formula columns.", DisplayName = "Negation of date (coerce date to number then back to date)")]
        [DataRow("UTCNow() / \"2\"", false, "Error 0-8: This argument cannot be passed as type Number in formula columns.", DisplayName = "Coerce date to number in division operation (with coerced string)")]
        [DataRow("2 > UTCNow()", false, "Error 4-12: This argument cannot be passed as type Number in formula columns.", DisplayName = "Coerce date to number in left arg of logical operation")]
        [DataRow("UTCToday() <= 8.2E9", false, "Error 0-10: This argument cannot be passed as type Number in formula columns.", DisplayName = "Coerce date to number in right arg of logical operation")]
        [DataRow("UTCToday() = 8.2E9", false, "Error 0-10: This argument cannot be passed as type Number in formula columns.", DisplayName = "Coerce date to number in right arg of equals")]
        [DataRow("UTCToday() <> 8.2E9", false, "Error 0-10: This argument cannot be passed as type Number in formula columns.", DisplayName = "Coerce date to number right arg of not equals")]
        [DataRow("Abs(UTCToday())", false, "Error 4-14: This argument cannot be passed as type Number in formula columns.", DisplayName = "Coerce date to number in Abs function")]
        [DataRow("Power(UTCNow(), 2)", false, "Error 6-14: This argument cannot be passed as type Number in formula columns.", DisplayName = "Coerce date to number in Power function")]
        [DataRow("Max(1, UTCNow())", false, "Error 7-15: This argument cannot be passed as type Number in formula columns.", DisplayName = "Coerce date to number in Max function")]
        [DataRow("Trunc(UTCToday(), UTCNow())", false, "Error 6-16: This argument cannot be passed as type Number in formula columns.", DisplayName = "Coerce date to number in Trunc function")]
        [DataRow("Left(\"foo\", UTCNow())", false, "Error 12-20: This argument cannot be passed as type Number in formula columns.", DisplayName = "Coerce date to number in Left function")]
        [DataRow("Replace(\"abcabcabc\", UTCToday(), UTCNow(), \"xx\")", false, "Error 21-31: This argument cannot be passed as type Number in formula columns.", DisplayName = "Coerce date to number in first numeric arg in Replace function")]
        [DataRow("Replace(\"abcabcabc\", 5, UTCNow(), \"xx\")", false, "Error 24-32: This argument cannot be passed as type Number in formula columns.", DisplayName = "Coerce date to number in second numeric arg in Replace function")]
        [DataRow("Substitute(\"abcabcabc\", \"ab\", \"xx\", UTCNow())", false, "Error 36-44: This argument cannot be passed as type Number in formula columns.", DisplayName = "Coerce date to number in Substitute function")]
        public void CheckCoercionFailures(string expr, bool success, string message = null)
        {
            var engine = new PowerFx2SqlEngine();
            var result = engine.Check(expr);

            Assert.IsNotNull(result);
            Assert.AreEqual(success, result.IsSuccess);
            if (!success)
            {
                Assert.IsNotNull(result.Errors);
                Assert.AreEqual(1, result.Errors.Count());
                Assert.AreEqual(message, result.Errors.First().ToString());
                Assert.AreEqual(SqlCompileException.ArgumentTypeNotSupported.Key, result.Errors.First().MessageKey);
            }
        }

        [DataTestMethod]
        [DataRow("Text(123, \",###.0\")", false, "Error 10-18: Locale-specific formatting tokens such as \".\" and \",\" are not supported in formula columns.", DisplayName = "Locale-specific separators not supported")]
        [DataRow("Text(123, \"\\,###\\.\")", false, "Error 10-19: Locale-specific formatting tokens such as \".\" and \",\" are not supported in formula columns.", DisplayName = "Escaped locale-specific separators not supported")]
        [DataRow("Text(123, \"#\", \"fr-FR\")", false, "Error 15-22: The language argument is not supported for the Text function in formula columns.", DisplayName = "Localization parameter")]
        [DataRow("Text(123, \"[$-fr-FR]#\")", false, "Error 10-22: Locale-specific formatting tokens such as \".\" and \",\" are not supported in formula columns.", DisplayName = "Locale token at start of format string not supported")]
        [DataRow("Text(123, \"#[$-fr-FR]\")", false, "Error 10-22: Locale-specific formatting tokens such as \".\" and \",\" are not supported in formula columns.", DisplayName = "Locale token in format string not supported")]
        [DataRow("Text(123, \"#\\[$-fr-FR]\")", false, "Error 10-23: Locale-specific formatting tokens such as \".\" and \",\" are not supported in formula columns.", DisplayName = "Escaped Locale token in format string not supported")]
        [DataRow("Text(123, \"#\" & \".0\")", false, "Error 14-15: Only a literal value is supported for this argument.", DisplayName = "Non-literal format string")]
        [DataRow("Int(\"123\")", true, DisplayName = "Int on string")]
        public void CheckTextFailures(string expr, bool success, string message = null)
        {
            var engine = new PowerFx2SqlEngine();
            var result = engine.Check(expr);

            Assert.IsNotNull(result);
            Assert.AreEqual(success, result.IsSuccess);
            if (!success)
            {
                Assert.IsNotNull(result.Errors);
                Assert.AreEqual(1, result.Errors.Count());
                Assert.AreEqual(message, result.Errors.First().ToString());
            }
        }

        [DataTestMethod]
        [DataRow("UTCNow()", true, typeof(DateTimeNoTimeZoneType), DisplayName = "UTCNow")]
        [DataRow("UTCToday()", true, typeof(DateTimeNoTimeZoneType), DisplayName = "UTCToday")]
        [DataRow("IsUTCToday(UTCNow())", true, typeof(BooleanType), DisplayName = "IsUTCToday of UTCNow")]
        [DataRow("Now()", false, null, "Error 0-5: Now is not supported in formula columns, use UTCNow instead.", DisplayName = "Now not supported")]
        [DataRow("Today()", false, null, "Error 0-7: Today is not supported in formula columns, use UTCToday instead.", DisplayName = "Today not supported")]
        [DataRow("IsToday(Today())", false, null, "Error 0-16: IsToday is not supported in formula columns, use IsUTCToday instead.", DisplayName = "IsToday not supported")]
        [DataRow("IsUTCToday(UTCToday())", true, typeof(BooleanType), DisplayName = "IsUTCToday of UTCToday")]
        [DataRow("UTCToday() = UTCNow()", true, typeof(BooleanType), DisplayName = "= UTCToday UTCNow")]
        [DataRow("IsUTCToday(tziDateOnly)", true, typeof(BooleanType), DisplayName = "IsUTCToday of TZI Date Only")]
        [DataRow("IsUTCToday(dateOnly)", true, typeof(BooleanType), DisplayName = "IsUTCToday of Date Only")]
        [DataRow("IsUTCToday(userLocalDateTime)", true, typeof(BooleanType), DisplayName = "IsUTCToday of User Local Date Time")]
        [DataRow("userLocalDateTime", true, typeof(DateTimeType), DisplayName = "User Local Date Time")]
        [DataRow("userLocalDateOnly", true, typeof(DateTimeType), DisplayName = "User Local Date Only")]
        [DataRow("dateOnly", true, typeof(DateType), DisplayName = "Date Only")]
        [DataRow("tziDateTime", true, typeof(DateTimeNoTimeZoneType), DisplayName = "TZI Date Time")]
        [DataRow("tziDateOnly", true, typeof(DateTimeNoTimeZoneType), DisplayName = "TZI Date Only")]
        [DataRow("dateOnly + 0.25", true, typeof(DateType), DisplayName = "DateOnly add fractional day")]
        [DataRow("DateAdd(dateOnly, 1, TimeUnit.Days)", true, typeof(DateType), DisplayName = "DateAdd Days Date Only")]
        [DataRow("DateAdd(dateOnly, 1, TimeUnit.Hours)", true, typeof(DateType), DisplayName = "DateAdd Hours Date Only")]
        [DataRow("DateAdd(tziDateOnly, 1, TimeUnit.Hours)", true, typeof(DateTimeNoTimeZoneType), DisplayName = "DateAdd TZI Date Only")]
        [DataRow("DateAdd(userLocalDateOnly, 1, TimeUnit.Hours)", true, typeof(DateTimeType), DisplayName = "DateAdd User Local Date Only")]
        [DataRow("If(true, tziDateOnly, dateOnly)", true, typeof(DateTimeNoTimeZoneType), DisplayName = "If TZI Date Only vs. Date Only")]
        [DataRow("If(true, userLocalDateTime, userLocalDateOnly)", true, typeof(DateTimeType), DisplayName = "If User Local Date Time vs. User Local Date Only")]
        [DataRow("If(true, tziDateOnly, tziDateTime)", true, typeof(DateTimeNoTimeZoneType), DisplayName = "If TZI Date Only vs. TZI Date Time")]
        [DataRow("Switch(1, 1, userLocalDateOnly, userLocalDateTime)", true, typeof(DateTimeType), DisplayName = "Switch UserLocal Date Only vs. User Local Date Time")]
        [DataRow("Switch(1, 2, tziDateOnly, userLocalDateOnly)", false, null, "Error 26-43: This operation cannot be performed on values which are of different Date Time Behaviors.", DisplayName = "Switch TZI Date Only vs. User Local Date Only")]
        [DataRow("Switch(1, 2, dateOnly, dateOnly)", true, typeof(DateType), DisplayName = "Switch Date Only vs. Date Only")]
        [DataRow("Text(tziDateOnly)", false, null, "Error 5-16: This argument cannot be passed as type DateTimeNoTimeZone in formula columns.", DisplayName = "Text for TZI Date Only")]
        [DataRow("Text(userLocalDateTime)", false, null, "Error 5-22: This argument cannot be passed as type DateTime in formula columns.", DisplayName = "Text for User Local Date Time")]
        [DataRow("Text(UTCNow())", false, null, "Error 5-13: This argument cannot be passed as type DateTimeNoTimeZone in formula columns.", DisplayName = "Text for UTCNow")]
        [DataRow("DateDiff(userLocalDateTime, tziDateOnly)", false, null, "Error 0-40: This operation cannot be performed on values which are of different Date Time Behaviors.", DisplayName = "DateDiff User Local Date Time vs TZI Date Only")]
        [DataRow("DateDiff(dateOnly, tziDateOnly)", true, typeof(SqlDecimalType), DisplayName = "DateDiff Date Only vs TZI Date Only")]
        [DataRow("DateDiff(userLocalDateOnly, dateOnly)", false, null, "Error 0-37: This operation cannot be performed on values which are of different Date Time Behaviors.", DisplayName = "DateDiff User Local Date Only vs Date Only")]
        [DataRow("DateDiff(userLocalDateOnly, userLocalDateTime)", true, typeof(SqlDecimalType), DisplayName = "DateDiff User Local Date Only vs User Local Date Time")]
        [DataRow("userLocalDateTime > userLocalDateOnly", true, typeof(BooleanType), DisplayName = "> User Local Date Time vs. User Local Date Only")]
        [DataRow("tziDateTime <> tziDateOnly", true, typeof(BooleanType), DisplayName = "<> TZI Date Time vs. TZI Date Only")]
        [DataRow("UTCToday() = tziDateOnly", true, typeof(BooleanType), DisplayName = "= UTCToday vs. TZI Date Only")]
        [DataRow("UTCToday() = dateOnly", true, typeof(BooleanType), DisplayName = "= UTCToday vs. Date Only")]
        // TODO: the span for operations is potentially incorrect in the IR: it is only the operator, and not the operands
        [DataRow("tziDateTime = userLocalDateOnly", false, null, "Error 12-13: This operation cannot be performed on values which are of different Date Time Behaviors.", DisplayName = "= TZI Date Time vs. User Local Date Only")]
        [DataRow("dateOnly <= userLocalDateOnly", false, null, "Error 9-11: This operation cannot be performed on values which are of different Date Time Behaviors.", DisplayName = "<= Date Only vs. User Local Date Only")]
        [DataRow("Day(dateOnly)", true, typeof(SqlDecimalType), DisplayName = "Day of Date Only")]
        [DataRow("Day(userLocalDateOnly)", false, null, "Error 0-22: Day cannot be performed on this input without a time zone conversion, which is not supported in formula columns.", DisplayName = "Day of User Local Date Only")]
        [DataRow("WeekNum(dateOnly)", true, typeof(SqlDecimalType))]
        [DataRow("WeekNum(tziDateTime)", true, typeof(SqlDecimalType))]
        [DataRow("WeekNum(tziDateOnly)", true, typeof(SqlDecimalType))]
        [DataRow("WeekNum(userLocalDateOnly)", false, typeof(SqlDecimalType), "Error 0-26: WeekNum cannot be performed on this input without a time zone conversion, which is not supported in formula columns.")]
        [DataRow("WeekNum(userLocalDateTime)", false, typeof(SqlDecimalType), "Error 0-26: WeekNum cannot be performed on this input without a time zone conversion, which is not supported in formula columns.")]
        [DataRow("WeekNum(dateOnly, 2)", false, typeof(SqlDecimalType), "Error 18-19: The start_of_week argument is not supported for the WeekNum function in formula columns.")]
        public void CompileSqlDateTimeBehaviors(string expr, bool success, Type returnType, params string[] errors)
        {
            var model = new EntityMetadataModel
            {
                LogicalName = "local",
                Attributes = new AttributeMetadataModel[]
                {
                    AttributeMetadataModel.NewDateTime("userLocalDateTime", "UserLocalDateTime", DateTimeBehavior.UserLocal, DateTimeFormat.DateAndTime),
                    AttributeMetadataModel.NewDateTime("userLocalDateOnly", "UserLocalDateOnly", DateTimeBehavior.UserLocal, DateTimeFormat.DateOnly),
                    AttributeMetadataModel.NewDateTime("dateOnly", "DateOnly", DateTimeBehavior.DateOnly, DateTimeFormat.DateOnly),
                    AttributeMetadataModel.NewDateTime("tziDateTime", "TimeZoneIndependentDateTime", DateTimeBehavior.TimeZoneIndependent, DateTimeFormat.DateAndTime),
                    AttributeMetadataModel.NewDateTime("tziDateOnly", "TimeZoneIndependentDateOnly", DateTimeBehavior.TimeZoneIndependent, DateTimeFormat.DateOnly)
                }
            };

            var metadata = model.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);

            AssertReturnTypeOrError(engine, expr, success, returnType, errors);
        }

        [TestMethod]
        public void CheckDuplicateOptionSets()
        {
            var localModel = new EntityMetadataModel
            {
                LogicalName = "local",
                PrimaryIdAttribute = "localid",
                Attributes = new AttributeMetadataModel[]
                {
                    AttributeMetadataModel.NewGuid("localid", "LocalId"),
                    AttributeMetadataModel.NewPicklist("global1", "Picklist", new OptionMetadataModel[]
                    {
                        new OptionMetadataModel { Label = "Eeny", Value = 1 },
                        new OptionMetadataModel { Label = "Meany", Value = 2 },
                        new OptionMetadataModel { Label = "Miney", Value = 3 },
                        new OptionMetadataModel { Label = "Moe", Value = 4 }
                    },
                    isGlobal: true),
                    AttributeMetadataModel.NewPicklist("global2", "Picklist", new OptionMetadataModel[]
                    {
                        new OptionMetadataModel { Label = "Eeny", Value = 1 },
                        new OptionMetadataModel { Label = "Meany", Value = 2 },
                        new OptionMetadataModel { Label = "Miney", Value = 3 },
                        new OptionMetadataModel { Label = "Moe", Value = 4 }
                    },
                    isGlobal: true)
                }
            };

            var engine = new PowerFx2SqlEngine(localModel.ToXrm());
            var result = engine.Check("'Picklist (global1)' = [@Picklist].Eeny || 'Picklist (global2)' = [@Picklist].Miney");

            Assert.AreEqual(true, result.IsSuccess);
        }

        [TestMethod]
        public void CheckOptionSetsCollidingDisplayAndLogicalNames()
        {
            var localModel = new EntityMetadataModel
            {
                LogicalName = "local",
                PrimaryIdAttribute = "localid",
                Attributes = new AttributeMetadataModel[]
                {
                    AttributeMetadataModel.NewGuid("localid", "LocalId"),
                    AttributeMetadataModel.NewPicklist("global1", "Picklist", new OptionMetadataModel[]
                    {
                        new OptionMetadataModel { Label = "4", Value = 1 },
                        new OptionMetadataModel { Label = "3", Value = 2 },
                        new OptionMetadataModel { Label = "2", Value = 3 },
                        new OptionMetadataModel { Label = "1", Value = 4 }
                    },
                    isGlobal: true),
                    AttributeMetadataModel.NewPicklist("global2", "Picklist", new OptionMetadataModel[]
                    {
                        new OptionMetadataModel { Label = "4", Value = 1 },
                        new OptionMetadataModel { Label = "3", Value = 2 },
                        new OptionMetadataModel { Label = "2", Value = 3 },
                        new OptionMetadataModel { Label = "1", Value = 4 }
                    },
                    isGlobal: true)
                }
            };

            var engine = new PowerFx2SqlEngine(localModel.ToXrm());
            var result = engine.Check("'Picklist (global1)' = [@Picklist].'4 (1)' || 'Picklist (global2)' = [@Picklist].'2 (3)'");

            Assert.AreEqual(true, result.IsSuccess);
        }


        [TestMethod]
        public void CheckOptionSetsCollidingDisplayNames()
        {
            var localModel = new EntityMetadataModel
            {
                LogicalName = "local",
                PrimaryIdAttribute = "localid",
                Attributes = new AttributeMetadataModel[]
                {
                    AttributeMetadataModel.NewGuid("localid", "LocalId"),
                    AttributeMetadataModel.NewPicklist("global1", "Picklist", new OptionMetadataModel[]
                    {
                        new OptionMetadataModel { Label = "Eeny", Value = 1 },
                        new OptionMetadataModel { Label = "Eeny", Value = 2 },
                        new OptionMetadataModel { Label = "Eeny", Value = 3 },
                        new OptionMetadataModel { Label = "Eeny", Value = 4 }
                    },
                    isGlobal: true),
                    AttributeMetadataModel.NewPicklist("global2", "Picklist", new OptionMetadataModel[]
                    {
                        new OptionMetadataModel { Label = "Eeny", Value = 1 },
                        new OptionMetadataModel { Label = "Eeny", Value = 2 },
                        new OptionMetadataModel { Label = "Eeny", Value = 3 },
                        new OptionMetadataModel { Label = "Eeny", Value = 4 }
                    },
                    isGlobal: true)
                }
            };

            var engine = new PowerFx2SqlEngine(localModel.ToXrm());
            var result = engine.Check("'Picklist (global1)' = [@Picklist].'Eeny (1)' || 'Picklist (global2)' = [@Picklist].'Eeny (3)'");

            Assert.AreEqual(true, result.IsSuccess);
        }

        [DataTestMethod]
        [DataRow("Float", false, "Error 0-5: Columns of type Double are not supported in formula columns.", DisplayName = "Local Float")]
        [DataRow("Other.Float", true, DisplayName = "Remote non-float with name collision")]
        [DataRow("Other.'Actual Float'", false, "Error 5-20: Columns of type Double are not supported in formula columns.", DisplayName = "Remote float")]
        public void CheckFloatingPoint(string expr, bool success, string error = null)
        {
            var provider = new MockXrmMetadataProvider(RelationshipModels);
            var engine = new PowerFx2SqlEngine(RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider));
            var options = new SqlCompileOptions();
            var result = engine.Compile(expr, options);

            Assert.AreEqual(success, result.IsSuccess);

            if (error == null)
            {
                Assert.AreEqual(0, result.Errors.Count());
            }
            else
            {
                Assert.IsNotNull(result.Errors);
                Assert.AreEqual(1, result.Errors.Count());
                Assert.AreEqual(error, result.Errors.First().ToString());
                Assert.IsNotNull(result.SanitizedFormula);
            }
        }

        [DataTestMethod]
        [DataRow("'Virtual Lookup'", "Error 0-16: The result type Record is not supported in formula columns.", DisplayName = "Direct virtual lookup access")]
        [DataRow("'Virtual Lookup'.'Virtual Data'", "Error 16-31: Cannot reference virtual table Virtual Remotes in formula columns.", DisplayName = "Virtual lookup field access")]
        public void CheckVirtualLookup(string expr, params string[] errors)
        {
            var provider = new MockXrmMetadataProvider(RelationshipModels);
            var engine = new PowerFx2SqlEngine(RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider));
            AssertReturnTypeOrError(engine, expr, false, null, errors);
        }

        [TestMethod]
        public void CompileLogicalLookup()
        {
            var provider = new MockXrmMetadataProvider(RelationshipModels);
            var engine = new PowerFx2SqlEngine(RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider));
            var options = new SqlCompileOptions { UdfName = "fn_udf_Logical" };
            var result = engine.Compile("'Logical Lookup'.Data", options);

            Assert.IsTrue(result.IsSuccess);
            // the SqlCreateRow has an embedded newline
            Assert.AreEqual(@"fn_udf_Logical([localid])
", result.SqlCreateRow);
        }

        /// <summary>
        /// Models used to run relationship tests
        /// </summary>
        internal static readonly EntityMetadataModel LocalModel = new EntityMetadataModel
        {
            LogicalName = "local",
            DisplayCollectionName = "Locals",
            PrimaryIdAttribute = "localid",
            Attributes = new AttributeMetadataModel[]
            {
                AttributeMetadataModel.NewDecimal("conflict1", "Conflict"),
                AttributeMetadataModel.NewDecimal("conflict2", "Conflict"),
                AttributeMetadataModel.NewDecimal("new_price", "Price"),
                AttributeMetadataModel.NewDecimal("new_quantity", "Quantity"),
                AttributeMetadataModel.NewLookup("otherid", "Other", new string[] { "remote" }),
                AttributeMetadataModel.NewLookup("selfid", "Self Reference", new string[] { "local" }),
                AttributeMetadataModel.NewLookup("virtualid", "Virtual Lookup", new string[] { "virtualremote" }),
                AttributeMetadataModel.NewLookup("logicalid", "Logical Lookup", new string[] { "remote" }).SetLogical(),
                AttributeMetadataModel.NewGuid("localid", "LocalId"),
                AttributeMetadataModel.NewDouble("float", "Float"),
                AttributeMetadataModel.NewPicklist("rating", "Rating", new OptionMetadataModel[]
                {
                    new OptionMetadataModel { Label = "Hot", Value = 1 },
                    new OptionMetadataModel { Label = "Warm", Value = 2 },
                    new OptionMetadataModel { Label = "Cold", Value = 3 }
                }),
                AttributeMetadataModel.NewPicklist("global_pick", "Global Picklist", new OptionMetadataModel[]
                {
                    new OptionMetadataModel { Label = "High", Value = 1 },
                    new OptionMetadataModel { Label = "Medium", Value = 2 },
                    new OptionMetadataModel { Label = "Low", Value = 3 }
                },
                isGlobal: true)
            },
            ManyToOneRelationships = new OneToManyRelationshipMetadataModel[]
            {
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "remoteid",
                    ReferencedEntity = "remote",
                    ReferencingAttribute = "otherid",
                    ReferencingEntity = "local",
                    ReferencedEntityNavigationPropertyName = "refd",
                    ReferencingEntityNavigationPropertyName = "refg",
                    SchemaName = "local_remote"
                },
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "localid",
                    ReferencedEntity = "local",
                    ReferencingAttribute = "selfid",
                    ReferencingEntity = "local",
                    ReferencedEntityNavigationPropertyName = "self_refd",
                    ReferencingEntityNavigationPropertyName = "self",
                    SchemaName = "self"
                },
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "virtualremoteid",
                    ReferencedEntity = "virtualremote",
                    ReferencingAttribute = "virtualid",
                    ReferencingEntity = "local",
                    ReferencedEntityNavigationPropertyName = "virtual_refd",
                    ReferencingEntityNavigationPropertyName = "virtual",
                    SchemaName = "virtual"
                },
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "remoteid",
                    ReferencedEntity = "remote",
                    ReferencingAttribute = "logicalid",
                    ReferencingEntity = "local",
                    ReferencedEntityNavigationPropertyName = "logical_refd",
                    ReferencingEntityNavigationPropertyName = "logical",
                    SchemaName = "logical"
                }
            },
            OneToManyRelationships = new OneToManyRelationshipMetadataModel[]
            {
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "localid",
                    ReferencedEntity = "local",
                    ReferencingAttribute = "selfid",
                    ReferencingEntity = "local",
                    ReferencedEntityNavigationPropertyName = "self_refd",
                    ReferencingEntityNavigationPropertyName = "self",
                    SchemaName = "self"
                }
            }
        };

        internal static readonly EntityMetadataModel RemoteModel = new EntityMetadataModel
        {
            LogicalName = "remote",
            DisplayCollectionName = "Remotes",
            PrimaryIdAttribute = "remoteid",
            Attributes = new AttributeMetadataModel[]
            {
                AttributeMetadataModel.NewDecimal("data", "Data"),
                AttributeMetadataModel.NewGuid("remoteid", "RemoteId"),
                AttributeMetadataModel.NewDecimal("calc", "Calculated Data").SetCalculated(),
                AttributeMetadataModel.NewDecimal("float", "Float"),
                AttributeMetadataModel.NewDouble("actual_float", "Actual Float"),
                AttributeMetadataModel.NewPicklist("rating", "Rating", new OptionMetadataModel[]
                {   new OptionMetadataModel { Label = "Small", Value = 1},
                    new OptionMetadataModel { Label = "Medium", Value = 2 },
                    new OptionMetadataModel { Label = "Large", Value = 3 }
                }),
                AttributeMetadataModel.NewLookup("otherotherid", "Other Other", new string[] { "doubleremote" }),
                AttributeMetadataModel.NewDouble("other", "Other")
            },
            ManyToOneRelationships = new OneToManyRelationshipMetadataModel[]
            {
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "doubleremoteid",
                    ReferencedEntity = "doubleremote",
                    ReferencingAttribute = "otherotherid",
                    ReferencingEntity = "remote",
                    ReferencedEntityNavigationPropertyName = "doublerefd",
                    ReferencingEntityNavigationPropertyName = "doublerefg",
                    SchemaName = "remote_doubleremote"
                }
            }
        };

        internal static readonly EntityMetadataModel DoubleRemoteModel = new EntityMetadataModel
        {
            LogicalName = "doubleremote",
            DisplayCollectionName = "Double Remotes",
            PrimaryIdAttribute = "doubleremoteid",
            Attributes = new AttributeMetadataModel[]
            {
                AttributeMetadataModel.NewDecimal("data2", "Data Two"),
                AttributeMetadataModel.NewGuid("doubleremoteid", "DoubleRemoteId"),
                AttributeMetadataModel.NewLookup("otherotherotherid", "Other Other Other", new string[] { "tripleremote" })
            },
            ManyToOneRelationships = new OneToManyRelationshipMetadataModel[]
            {
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "tripleremoteid",
                    ReferencedEntity = "tripleremote",
                    ReferencingAttribute = "otherotherotherid",
                    ReferencingEntity = "doubleremote",
                    ReferencedEntityNavigationPropertyName = "triplerefd",
                    ReferencingEntityNavigationPropertyName = "triplerefg",
                    SchemaName = "doubleremote_tripleremote"
                }
            }
        };

        internal static readonly EntityMetadataModel TripleRemoteModel = new EntityMetadataModel
        {
            LogicalName = "tripleremote",
            DisplayCollectionName = "Triple Remotes",
            PrimaryIdAttribute = "tripleremoteid",
            Attributes = new AttributeMetadataModel[]
            {
                AttributeMetadataModel.NewDecimal("data3", "Data Three"),
                AttributeMetadataModel.NewGuid("tripleremoteid", "TripleRemoteId"),
            }
        };

        internal static readonly EntityMetadataModel VirtualRemoteModel = new EntityMetadataModel
        {
            LogicalName = "virtualremote",
            DisplayCollectionName = "Virtual Remotes",
            PrimaryIdAttribute = "virtualremoteid",
            Attributes = new AttributeMetadataModel[]
            {
                AttributeMetadataModel.NewDecimal("vdata", "Virtual Data"),
                AttributeMetadataModel.NewGuid("virtualremoteid", "VirtualRemoteId")
            }
        }.SetVirtual();

        internal static readonly EntityMetadataModel[] RelationshipModels = new EntityMetadataModel[] { LocalModel, RemoteModel, DoubleRemoteModel, TripleRemoteModel, VirtualRemoteModel };

        internal static readonly EntityMetadataModel AllAttributeModel = new EntityMetadataModel
        {
            LogicalName = "allattributes",
            DisplayCollectionName = "All Attributes",
            PrimaryIdAttribute = "allid",
            Attributes = new AttributeMetadataModel[]
            {
                AttributeMetadataModel.NewDecimal("new_field", "field"),
                AttributeMetadataModel.NewDouble("double", "Double"),
                AttributeMetadataModel.NewInteger("int", "Int"),
                AttributeMetadataModel.NewLookup("new_lookup", "Lookup", new [] { "tripleremote" }),
                AttributeMetadataModel.NewLookup("selfid", "Self Reference", new [] { "allattributes" }),
                AttributeMetadataModel.NewMoney("money", "Money"),
                AttributeMetadataModel.NewGuid("guid", "Guid"),
                AttributeMetadataModel.NewGuid("allid", "AllId"),
                AttributeMetadataModel.NewString("string", "String"),
                AttributeMetadataModel.NewString("hyperlink", "Hyperlink", StringFormat.Url),
                AttributeMetadataModel.NewString("email", "Email", StringFormat.Email),
                AttributeMetadataModel.NewString("ticker", "Ticker", StringFormat.TickerSymbol),
                AttributeMetadataModel.NewInteger("timezone", "TimeZone", IntegerFormat.TimeZone),
                AttributeMetadataModel.NewInteger("duration", "Duration", IntegerFormat.Duration),
                AttributeMetadataModel.NewDateTime("userlocaldatetime", "UserLocal DateTime", DateTimeBehavior.UserLocal, DateTimeFormat.DateAndTime),
                AttributeMetadataModel.NewDateTime("userlocaldateonly", "UserLocal DateOnly", DateTimeBehavior.UserLocal, DateTimeFormat.DateOnly),
                AttributeMetadataModel.NewDateTime("dateonly", "DateOnly", DateTimeBehavior.DateOnly, DateTimeFormat.DateOnly),
                AttributeMetadataModel.NewDateTime("timezoneindependentdatetime", "TimeZoneIndependent DateTime", DateTimeBehavior.TimeZoneIndependent, DateTimeFormat.DateAndTime),
                AttributeMetadataModel.NewDateTime("timezoneindependentdateonly", "TimeZoneIndependent DateOnly", DateTimeBehavior.TimeZoneIndependent, DateTimeFormat.DateOnly),
                new AttributeMetadataModel
                {
                    LogicalName= "bigint",
                    DisplayName = "BigInt",
                    AttributeType = AttributeTypeCode.BigInt
                },
                AttributeMetadataModel.NewBoolean("boolean", "Boolean", "Yes", "No"),
                AttributeMetadataModel.NewLookup("customerid", "Customer", new [] { "tripleremote" }, AttributeTypeCode.Customer),
                new AttributeMetadataModel
                {
                    LogicalName = "EntityName",
                    DisplayName = "EntityName",
                    AttributeType = AttributeTypeCode.EntityName
                },
                new AttributeMetadataModel
                {
                    LogicalName = "Memo",
                    DisplayName = "Memo",
                    AttributeType = AttributeTypeCode.Memo
                },
                AttributeMetadataModel.NewLookup("ownerid", "Owner", new [] { "tripleremote" }, AttributeTypeCode.Owner),
                AttributeMetadataModel.NewPicklist(
                    "statecode",
                    "State",
                    new OptionMetadataModel[]
                    {
                        new OptionMetadataModel
                        {
                            Label = "Active",
                            Value = 1
                        },
                        new OptionMetadataModel
                        {
                            Label = "Inactive",
                            Value = 2
                        }
                    },
                    AttributeTypeCode.State),
                AttributeMetadataModel.NewPicklist(
                    "statuscode",
                    "Status",
                    new OptionMetadataModel[]
                    {
                        new OptionMetadataModel
                        {
                            Label = "Active",
                            Value = 1
                        },
                        new OptionMetadataModel
                        {
                            Label = "Inactive",
                            Value = 2
                        }
                    },
                    AttributeTypeCode.Status),
                AttributeMetadataModel.NewPicklist(
                    "picklist",
                    "Picklist",
                    new OptionMetadataModel[]
                    {
                        new OptionMetadataModel
                        {
                            Label = "One",
                            Value = 1
                        },
                        new OptionMetadataModel
                        {
                            Label = "Two",
                            Value = 2
                        },
                        new OptionMetadataModel
                        {
                            Label = "Three",
                            Value = 3
                        }
                    }),
                AttributeMetadataModel.NewPicklist(
                    "multiSelect",
                    "MultiSelect",
                    new OptionMetadataModel[]
                    {
                        new OptionMetadataModel
                        {
                            Label = "Eight",
                            Value = 8
                        },
                        new OptionMetadataModel
                        {
                            Label = "Nine",
                            Value = 9
                        },
                        new OptionMetadataModel
                        {
                            Label = "Ten",
                            Value = 10
                        }
                    },
                    typeName: AttributeTypeDisplayName.MultiSelectPicklistType),
                AttributeMetadataModel.NewImage("image", "Image"),
                AttributeMetadataModel.NewFile("file", "File")
            },
            ManyToOneRelationships = new OneToManyRelationshipMetadataModel[]
            {
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "tripleremoteid",
                    ReferencedEntity = "tripleremote",
                    ReferencingAttribute = "new_lookup",
                    ReferencingEntity = "allattributes",
                    ReferencedEntityNavigationPropertyName = "lookup_refd",
                    ReferencingEntityNavigationPropertyName = "lookup",
                    SchemaName = "all_tripleremote"
                },
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "allid",
                    ReferencedEntity = "allattributes",
                    ReferencingAttribute = "selfid",
                    ReferencingEntity = "allattributes",
                    ReferencedEntityNavigationPropertyName = "self_refd",
                    ReferencingEntityNavigationPropertyName = "self",
                    SchemaName = "self"
                },
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "ownerid",
                    ReferencedEntity = "tripleremote",
                    ReferencingAttribute = "ownerid",
                    ReferencingEntity = "allattributes",
                    ReferencedEntityNavigationPropertyName = "owner_allattributes",
                    ReferencingEntityNavigationPropertyName = "ownerid",
                    SchemaName = "owner_allattributes"
                },
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "accountid",
                    ReferencedEntity = "tripleremote",
                    ReferencingAttribute = "customerid",
                    ReferencingEntity = "allattributes",
                    ReferencedEntityNavigationPropertyName = "allattributes_customer_account",
                    ReferencingEntityNavigationPropertyName = "customerid_account",
                    SchemaName = "allattributes_customer_account"
                },
            }
        };

        internal static readonly EntityMetadataModel[] AllAttributeModels = new EntityMetadataModel[] { AllAttributeModel, TripleRemoteModel };

        [DataTestMethod]
        [DataRow("new_price * new_quantity", "Price * Quantity", DisplayName = "Logical Names")]
        [DataRow("ThisRecord.new_price + new_quantity", "ThisRecord.Price + Quantity", DisplayName = "ThisRecord")]
        [DataRow("conflict1 + conflict2", "'Conflict (conflict1)' + 'Conflict (conflict2)'", DisplayName = "Conflict")]
        [DataRow("new_price + refg.data", "Price + Other.Data", DisplayName = "Lookup")]
        [DataRow("refg.data + refg.doublerefg.data2 + refg.doublerefg.triplerefg.data3", "Other.Data + Other.'Other Other'.'Data Two' + Other.'Other Other'.'Other Other Other'.'Data Three'", DisplayName = "Multiple Lookups")]
        [DataRow("refg.data + self.new_price", "Other.Data + 'Self Reference'.Price", DisplayName = "Self Reference")]
        [DataRow("If(true, \"random string\", Text(new_price))", "If(true, \"random string\", Text(Price))", DisplayName = "Function")]
        [DataRow("If(rating = local_rating_optionSet.'1', new_quantity, new_price)", "If(Rating = 'Rating (Locals)'.Hot, Quantity, Price)", DisplayName = "CDS Enum literal")]
        [DataRow("If(global_pick = [@global_global_pick_optionSet].'2', new_quantity, new_price)", "If('Global Picklist' = [@'Global Picklist'].Medium, Quantity, Price)", DisplayName = "CDS Global Enum literal")]
        [DataRow("DateAdd(UTCToday(), new_quantity, TimeUnit.Months)", "DateAdd(UTCToday(), Quantity, TimeUnit.Months)", DisplayName = "Enum literal")]
        [DataRow("/* Comment */\n\n\t  conflict1\n\n\t  \n -conflict2", "/* Comment */\n\n\t  'Conflict (conflict1)'\n\n\t  \n -'Conflict (conflict2)'", DisplayName = "Preserves whitespace and comments")]
        public void Translate(string expr, string translation)
        {
            var provider = new MockXrmMetadataProvider(RelationshipModels);
            var engine = new PowerFx2SqlEngine(RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider));
            var actualTranslation = engine.ConvertToDisplay(expr);
            Assert.AreEqual(translation, actualTranslation);

            // compile the translated expression and ensure it matches the original logical expression
            var result = engine.Compile(actualTranslation, new SqlCompileOptions());
            Assert.AreEqual(expr, result.LogicalFormula);
        }

        [DataTestMethod]
        [DataRow("Price * Quantity", "#$FieldDecimal$# * #$FieldDecimal$#", DisplayName = "Display Names")]
        [DataRow("new_price * new_quantity", "#$FieldDecimal$# * #$FieldDecimal$#", DisplayName = "Logical Names")]
        [DataRow("\"John Smith\"", "#$string$#", DisplayName = "String literal")]
        [DataRow("123456", "#$number$#", DisplayName = "Numeric literal")]
        [DataRow("If(true,\"John Smith\",Price+7)", "If(#$boolean$#, #$string$#, #$FieldDecimal$# + #$number$#)", DisplayName = "Function with boolean literal")]
        [DataRow("Text(123, \"0000\")", "Text(#$number$#, #$string$#)", DisplayName = "Text with format string")]
        [DataRow("If(123,\"Foo\"", "If(#$number$#, #$string$#)", DisplayName = "Invalid formula - cleaned up")]
        [DataRow("'Conflict (conflict1)' + 'Conflict (conflict2)'", "#$FieldDecimal$# + #$FieldDecimal$#", DisplayName = "Conflict")]
        [DataRow("Price + Other.Data", "#$FieldDecimal$# + #$FieldLookup$#.#$FieldDecimal$#", DisplayName = "Lookup")]
        [DataRow("Other.Data + Other.'Other Other'.'Data Two' + Other.'Other Other'.'Other Other Other'.'Data Three'", "#$FieldLookup$#.#$FieldDecimal$# + #$FieldLookup$#.#$FieldLookup$#.#$FieldDecimal$# + #$FieldLookup$#.#$FieldLookup$#.#$FieldLookup$#.#$FieldDecimal$#", DisplayName = "Multiple Lookups")]
        [DataRow("Other.Data + 'Self Reference'.Price", "#$FieldLookup$#.#$FieldDecimal$# + #$FieldLookup$#.#$FieldDecimal$#", DisplayName = "Self Reference")]
        [DataRow("If(true, \"random string\", Text(Price))", "If(#$boolean$#, #$string$#, Text(#$FieldDecimal$#))", DisplayName = "Function")]
        [DataRow("If(Rating = 'Rating (Locals)'.Hot, Quantity, Price)", "If(#$FieldPicklist$# = #$OptionSet$#.#$righthandid$#, #$FieldDecimal$#, #$FieldDecimal$#)", DisplayName = "CDS Enum literal")]
        [DataRow("If('Global Picklist' = [@'Global Picklist'].Medium, Quantity, Price)", "If(#$FieldPicklist$# = #$FieldPicklist$#.#$righthandid$#, #$FieldDecimal$#, #$FieldDecimal$#)", DisplayName = "CDS Global Enum literal")]
        [DataRow("DateAdd(UTCToday(), Quantity, TimeUnit.Months)", "DateAdd(UTCToday(), #$FieldDecimal$#, #$Enum$#.#$righthandid$#)", DisplayName = "Enum literal")]
        [DataRow("/* Comment */\n\n\t  'Conflict (conflict1)'\n\n\t  \n -'Conflict (conflict2)'", "#$FieldDecimal$# + -#$FieldDecimal$#", DisplayName = "Preserves whitespace and comments")]

        public void Sanitize(string expr, string sanitized)
        {
            var provider = new MockXrmMetadataProvider(RelationshipModels);
            var engine = new PowerFx2SqlEngine(RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider));

            var options = new SqlCompileOptions();
            var result = engine.Compile(expr, options);

            Assert.AreEqual(sanitized, result.SanitizedFormula);
        }

        [DataTestMethod]
        [DataRow("01,200", null, null, false, DisplayName = "Default numeric literal with comma")]
        [DataRow("01,200", "en-US", null, false, DisplayName = "English numeric literal with comma")]
        [DataRow("01,200", "fr-FR", "01.200", true, DisplayName = "French numeric literal with comma (decimal)")]
        [DataRow("01,000", "fr-FR", "01.000", true, DisplayName = "French numeric literal with comma (decimal) with all decimal zeros")]
        [DataRow("01.200", null, "01.200", true, DisplayName = "Default numeric literal with period")]
        [DataRow("01.200", "en-US", "01.200", true, DisplayName = "English numeric literal with period")]
        [DataRow("01.200", "fr-FR", "01.200", false, DisplayName = "French numeric literal with period")]
        [DataRow("123", null, "123", true, DisplayName = "Default whole number literal")]
        [DataRow("123", "en-US", "123", true, DisplayName = "English whole number literal")]
        [DataRow("123", "fr-FR", "123", true, DisplayName = "French whole number literal")]
        [DataRow("If(true, 1.1, 2)", null, "If(true, 1.1, 2)", true, DisplayName = "Default If with comma separators")]
        [DataRow("If(true, 1.1, 2)", "en-US", "If(true, 1.1, 2)", true, DisplayName = "English If with comma separators")]
        [DataRow("If(true, 1, 2)", "fr-FR", null, false, DisplayName = "French If with comma separators")]
        [DataRow("If(true; 1,1; 2)", "fr-FR", "If(true, 1.1, 2)", true, DisplayName = "French If with semicolon separators and comma (decimal)")]
        public void CompileLocalizedScripts(string expr, string localeName, string logicalFormula, bool success)
        {
            CultureInfo culture;
            switch (localeName)
            {
                case null:
                    culture = CultureInfo.InvariantCulture;
                    break;
                default:
                    culture = CultureInfo.CreateSpecificCulture(localeName);
                    break;
            };

            var engine = new PowerFx2SqlEngine(culture: culture);

            var result = engine.Compile(expr, new SqlCompileOptions());
            if (success)
            {
                Assert.IsTrue(result.IsSuccess);
                Assert.AreEqual(logicalFormula, result.LogicalFormula);

                var translation = engine.ConvertToDisplay(logicalFormula);
                Assert.AreEqual(expr, translation);
            }
            else
            {
                Assert.IsFalse(result.IsSuccess);
            }
        }

        private static string ToStableString(IEnumerable<string> items)
        {
            var array = items.ToArray();
            Array.Sort(array);
            return string.Join(',', array);
        }
    }
}