//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.Types;
using Xunit;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttributeTypeCode = Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    
    public class DataverseTests
    {
        [Fact]
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

            Assert.NotNull(result);
            Assert.NotNull(result.SqlFunction);
            Assert.NotNull(result.SqlCreateRow);
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Errors);

            Assert.True(result.ReturnType is DecimalType);
            Assert.Single(result.TopLevelIdentifiers);
            Assert.Equal("new_field", result.TopLevelIdentifiers.First());
            Assert.Equal("\t\t\nnew_field    *\n2.0\t", result.LogicalFormula);
        }

        public const string BaselineCurrencyFunction = @"CREATE FUNCTION fn_testUdf1(
    @v0 decimal(23,10), -- new_field
    @v1 decimal(38,10) -- new_field1
) RETURNS decimal(23,10)
  WITH SCHEMABINDING
AS BEGIN
    DECLARE @v2 decimal(38,10)
    DECLARE @v3 decimal(23,10)
    DECLARE @v4 decimal(38,10)

    -- expression body
    SET @v2 = (CAST(ISNULL(@v0,0) AS decimal(23,10)) * CAST(ISNULL(@v1,0) AS decimal(38,10)))
    IF(@v2<-100000000000 OR @v2>100000000000) BEGIN RETURN NULL END
    SET @v3 = 2.0
    SET @v4 = (CAST(ISNULL(@v2,0) AS decimal(38,10)) * CAST(ISNULL(@v3,0) AS decimal(23,10)))
    -- end expression body

    IF(@v4<-100000000000 OR @v4>100000000000) BEGIN RETURN NULL END
    RETURN ROUND(@v4, 10)
END
";
        [Fact]
        public void CheckCurrencyCompile()
        {
            var expr = "\t\t\nfield*field1*\n2.0\t";

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
                         LogicalName= "new_field1",
                         DisplayName = "field1",
                         AttributeType = AttributeTypeCode.Money
                     },
                }
            };

            var options = new SqlCompileOptions
            {
                CreateMode = SqlCompileOptions.Mode.Create,
                UdfName = "fn_testUdf1"
            };

            var metadata = model.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);
            var result = engine.Compile(expr, options);

            Assert.NotNull(result);
            Assert.NotNull(result.SqlFunction);
            Assert.NotNull(result.SqlCreateRow);

            Assert.Equal(BaselineCurrencyFunction, result.SqlFunction);
            
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Errors);

            Assert.True(result.ReturnType is DecimalType);
            Assert.Equal(2,result.TopLevelIdentifiers.Count);
            Assert.Equal("new_field", result.TopLevelIdentifiers.First());
            Assert.Equal("\t\t\nnew_field*new_field1*\n2.0\t", result.LogicalFormula);
        }

        public const string BaselineExchangeFunction = @"CREATE FUNCTION fn_testUdf1(
    @v0 decimal(28,12), -- exchangerate
    @v1 decimal(38,10) -- new_field1
) RETURNS decimal(23,10)
  WITH SCHEMABINDING
AS BEGIN
    DECLARE @v2 decimal(38,10)
    DECLARE @v3 decimal(23,10)
    DECLARE @v4 decimal(38,10)

    -- expression body
    SET @v2 = (ISNULL(@v0,0) * CAST(ISNULL(@v1,0) AS decimal(38,10)))
    IF(@v2<-100000000000 OR @v2>100000000000) BEGIN RETURN NULL END
    SET @v3 = 2.0
    SET @v4 = (CAST(ISNULL(@v2,0) AS decimal(38,10)) * CAST(ISNULL(@v3,0) AS decimal(23,10)))
    -- end expression body

    IF(@v4<-100000000000 OR @v4>100000000000) BEGIN RETURN NULL END
    RETURN ROUND(@v4, 10)
END
";

        [Fact]
        public void CheckExchangeRateCompile()
        {
            var expr = "\t\t\nexchangerate*field1*\n2.0\t";

            var model = new EntityMetadataModel
            {
                Attributes = new AttributeMetadataModel[]
                {
                    new AttributeMetadataModel
                     {
                         LogicalName= "exchangerate",
                         DisplayName = "exchangerate",
                         AttributeType = AttributeTypeCode.Money
                     },
                    new AttributeMetadataModel
                     {
                         LogicalName= "new_field1",
                         DisplayName = "field1",
                         AttributeType = AttributeTypeCode.Money
                     },
                }
            };

            var options = new SqlCompileOptions
            {
                CreateMode = SqlCompileOptions.Mode.Create,
                UdfName = "fn_testUdf1"
            };

            var metadata = model.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);
            var result = engine.Compile(expr, options);

            Assert.NotNull(result);
            Assert.NotNull(result.SqlFunction);
            Assert.NotNull(result.SqlCreateRow);

            Assert.Equal(BaselineExchangeFunction, result.SqlFunction);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Errors);

            Assert.True(result.ReturnType is DecimalType);
            Assert.Equal(2, result.TopLevelIdentifiers.Count);
            Assert.Equal("exchangerate", result.TopLevelIdentifiers.First());
            Assert.Equal("\t\t\nexchangerate*new_field1*\n2.0\t", result.LogicalFormula);
        }

        [Fact]
        public void PowerFunctionBlockedTest()
        {
            var expr = "Power(2,5)";

            var engine = new PowerFx2SqlEngine();
            var result = engine.Compile(expr, new SqlCompileOptions());

            Assert.NotNull(result);
            Assert.False(result.IsSuccess);
            Assert.Single(result.Errors);
            Assert.Contains("'Power' is an unknown or unsupported function.", result.Errors.First().ToString());
        }

        [Fact]
        public void SqrtFunctionBlockedTest()
        {
            var expr = "Sqrt(16)";

            var engine = new PowerFx2SqlEngine();
            var result = engine.Compile(expr, new SqlCompileOptions());

            Assert.NotNull(result);
            Assert.False(result.IsSuccess);
            Assert.Single(result.Errors);
            Assert.Contains("'Sqrt' is an unknown or unsupported function.", result.Errors.First().ToString());
        }

        [Fact]
        public void LnFunctionBlockedTest()
        {
            var expr = "Ln(20)";

            var engine = new PowerFx2SqlEngine();
            var result = engine.Compile(expr, new SqlCompileOptions());

            Assert.NotNull(result);
            Assert.False(result.IsSuccess);
            Assert.Single(result.Errors);
            Assert.Contains("'Ln' is an unknown or unsupported function.", result.Errors.First().ToString());
        }

        [Fact]
        public void ExpFunctionBlockedTest()
        {
            var expr = "Exp(10)";

            var engine = new PowerFx2SqlEngine();
            var result = engine.Compile(expr, new SqlCompileOptions());

            Assert.NotNull(result);
            Assert.False(result.IsSuccess);
            Assert.Single(result.Errors);
            Assert.Contains("'Exp' is an unknown or unsupported function.", result.Errors.First().ToString());
        }

        [Fact]
        public void CheckSchemaBinding()
        {
            var model = new EntityMetadataModel
            {
                Attributes = new AttributeMetadataModel[]
                {
                    new AttributeMetadataModel
                    {
                        LogicalName = "new_field",
                        DisplayName = "field",
                        AttributeType = AttributeTypeCode.Decimal
                    }
                }
            };

            var metadata = model.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);

            var expr = "UTCNow()";
            var result = engine.Compile(expr, new SqlCompileOptions());
            Assert.DoesNotContain(SqlStatementFormat.WithSchemaBindingFormat, result.SqlFunction);

            expr = "field * 10";
            result = engine.Compile(expr, new SqlCompileOptions());
            Assert.Contains(SqlStatementFormat.WithSchemaBindingFormat, result.SqlFunction);
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
    SELECT TOP(1) @v1 = [new_Calc_Schema] FROM [dbo].[AccountBase] WHERE[AccountId] = @v2
    SELECT TOP(1) @v4 = [address1_latitude] FROM [dbo].[Account] WHERE[AccountId] = @v2

    -- expression body
    SET @v3 = (CAST(ISNULL(@v0,0) AS decimal(23,10)) + CAST(ISNULL(@v1,0) AS decimal(23,10)))
    IF(@v3<-100000000000 OR @v3>100000000000) BEGIN RETURN NULL END
    SET @v5 = (CAST(ISNULL(@v3,0) AS decimal(38,10)) + CAST(ISNULL(@v4,0) AS decimal(23,10)))
    -- end expression body

    IF(@v5<-100000000000 OR @v5>100000000000) BEGIN RETURN NULL END
    RETURN ROUND(@v5, 10)
END
";
        public const string BaselineCreateRow = @"fn_testUdf1([new_CurrencyPrice_Schema],[AccountId])
";
        public const string BaselineLogicalFormula = "new_CurrencyPrice + new_Calc + address1_latitude";

        [Fact]
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

            Assert.Equal("address1_latitude,new_Calc,new_CurrencyPrice", ToStableString(result.TopLevelIdentifiers));

            Assert.Equal(BaselineFunction, result.SqlFunction);

            Assert.Equal(BaselineCreateRow, result.SqlCreateRow);
            Assert.Equal(BaselineLogicalFormula, result.LogicalFormula);
        }

        [Fact]
        public void CheckCompileAllAttributeTypes()
        {
            var expr = "field * Int - Money + If(Boolean || Picklist = 'Picklist (All Attributes)'.One, Value(String), 2)";

            var metadata = AllAttributeModel.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);
            var result = engine.Compile(expr, new SqlCompileOptions());

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.Equal("new_field * int - money + If(boolean || picklist = allattributes_picklist_optionSet.'1', Value(string), 2)", result.LogicalFormula);
        }

        [Fact]
        public void CheckMoney()
        {
            var expr = "Money"; // resolve to Money filed

            var metadata = AllAttributeModel.ToXrm();

            var metadataProvider = new CdsEntityMetadataProvider(null)
            {
                NumberIsFloat = false  // Causes money to be imported as Decimal instead of Number
            };

            var engine = new PowerFx2SqlEngine(metadata, metadataProvider);
            var result = engine.Check(expr);

            Assert.NotNull(result);

            Assert.True(result.IsSuccess);
            Assert.Equal("money", result.ApplyGetInvariant());
        }

        [Fact]
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

            Assert.NotNull(result);
            Assert.NotNull(result.Errors);
            var errors = result.Errors.ToArray();
            Assert.Equal(2, errors.Length);
            Assert.Equal("Name isn't valid. 'Lookup' isn't recognized.", errors[0].Message);
        }

        [Fact]
        public void CheckNullRef()
        {
            var engine = new PowerFx2SqlEngine();
            Assert.Throws<ArgumentNullException>(() => engine.Check((string)null));
        }

        [Fact]
        public void CheckSuggestionFailure()
        {
            var engine = new PowerFx2SqlEngine();
            var intellisense = engine.Suggest("foo + ", cursorPosition: 6);

            Assert.Empty(intellisense.Suggestions);
        }

        [Fact]
        public void CheckSuccess()
        {
            var engine = new PowerFx2SqlEngine();
            var result = engine.Check("3*2");

            Assert.True(result.IsSuccess);
            Assert.True(result.ReturnType is DecimalType);
        }

        [Fact]
        public void CheckParseError()
        {
            var engine = new PowerFx2SqlEngine();
            var result = engine.Check("3*1+");

            Assert.False(result.IsSuccess);
            var errors = result.Errors.ToArray();
            Assert.True(errors.Length > 1);
            Assert.StartsWith("Error 4-4: Expected an operand", errors[0].ToString());
            Assert.Equal(TexlStrings.ErrOperandExpected.Key, errors[0].MessageKey);
        }

        [Theory]
        [InlineData("3+foo+2", "Error 2-5: Name isn't valid. 'foo' isn't recognized.", "ErrInvalidName")] // "Invalid field"
        [InlineData("3+foo(2)", "Error 2-8: 'foo' is an unknown or unsupported function.", "ErrUnknownFunction")] // "Invalid function"
        public void CheckBindError(string expr, string message, string key)
        {
            var engine = new PowerFx2SqlEngine();
            var result = engine.Check(expr); // foo is undefined 

            Assert.False(result.IsSuccess);
            var errors = result.Errors.ToArray();
            Assert.Single(errors);
            Assert.Equal(message, errors[0].ToString());
            Assert.Equal(key, errors[0].MessageKey);
        }

        // Verify error messages in other locales
        [Theory]
        [InlineData("3+", "Opérande attendu. La formule ou l’expression attend un opérande valide", "ErrOperandExpected")] // "Parse error"
        public void CheckLocaleErrorMssage(string expr, string message, string key)
        {
            var culture = new CultureInfo("fr-FR");
            var engine = new PowerFx2SqlEngine(culture: culture);
            var result = engine.Check(expr); // foo is undefined 

            Assert.False(result.IsSuccess);
            var errors = result.Errors.ToArray();
            Assert.Contains(message, errors[0].ToString());
            Assert.Equal(key, errors[0].MessageKey);
        }

        [Fact]
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

            Assert.NotNull(result);
            Assert.NotNull(result.SqlFunction);
            Assert.NotNull(result.SqlCreateRow);
            Assert.Empty(result.Errors);

            Assert.True(result.ReturnType is SqlIntType);
        }

        [Fact]
        public void CompilePassthruTypeHint()
        {
            var expr = "\"foo\"";
            var engine = new PowerFx2SqlEngine();
            var options = new SqlCompileOptions { TypeHints = new SqlCompileOptions.TypeDetails { TypeHint = AttributeTypeCode.String } };
            var result = engine.Compile(expr, options);

            Assert.Empty(result.Errors);
            Assert.True(result.ReturnType is StringType);
        }

        [Fact]
        public void CompileInvalidTypeHint()
        {
            var expr = "2 + 2";

            var engine = new PowerFx2SqlEngine();
            var options = new SqlCompileOptions { TypeHints = new SqlCompileOptions.TypeDetails { TypeHint = AttributeTypeCode.String } };
            var result = engine.Compile(expr, options);

            var errors = result.Errors.ToArray();
            Assert.Single(errors);
            Assert.Equal("The result type for this formula is expected to be String, but the actual result type is Decimal. The result type of a formula column cannot be changed.", errors[0].Message);
            Assert.Equal(SqlCompileException.ResultTypeMustMatch.Key, errors[0].MessageKey);
        }

        [Theory]
        [InlineData("Lookup", "Error 0-6: The result type Record is not supported in formula columns.")] // "Lookup"
        [InlineData("If(true, 'Self Reference', Lookup)", "Error 0-34: The result type Record is not supported in formula columns.")] // "Polymorphic Lookup"
        [InlineData("Blank()", "Error 0-7: The result type ObjNull is not supported in formula columns.")] // "Blank"
        [InlineData("Guid", "Error 0-4: The result type Guid is not supported in formula columns.")] // "Guid"
        [InlineData("Owner", "Error 0-5: The result type Record is not supported in formula columns.")] // "Owner"
        [InlineData("Customer", "Error 0-8: The result type Record is not supported in formula columns.")] // "Customer"
        [InlineData("BigInt", "Error 0-6: Columns of type BigInt are not supported in formula columns.")] // "BigInt"
        [InlineData("Email", "Error 0-5: Columns of type String with format Email are not supported in formula columns.")] // "Email"
        [InlineData("Ticker", "Error 0-6: Columns of type String with format TickerSymbol are not supported in formula columns.")] // "Ticker"
        [InlineData("Hyperlink", "Error 0-9: Columns of type String with format Url are not supported in formula columns.")] // "Hyperlink"
        [InlineData("If(true, Hyperlink)", "Error 9-18: Columns of type String with format Url are not supported in formula columns.")] // "Hyperlink in If"
        [InlineData("Left(Hyperlink, 2)", "Error 5-14: Columns of type String with format Url are not supported in formula columns.")] // "Hyperlink in Left"
        [InlineData("Duration", "Error 0-8: Columns of type Integer with format Duration are not supported in formula columns.")] // "Duration"
        [InlineData("TimeZone", "Error 0-8: Columns of type Integer with format TimeZone are not supported in formula columns.")] // "TimeZone"
        [InlineData("Image", "Error 0-5: Columns of type Virtual are not supported in formula columns.")] // "Image"
        [InlineData("IsBlank(Image)", "Error 8-13: Columns of type Virtual are not supported in formula columns.")] // "Image in IsBlank"
        [InlineData("File", "Error 0-4: Name isn't valid. 'File' isn't recognized.")] // "File not added to entity"
        [InlineData("Picklist", "Error 0-8: The result type OptionSetValue is not supported in formula columns.")] // "Picklist"
        [InlineData("MultiSelect", "Error 0-11: The result type OptionSetValue is not supported in formula columns.")] // "Multi Select Picklist"
        [InlineData("If(IsBlank(String), 'Picklist (All Attributes)'.One, 'Picklist (All Attributes)'.Two)", "Error 0-85: The result type OptionSetValue (allattributes_picklist_optionSet) is not supported in formula columns.")] // "Built picklist"
        [InlineData("If(IsBlank(String), 'MultiSelect (All Attributes)'.Eight, 'MultiSelect (All Attributes)'.Ten)", "Error 0-93: The result type OptionSetValue (allattributes_multiSelect_optionSet) is not supported in formula columns.")] // "Built hybrid picklist"
        public void CompileInvalidTypes(string expr, string error)
        {
            // This use of NumberIsFloat and these tests to be redone when the SQL compiler is running on native Decimal
            // Tracked with https://github.com/microsoft/Power-Fx-Dataverse/issues/117
            var provider = new MockXrmMetadataProvider(AllAttributeModels);
            var engine = new PowerFx2SqlEngine(AllAttributeModels[0].ToXrm(), new CdsEntityMetadataProvider(provider) { NumberIsFloat = DataverseEngine.NumberIsFloat });

            var checkResult = engine.Check(expr);
            Assert.False(checkResult.IsSuccess);
            Assert.NotNull(checkResult.Errors);
            var errors = checkResult.Errors.ToArray();
            Assert.Single(errors);
            Assert.Equal(error, errors[0].ToString());

            var compileResult = engine.Compile(expr, new SqlCompileOptions());
            Assert.False(compileResult.IsSuccess);
            Assert.NotNull(compileResult.Errors);
            errors = checkResult.Errors.ToArray();
            Assert.Single(errors);
            Assert.Equal(error, errors[0].ToString());
            Assert.NotNull(compileResult.SanitizedFormula);
        }

        [Theory]
        [InlineData("field", typeof(DecimalType))] // "Decimal"
        [InlineData("1.1", typeof(DecimalType))] // "Numeric literal returns Decimal"
        [InlineData("Money", typeof(DecimalType))] // "Money returns Decimal"
        [InlineData("Int", typeof(DecimalType))] // "Int returns Decimal"
        [InlineData("String", typeof(StringType))] // "String"
        [InlineData("\"foo\"", typeof(StringType))] // "String literal returns String"
        [InlineData("Boolean", typeof(BooleanType))] // "Boolean"
        [InlineData("true", typeof(BooleanType))] // "Boolean literal returns Boolean"
        [InlineData("Mod(int, int)", typeof(DecimalType))] // "Int from function returns decimal"
        public void CompileValidReturnType(string expr, Type returnType)
        {
            // This use of NumberIsFloat and these tests to be redone when the SQL compiler is running on native Decimal
            // Tracked with https://github.com/microsoft/Power-Fx-Dataverse/issues/117
            var provider = new MockXrmMetadataProvider(AllAttributeModels);
            var engine = new PowerFx2SqlEngine(AllAttributeModels[0].ToXrm(), new CdsEntityMetadataProvider(provider));

            AssertReturnType(engine, expr, returnType);
        }

        [Theory]
        [InlineData("")] // "Empty"
        [InlineData("    ")] // "Spaces"
        [InlineData("\n\t  \r\n  \t\n\r\n")] // "Whitespace"
        public void CheckEmptyFormula(string expr)
        {
            var engine = new PowerFx2SqlEngine();

            var result = engine.Check(expr);
            Assert.True(result.IsSuccess);
            Assert.Equal(typeof(BlankType), result.ReturnType.GetType());
        }

        // Verify that AllAttributeModel has an attribute of each type
        [Fact]
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
            Assert.Equal(untested, remaining);
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

        private static void AssertResult(CheckResult checkResult, Dictionary<string, string> expectedErrors, string fieldName, string expr)
        {
            if (checkResult.IsSuccess)
            {
                Assert.False(expectedErrors.ContainsKey(fieldName), $"Type {fieldName} should not be supported");
            }
            else
            {
                Assert.True(expectedErrors.ContainsKey(fieldName), $"{expr} fails to compile.");
                string expectedError = expectedErrors[fieldName];
                var foundError = checkResult.Errors.FirstOrDefault(error => error.Message.Contains(expectedError, StringComparison.OrdinalIgnoreCase));
                var actualError = checkResult.Errors.First().Message;
                Assert.NotNull(foundError); // $"For {fieldName}, expected error message: {expectedError}\r\nActualError: {actualError}"
            }
        }

        // For each attribute type x, verify we can consume it.
        // Explicitly iterate over every attribute in AllAttributeModels (to ensure we're being comprehensive.
        // And if we can't consume it, verify the error. 
        [Fact]
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

            // This use of NumberIsFloat and these tests to be redone when the SQL compiler is running on native Decimal
            // Tracked with https://github.com/microsoft/Power-Fx-Dataverse/issues/117
            var provider = new MockXrmMetadataProvider(AllAttributeModels);
            var engine = new PowerFx2SqlEngine(AllAttributeModels[0].ToXrm(), new CdsEntityMetadataProvider(provider) { NumberIsFloat = false });

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
        [Fact]
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
            Assert.Throws<AppMagic.Authoring.Importers.DataDescription.ParseException>(
                () => new PowerFx2SqlEngine(model.ToXrm()));
        }

        // Model where names conflict on case. 
        private readonly EntityMetadataModel ModelWithCasing = new EntityMetadataModel
        {
            Attributes = new AttributeMetadataModel[]
              {
                  AttributeMetadataModel.NewDecimal("field1", "FIELD DISPLAY"),
                  AttributeMetadataModel.NewString("Field1", "field display")
              }
        };

        // Test that we can handle casing overloads on fields. 
        // Dataverse fields are case *sensitive*. 
        [Theory]
        [InlineData("'FIELD DISPLAY'", typeof(DecimalType))]
        [InlineData("field1", typeof(DecimalType))]
        [InlineData("'field display'", typeof(StringType))]
        [InlineData("Field1", typeof(StringType))]
        public void CheckCasing(string expr, Type returnType)
        {
            var metadata = ModelWithCasing.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);

            AssertReturnType(engine, expr, returnType);
        }

        // Verify the expression has the given return type (specified as a FormulaType). 
        private static void AssertReturnType(PowerFx2SqlEngine engine, string expr, Type returnType)
        {
            Assert.True(typeof(FormulaType).IsAssignableFrom(returnType));

            var checkResult = engine.Check(expr);
            Assert.True(checkResult.IsSuccess);
            Assert.Equal(returnType, checkResult.ReturnType.GetType());

            var compileResult = engine.Compile(expr, new SqlCompileOptions());
            Assert.True(compileResult.IsSuccess);
            Assert.Equal(returnType, compileResult.ReturnType.GetType());
        }

        private static void AssertReturnTypeOrError(PowerFx2SqlEngine engine, string expr, bool success, Type returnType, params string[] errors)
        {
            if (success)
            {
                Assert.True(typeof(FormulaType).IsAssignableFrom(returnType));
            }

            var checkResult = engine.Check(expr);
            Assert.Equal(success, checkResult.IsSuccess);
            if (success)
            {
                Assert.Equal(returnType, checkResult.ReturnType.GetType());
                Assert.Empty(checkResult.Errors);
            }
            else
            {
                Assert.NotNull(checkResult.Errors);
                var actualErrors = checkResult.Errors.Select(error => error.ToString()).ToArray();
                Assert.Equal(errors, actualErrors);
            }

            var options = new SqlCompileOptions();
            var compileResult = engine.Compile(expr, options);
            Assert.Equal(success, compileResult.IsSuccess);

            if (success)
            {
                Assert.Equal(returnType, checkResult.ReturnType.GetType());
                Assert.Empty(compileResult.Errors);
            }
            else
            {
                Assert.NotNull(compileResult.Errors);
                var actualErrors = compileResult.Errors.Select(error => error.ToString()).ToArray();
                Assert.Equal(errors, actualErrors);
                Assert.NotNull(compileResult.SanitizedFormula);
            }
        }

        // Has conflicting _display_ names
        private readonly EntityMetadataModel ModelWithConflict = new EntityMetadataModel
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

        [Fact]
        public void CheckFieldConflict()
        {
            var expr = "Conflict - conflict2";

            var metadata = ModelWithConflict.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);
            var result = engine.Check(expr);

            Assert.NotNull(result.Errors);
            var errors = result.Errors.ToArray();
            Assert.Single(errors);
            Assert.Equal("Error 0-8: Name isn't valid. 'Conflict' isn't recognized.", errors[0].ToString());
        }

        [Theory]
        [InlineData("conflict1 + conflict2")] // "LogicalNames"
        [InlineData("'Conflict (conflict1)' + 'Conflict (conflict2)'")] // "Disambiguation"
        public void CompileFieldConflictResolved(string expr)
        {
            var metadata = ModelWithConflict.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);
            var options = new SqlCompileOptions();
            var result = engine.Compile(expr, options);

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Errors);
            Assert.Equal("conflict1 + conflict2", result.LogicalFormula);
        }

        [Fact]
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

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.Equal("ThisRecord.a + ThisRecord.b + a + b", result.LogicalFormula);
        }

        [Fact]
        public void CompileInvalidFormula()
        {
            // Max length is determined by engine. 
            var opts = new PowerFx2SqlEngine().GetDefaultParserOptionsCopy();
            Assert.Equal(1000, opts.MaxExpressionLength);

            var expr = new string('a', 1001);
            var error = "Error 0-1001: Expression can't be more than 1000 characters. The expression is 1001 characters.";

            var engine = new PowerFx2SqlEngine();            

            var checkResult = engine.Check(expr);
            Assert.False(checkResult.IsSuccess);
            Assert.NotNull(checkResult.Errors);
            Assert.Single(checkResult.Errors);
            Assert.Equal(error, checkResult.Errors.First().ToString());

            var compileResult = engine.Compile(expr, new SqlCompileOptions());
            Assert.False(compileResult.IsSuccess);
            Assert.NotNull(compileResult.Errors);
            Assert.Single(compileResult.Errors);
            Assert.Equal(error, compileResult.Errors.First().ToString());
            Assert.Equal("ErrTextTooLarge", compileResult.Errors.First().MessageKey);
            Assert.NotNull(compileResult.SanitizedFormula);
        }

        [Theory]
        [InlineData("Price * Quantity", "new_price,new_quantity")] // "Main Entity"
        [InlineData("ThisRecord.Price + Quantity", "new_price,new_quantity")] // "Main Entity ThisRecord"
        [InlineData("Price + Other.Data", "new_price,otherid", "remote=>data", "local=>local_remote")] // "Lookup"
        [InlineData("Other.'Other Other'.'Data Two' + Other.'Other Other'.'Other Other Other'.'Data Three'",
            "otherid",
            "remote=>otherotherid|doubleremote=>data2,otherotherotherid|tripleremote=>data3",
            "local=>local_remote|remote=>remote_doubleremote|doubleremote=>doubleremote_tripleremote")] // "Multiple levels of lookup"
        [InlineData("'Self Reference'.Price + Other.Data",
            "new_price,otherid,selfid",
            "remote=>data",
            "local=>local_remote,self")] // "Multiple lookups"
        [InlineData("'Logical Lookup'.Data",
            "logicalid",
            "remote=>data",
            "local=>logical")] // "Logical Lookup"
        [InlineData("7 + 2", "")] // "Literals"
        public void CompileIdentifiers(string expr, string topLevelFields, string relatedFields = null, string relationships = null)
        {
            // This use of NumberIsFloat and these tests to be redone when the SQL compiler is running on native Decimal
            // Tracked with https://github.com/microsoft/Power-Fx-Dataverse/issues/117
            var provider = new MockXrmMetadataProvider(RelationshipModels);
            var engine = new PowerFx2SqlEngine(RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider));
            var options = new SqlCompileOptions();
            var result = engine.Compile(expr, options);

            Assert.NotNull(result);
            Assert.Equal(topLevelFields, ToStableString(result.TopLevelIdentifiers));
            if (relatedFields != null)
            {
                Assert.Equal(relatedFields, string.Join('|', result.RelatedIdentifiers.ToArray().Select(pair => pair.Key + "=>" + ToStableString(pair.Value))));
            }
            else
            {
                Assert.Empty(result.RelatedIdentifiers);
            }
            if (relationships != null)
            {
                Assert.Equal(relationships, string.Join('|', result.DependentRelationships.ToArray().Select(pair => pair.Key + "=>" + ToStableString(pair.Value))));
            }
            else
            {
                Assert.Empty(result.DependentRelationships);
            }
        }

        [Theory]
        [InlineData("a in b", "Error 0-1: Only a literal value is supported for this argument.")] // "in"
        [InlineData("a exactin b", "Error 0-1: Only a literal value is supported for this argument.")] // "exactin"
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

            Assert.NotNull(result.Errors);
            Assert.Single(result.Errors);
            Assert.Equal(error, result.Errors.First().ToString());
            Assert.Equal(SqlCompileException.LiteralArgRequired.Key, result.Errors.First().MessageKey);
        }

        [Theory]
        [InlineData("1 - UTCToday()", false, "Error 4-14: This argument cannot be passed as type Date in formula columns.")] // "Negation of date (coerce date to number then back to date)"
        [InlineData("UTCNow() / \"2\"", false, "Error 0-8: This argument cannot be passed as type Decimal in formula columns.")] // "Coerce date to number in division operation (with coerced string)"
        [InlineData("2 > UTCNow()", false, "Error 4-12: This argument cannot be passed as type Decimal in formula columns.")] // "Coerce date to number in left arg of logical operation"
        [InlineData("UTCToday() <= 8.2E9", false, "Error 0-10: This argument cannot be passed as type Decimal in formula columns.")] // "Coerce date to number in right arg of logical operation"
        [InlineData("UTCToday() = 8.2E9", false, "Error 0-10: This argument cannot be passed as type Decimal in formula columns.")] // "Coerce date to number in right arg of equals"
        [InlineData("UTCToday() <> 8.2E9", false, "Error 0-10: This argument cannot be passed as type Decimal in formula columns.")] // "Coerce date to number right arg of not equals"
        [InlineData("Abs(UTCToday())", false, "Error 4-14: This argument cannot be passed as type Decimal in formula columns.")] // "Coerce date to number in Abs function"
        [InlineData("Max(1, UTCNow())", false, "Error 7-15: This argument cannot be passed as type Decimal in formula columns.")] // "Coerce date to number in Max function"
        [InlineData("Trunc(UTCToday(), UTCNow())", false, "Error 6-16: This argument cannot be passed as type Decimal in formula columns.")] // "Coerce date to number in Trunc function"

        // These 3 functions uses DateTimeToNumber for UTCNow() when used in Left, Replace and Subsitute function even though NIF flag is false
        [InlineData("Left(\"foo\", UTCNow())", false, "Error 12-20: This argument cannot be passed as type Number in formula columns.")] // "Coerce date to number in Left function"
        [InlineData("Replace(\"abcabcabc\", UTCToday(), UTCNow(), \"xx\")", false, "Error 21-31: This argument cannot be passed as type Number in formula columns.")] // "Coerce date to number in first numeric arg in Replace function"
        [InlineData("Replace(\"abcabcabc\", 5, UTCNow(), \"xx\")", false, "Error 24-32: This argument cannot be passed as type Number in formula columns.")] // "Coerce date to number in second numeric arg in Replace function"
        [InlineData("Substitute(\"abcabcabc\", \"ab\", \"xx\", UTCNow())", false, "Error 36-44: This argument cannot be passed as type Number in formula columns.")] // "Coerce date to number in Substitute function"
        public void CheckCoercionFailures(string expr, bool success, string message = null)
        {
            var engine = new PowerFx2SqlEngine();
            var result = engine.Check(expr);

            Assert.NotNull(result);
            Assert.Equal(success, result.IsSuccess);
            if (!success)
            {
                Assert.NotNull(result.Errors);
                Assert.Single(result.Errors);
                Assert.Equal(message, result.Errors.First().ToString());
                Assert.Equal(SqlCompileException.ArgumentTypeNotSupported.Key, result.Errors.First().MessageKey);
            }
        }

        [Theory]
        [InlineData("Text(123, \"#[$-fr-FR]\")", false, false, "Error 0-23: The function 'Text' has some invalid arguments.")] // "Locale token in format string not supported"
        [InlineData("Text(123, \"#\\[$-fr-FR]\")", false, false, "Error 10-23: Locale-specific formatting tokens such as \".\" and \",\" are not supported in formula columns.")] // "Escaped Locale token in format string not supported"
        [InlineData("Text(123, \",###.0\")", true, false, "Error 10-18: Locale-specific formatting tokens such as \".\" and \",\" are not supported in formula columns.")] // "Locale-specific separators not supported"
        [InlineData("Text(123, \"\\,###\\.\")", true, false, "Error 10-19: Locale-specific formatting tokens such as \".\" and \",\" are not supported in formula columns.")] // "Escaped locale-specific separators not supported"
        [InlineData("Text(123, \"#\", \"fr-FR\")", true, false, "Error 15-22: The language argument is not supported for the Text function in formula columns.")] // "Localization parameter"
        [InlineData("Text(123, \"[$-fr-FR]#\")", true, false, "Error 10-22: Locale-specific formatting tokens such as \".\" and \",\" are not supported in formula columns.")] // "Locale token at start of format string not supported"
        [InlineData("Text(123, \"#\" & \".0\")", true, false, "Error 14-15: Only a literal value is supported for this argument.")] // "Non-literal format string"
        [InlineData("Int(\"123\")", true, true)] // "Int on string"
        [InlineData("Text(123)", true, false, "Error 0-9: Include a format in the second argument when using the Text function with numbers. The format string cannot include a thousands or decimal separator in formula columns.")] // "Text() function with single numeric arg is not supported"
        [InlineData("Text(123.4)", true, false, "Error 0-11: Include a format in the second argument when using the Text function with numbers. The format string cannot include a thousands or decimal separator in formula columns.")] // "Text() function with single numeric arg is not supported"
        [InlineData("Text(1/2)", true, false, "Error 0-9: Include a format in the second argument when using the Text function with numbers. The format string cannot include a thousands or decimal separator in formula columns.")] // "Text() function with single numeric arg is not supported"
        [InlineData("Text(-123.4)", true, false, "Error 0-12: Include a format in the second argument when using the Text function with numbers. The format string cannot include a thousands or decimal separator in formula columns.")] // "Text() function with single numeric arg is not supported"
        [InlineData("Text(1234567.89)", true, false, "Error 0-16: Include a format in the second argument when using the Text function with numbers. The format string cannot include a thousands or decimal separator in formula columns.")] // "Text() function with single numeric arg is not supported"
        [InlineData("Text(If(1<0,2))", true, false, "Error 0-15: Include a format in the second argument when using the Text function with numbers. The format string cannot include a thousands or decimal separator in formula columns.")] // "Text() function with single numeric arg is not supported"
        [InlineData("123 & 456", true, false, "Error 0-3: Use the Text function to convert numbers to text. Include a format in the second argument, which cannot include a thousands or decimal separator in formula columns.")] // "Implicit Conversion of Numbers is not supported"
        [InlineData("123.45 & 456", true, false, "Error 0-6: Use the Text function to convert numbers to text. Include a format in the second argument, which cannot include a thousands or decimal separator in formula columns.")] // "Implicit Conversion of Numbers is not supported"
        [InlineData("Concatenate(123, 456)", true, false, "Error 12-15: Use the Text function to convert numbers to text. Include a format in the second argument, which cannot include a thousands or decimal separator in formula columns.")] // "Implicit Conversion of Numbers is not supported"
        [InlineData("123 & \"a\"", true, false, "Error 0-3: Use the Text function to convert numbers to text. Include a format in the second argument, which cannot include a thousands or decimal separator in formula columns.")] // "Implicit Conversion of Numbers is not supported"
        public void CheckTextFailures(string expr, bool pfxSuccess, bool sqlSuccess, string message = null)
        {
            var sqlEngine = new PowerFx2SqlEngine();
            var engine = new RecalcEngine();
            var check = engine.Check(expr);

            Assert.Equal(pfxSuccess, check.IsSuccess);

            if (!check.IsSuccess)
            {
                Assert.True(check.Errors.Select(err => err.Message.Contains(message)).Any());
            }

            var result = sqlEngine.Check(expr);

            Assert.NotNull(result);
            Assert.Equal(sqlSuccess, result.IsSuccess);
            Assert.NotNull(result.Errors);

            if (!result.IsSuccess)
            {
                Assert.True(result.Errors.Select(err => err.Message.Contains(message)).Any());
            }
        }

        [Theory]
        [InlineData("UTCNow()", true, typeof(DateTimeNoTimeZoneType))] // "UTCNow"
        [InlineData("UTCToday()", true, typeof(DateTimeNoTimeZoneType))] // "UTCToday"
        [InlineData("IsUTCToday(UTCNow())", true, typeof(BooleanType))] // "IsUTCToday of UTCNow"
        [InlineData("Now()", true, typeof(DateTimeType))] // "Now"
        [InlineData("Today()", false, null, "Error 0-7: Today is not supported in formula columns, use UTCToday instead.")] // "Today not supported"
        [InlineData("IsToday(Today())", false, null, "Error 0-16: IsToday is not supported in formula columns, use IsUTCToday instead.")] // "IsToday not supported"
        [InlineData("IsUTCToday(UTCToday())", true, typeof(BooleanType))] // "IsUTCToday of UTCToday"
        [InlineData("IsUTCToday(tziDateOnly)", true, typeof(BooleanType))] // "IsUTCToday of TZI Date Only"
        [InlineData("IsUTCToday(dateOnly)", true, typeof(BooleanType))] // "IsUTCToday of Date Only"
        [InlineData("IsUTCToday(userLocalDateTime)", true, typeof(BooleanType))] // "IsUTCToday of User Local Date Time"
        [InlineData("userLocalDateTime", true, typeof(DateTimeType))] // "User Local Date Time"
        [InlineData("userLocalDateOnly", true, typeof(DateTimeType))] // "User Local Date Only"
        [InlineData("dateOnly", true, typeof(DateType))] // "Date Only"
        [InlineData("tziDateTime", true, typeof(DateTimeNoTimeZoneType))] // "TZI Date Time"
        [InlineData("tziDateOnly", true, typeof(DateTimeNoTimeZoneType))] // "TZI Date Only"
        [InlineData("dateOnly + 0.25", true, typeof(DateType))] // "DateOnly add fractional day"
        [InlineData("DateAdd(dateOnly, 1, TimeUnit.Days)", true, typeof(DateType))] // "DateAdd Days Date Only"
        [InlineData("DateAdd(dateOnly, 1, TimeUnit.Hours)", true, typeof(DateType))] // "DateAdd Hours Date Only"
        [InlineData("DateAdd(tziDateOnly, 1, TimeUnit.Hours)", true, typeof(DateTimeNoTimeZoneType))] // "DateAdd TZI Date Only"
        [InlineData("DateAdd(userLocalDateOnly, 1, TimeUnit.Hours)", true, typeof(DateTimeType))] // "DateAdd User Local Date Only"
        [InlineData("If(true, tziDateOnly, dateOnly)", true, typeof(DateTimeNoTimeZoneType))] // "If TZI Date Only vs. Date Only"
        [InlineData("If(true, userLocalDateTime, userLocalDateOnly)", true, typeof(DateTimeType))] // "If User Local Date Time vs. User Local Date Only"
        [InlineData("If(true, tziDateOnly, tziDateTime)", true, typeof(DateTimeNoTimeZoneType))] // "If TZI Date Only vs. TZI Date Time"
        [InlineData("Switch(1, 1, userLocalDateOnly, userLocalDateTime)", true, typeof(DateTimeType))] // "Switch UserLocal Date Only vs. User Local Date Time"
        [InlineData("Switch(1, 2, tziDateOnly, userLocalDateOnly)", false, null, "Error 26-43: This operation cannot be performed on values which are of different Date Time Behaviors.")] // "Switch TZI Date Only vs. User Local Date Only"
        [InlineData("Switch(1, 2, dateOnly, dateOnly)", true, typeof(DateType))] // "Switch Date Only vs. Date Only"
        [InlineData("Text(tziDateOnly)", false, null, "Error 5-16: This argument cannot be passed as type DateTimeNoTimeZone in formula columns.")] // "Text for TZI Date Only"
        [InlineData("Text(userLocalDateTime)", false, null, "Error 5-22: This argument cannot be passed as type DateTime in formula columns.")] // "Text for User Local Date Time"
        [InlineData("Text(UTCNow())", false, null, "Error 5-13: This argument cannot be passed as type DateTimeNoTimeZone in formula columns.")] // "Text for UTCNow"
        [InlineData("DateDiff(userLocalDateTime, tziDateOnly)", false, null, "Error 0-40: This operation cannot be performed on values which are of different Date Time Behaviors.")] // "DateDiff User Local Date Time vs TZI Date Only"
        [InlineData("DateDiff(dateOnly, tziDateOnly)", true, typeof(DecimalType))] // "DateDiff Date Only vs TZI Date Only"
        [InlineData("DateDiff(userLocalDateOnly, dateOnly)", false, null, "Error 0-37: This operation cannot be performed on values which are of different Date Time Behaviors.")] // "DateDiff User Local Date Only vs Date Only"
        [InlineData("DateDiff(userLocalDateOnly, userLocalDateTime)", true, typeof(DecimalType))] // "DateDiff User Local Date Only vs User Local Date Time"
        [InlineData("userLocalDateTime > userLocalDateOnly", true, typeof(BooleanType))] // "> User Local Date Time vs. User Local Date Only"
        [InlineData("tziDateTime <> tziDateOnly", true, typeof(BooleanType))] // "<> TZI Date Time vs. TZI Date Only"

        // Regressed with https://github.com/microsoft/Power-Fx/issues/1379 
        // [InlineData("UTCToday() = tziDateOnly", true, typeof(BooleanType))] // "= UTCToday vs. TZI Date Only"
        // [InlineData("UTCToday() = UTCNow()", true, typeof(BooleanType))] // "= UTCToday UTCNow"

        [InlineData("UTCToday() = dateOnly", true, typeof(BooleanType))] // "= UTCToday vs. Date Only"
        // TODO: the span for operations is potentially incorrect in the IR: it is only the operator, and not the operands
        [InlineData("tziDateTime = userLocalDateOnly", false, null, "Error 12-13: This operation cannot be performed on values which are of different Date Time Behaviors.")] // "= TZI Date Time vs. User Local Date Only"
        [InlineData("dateOnly <= userLocalDateOnly", false, null, "Error 9-11: This operation cannot be performed on values which are of different Date Time Behaviors.")] // "<= Date Only vs. User Local Date Only"
        [InlineData("Day(dateOnly)", true, typeof(DecimalType))] // "Day of Date Only"
        [InlineData("Day(userLocalDateOnly)", false, null, "Error 0-22: Day cannot be performed on this input without a time zone conversion, which is not supported in formula columns.")] // "Day of User Local Date Only"
        [InlineData("WeekNum(dateOnly)", true, typeof(DecimalType))]
        [InlineData("WeekNum(tziDateTime)", true, typeof(DecimalType))]
        [InlineData("WeekNum(tziDateOnly)", true, typeof(DecimalType))]
        [InlineData("WeekNum(userLocalDateOnly)", false, typeof(DecimalType), "Error 0-26: WeekNum cannot be performed on this input without a time zone conversion, which is not supported in formula columns.")]
        [InlineData("WeekNum(userLocalDateTime)", false, typeof(DecimalType), "Error 0-26: WeekNum cannot be performed on this input without a time zone conversion, which is not supported in formula columns.")]
        [InlineData("WeekNum(dateOnly, 2)", false, typeof(DecimalType), "Error 18-19: The start_of_week argument is not supported for the WeekNum function in formula columns.")]
        [InlineData("Hour(Now())", false, null, "Error 0-11: Hour cannot be performed on this input without a time zone conversion, which is not supported in formula columns.")]
        [InlineData("Minute(Now())", false, null, "Error 0-13: Minute cannot be performed on this input without a time zone conversion, which is not supported in formula columns.")]
        [InlineData("Text(Now())", false, null, "Error 5-10: This argument cannot be passed as type DateTime in formula columns.")]
        [InlineData("DateDiff(UTCNow(), Now())", false, null, "Error 0-25: This operation cannot be performed on values which are of different Date Time Behaviors.")]
        [InlineData("Now() < UTCNow()", false, null, "Error 6-7: This operation cannot be performed on values which are of different Date Time Behaviors.")]
        [InlineData("DateAdd(Now(), 1, TimeUnit.Days)", true, typeof(DateTimeType))] // "DateAdd Days User Local"
        [InlineData("IsUTCToday(Now())", true, typeof(BooleanType))] // "IsUTCToday of Now function"
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

        [Fact]
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

            Assert.True(result.IsSuccess);
        }

        [Fact]
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

            Assert.True(result.IsSuccess);
        }


        [Fact]
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

            Assert.True(result.IsSuccess);
        }

        [Theory]
        [InlineData("Float", false, "Error 0-5: Columns of type Double are not supported in formula columns.")] // "Local Float"
        [InlineData("Other.Float", true)] // "Remote non-float with name collision"
        [InlineData("Other.'Actual Float'", false, "Error 5-20: Columns of type Double are not supported in formula columns.")] // "Remote float"
        public void CheckFloatingPoint(string expr, bool success, string error = null)
        {
            // This use of NumberIsFloat and these tests to be redone when the SQL compiler is running on native Decimal
            // Tracked with https://github.com/microsoft/Power-Fx-Dataverse/issues/117
            var provider = new MockXrmMetadataProvider(RelationshipModels);
            var engine = new PowerFx2SqlEngine(RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider));
            var options = new SqlCompileOptions();
            var result = engine.Compile(expr, options);

            Assert.Equal(success, result.IsSuccess);

            if (error == null)
            {
                Assert.Empty(result.Errors);
            }
            else
            {
                Assert.NotNull(result.Errors);
                Assert.Single(result.Errors);
                Assert.Equal(error, result.Errors.First().ToString());
                Assert.NotNull(result.SanitizedFormula);
            }
        }

        [Theory]
        [InlineData("'Virtual Lookup'", "Error 0-16: The result type Record is not supported in formula columns.")] // "Direct virtual lookup access"
        [InlineData("'Virtual Lookup'.'Virtual Data'", "Error 16-31: Cannot reference virtual table Virtual Remotes in formula columns.")] // "Virtual lookup field access"
        public void CheckVirtualLookup(string expr, params string[] errors)
        {
            // This NumberIsFloat should be removed when the SQL compiler is running on native Decimal
            // Tracked with https://github.com/microsoft/Power-Fx-Dataverse/issues/117
            var provider = new MockXrmMetadataProvider(RelationshipModels);
            var engine = new PowerFx2SqlEngine(RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider));
            AssertReturnTypeOrError(engine, expr, false, null, errors);
        }

        [Fact]
        public void CompileLogicalLookup()
        {
            // This NumberIsFloat should be removed when the SQL compiler is running on native Decimal
            // Tracked with https://github.com/microsoft/Power-Fx-Dataverse/issues/117
            var provider = new MockXrmMetadataProvider(RelationshipModels);
            var engine = new PowerFx2SqlEngine(RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider));
            var options = new SqlCompileOptions { UdfName = "fn_udf_Logical" };
            var result = engine.Compile("'Logical Lookup'.Data", options);

            Assert.True(result.IsSuccess);
            // the SqlCreateRow has an embedded newline
            Assert.Equal(@"fn_udf_Logical([localid])
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
                AttributeMetadataModel.NewDecimal("old_price", "Old_Price"),
                AttributeMetadataModel.NewDateTime("new_date", "Date", DateTimeBehavior.DateOnly, DateTimeFormat.DateOnly),
                AttributeMetadataModel.NewDateTime("new_datetime", "DateTime", DateTimeBehavior.TimeZoneIndependent, DateTimeFormat.DateAndTime),
                AttributeMetadataModel.NewMoney("new_currency", "Currency"),
                AttributeMetadataModel.NewDecimal("new_quantity", "Quantity"),
                AttributeMetadataModel.NewLookup("otherid", "Other", new string[] { "remote" }),
                AttributeMetadataModel.NewLookup("selfid", "Self Reference", new string[] { "local" }),
                AttributeMetadataModel.NewLookup("virtualid", "Virtual Lookup", new string[] { "virtualremote" }),
                AttributeMetadataModel.NewLookup("logicalid", "Logical Lookup", new string[] { "remote" }).SetLogical(),
                AttributeMetadataModel.NewGuid("localid", "LocalId"),
                AttributeMetadataModel.NewDouble("float", "Float"),
                AttributeMetadataModel.NewBoolean("new_bool", "Boolean", "true", "false"),
                AttributeMetadataModel.NewInteger("new_int", "Integer"),
                AttributeMetadataModel.NewString("new_string", "String"),
                AttributeMetadataModel.NewGuid("some_id", "SomeId"),
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
                AttributeMetadataModel.NewMoney("currencyField", "Currency Field")
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

        [Fact]
        public void CheckGlobalOptionSets()
        {
            var xrmModel = AllAttributeModel.ToXrm();
            List<OptionSetMetadata> globalOptionSets = new List<OptionSetMetadata>();
            var optionSet1 = new OptionSetMetadata(new OptionMetadataCollection(new List<OptionMetadata>(
                new OptionMetadata[]
                {
                    new OptionMetadata { Label = new Label(new LocalizedLabel("One", 1033), new LocalizedLabel[0]), Value = 1 },
                    new OptionMetadata { Label = new Label(new LocalizedLabel("Two", 1033), new LocalizedLabel[0]), Value = 2 },
                }
            )))
            {
                IsGlobal = true,
                Name = "global1",
                DisplayName = new Label(new LocalizedLabel("Global1", 1033), new LocalizedLabel[0])
            };

            var optionSet2 = new OptionSetMetadata(new OptionMetadataCollection(new List<OptionMetadata>(
                new OptionMetadata[]
                {
                    new OptionMetadata { Label = new Label(new LocalizedLabel("Three", 1033), new LocalizedLabel[0]), Value = 3 },
                    new OptionMetadata { Label = new Label(new LocalizedLabel("Four", 1033), new LocalizedLabel[0]), Value = 4 },
                }
            )))
            {
                IsGlobal = true,
                Name = "global2",
                DisplayName = new Label(new LocalizedLabel("Global2", 1033), new LocalizedLabel[0])
            };

            globalOptionSets.Add(optionSet1);
            globalOptionSets.Add(optionSet2);
            var provider = new MockXrmMetadataProvider(AllAttributeModels);

            // Global optionsets - 'global1', 'global2' are not used by any attribute of the entity, so will not be present in the metadatacache optionsets
            var engine = new PowerFx2SqlEngine(xrmModel, new CdsEntityMetadataProvider(provider));
            var result = engine.Compile("Global2", new SqlCompileOptions());
            Assert.False(result.IsSuccess);
            Assert.Contains("Name isn't valid. 'Global2' isn't recognized", result.Errors.First().ToString());

            // passing list of these global optionsets so that these option sets will also be processed and added to metadatacache optionsets
            var engine2 = new PowerFx2SqlEngine(xrmModel, new CdsEntityMetadataProvider(provider, globalOptionSets: globalOptionSets));
            var result2 = engine2.Compile("Global2", new SqlCompileOptions());
            Assert.False(result2.IsSuccess);
            Assert.Contains("Not supported in formula columns.", result2.Errors.First().ToString());

            result2 = engine2.Compile("(Global2.Three = Global2.Four)", new SqlCompileOptions());
            Assert.True(result2.IsSuccess);
        }

        [Fact]
        public void CheckRelatedEntityCurrencyUsedInFormula()
        {
            var xrmModel = AllAttributeModel.ToXrm();
            var provider = new MockXrmMetadataProvider(AllAttributeModels);
            var engine = new PowerFx2SqlEngine(xrmModel, new CdsEntityMetadataProvider(provider));
            var result = engine.Compile("money + lookup.data3 + lookup.currencyField + 11", new SqlCompileOptions());
            Assert.False(result.IsSuccess);
            Assert.Equal("Error 29-43: Calculations with currency columns in related tables are not currently supported in formula columns.", result.Errors.First().ToString());
        }

        [Theory]
        [InlineData("new_price * new_quantity", "Price * Quantity")] // "Logical Names"
        [InlineData("ThisRecord.new_price + new_quantity", "ThisRecord.Price + Quantity")] // "ThisRecord"
        [InlineData("conflict1 + conflict2", "'Conflict (conflict1)' + 'Conflict (conflict2)'")] // "Conflict"
        [InlineData("new_price + refg.data", "Price + Other.Data")] // "Lookup"
        [InlineData("refg.data + refg.doublerefg.data2 + refg.doublerefg.triplerefg.data3", "Other.Data + Other.'Other Other'.'Data Two' + Other.'Other Other'.'Other Other Other'.'Data Three'")] // "Multiple Lookups"
        [InlineData("refg.data + self.new_price", "Other.Data + 'Self Reference'.Price")] // "Self Reference"
        [InlineData("If(rating = local_rating_optionSet.'1', new_quantity, new_price)", "If(Rating = 'Rating (Locals)'.Hot, Quantity, Price)")] // "CDS Enum literal"
        [InlineData("If(global_pick = [@global_global_pick_optionSet].'2', new_quantity, new_price)", "If('Global Picklist' = [@'Global Picklist'].Medium, Quantity, Price)")] // "CDS Global Enum literal"
        [InlineData("DateAdd(UTCToday(), new_quantity, TimeUnit.Months)", "DateAdd(UTCToday(), Quantity, TimeUnit.Months)")] // "Enum literal"
        [InlineData("/* Comment */\n\n\t  conflict1\n\n\t  \n -conflict2", "/* Comment */\n\n\t  'Conflict (conflict1)'\n\n\t  \n -'Conflict (conflict2)'")] // "Preserves whitespace and comments"
        public void Translate(string expr, string translation)
        {
            // This NumberIsFloat should be removed when the SQL compiler is running on native Decimal
            // Tracked with https://github.com/microsoft/Power-Fx-Dataverse/issues/117
            var provider = new MockXrmMetadataProvider(RelationshipModels);
            var engine = new PowerFx2SqlEngine(RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider));
            var actualTranslation = engine.ConvertToDisplay(expr);
            Assert.Equal(translation, actualTranslation);

            // compile the translated expression and ensure it matches the original logical expression
            var result = engine.Compile(actualTranslation, new SqlCompileOptions());
            Assert.Equal(expr, result.LogicalFormula);
        }

        [Theory]
        [InlineData("Price * Quantity", "#$FieldDecimal$# * #$FieldDecimal$#")] // "Display Names"
        [InlineData("new_price * new_quantity", "#$FieldDecimal$# * #$FieldDecimal$#")] // "Logical Names"
        [InlineData("\"John Smith\"", "#$string$#")] // "String literal"
        [InlineData("123456", "#$decimal$#")] // "Numeric literal"
        [InlineData("If(true,\"John Smith\",Price+7)", "If(#$boolean$#, #$string$#, #$FieldDecimal$# + #$decimal$#)")] // "Function with boolean literal"
        [InlineData("Text(123, \"0000\")", "Text(#$decimal$#, #$string$#)")] // "Text with format string"
        [InlineData("If(123,\"Foo\"", "If(#$decimal$#, #$string$#)")] // "Invalid formula - cleaned up"
        [InlineData("'Conflict (conflict1)' + 'Conflict (conflict2)'", "#$FieldDecimal$# + #$FieldDecimal$#")] // "Conflict"
        [InlineData("Price + Other.Data", "#$FieldDecimal$# + #$FieldLookup$#.#$FieldDecimal$#")] // "Lookup"
        [InlineData("Other.Data + Other.'Other Other'.'Data Two' + Other.'Other Other'.'Other Other Other'.'Data Three'", "#$FieldLookup$#.#$FieldDecimal$# + #$FieldLookup$#.#$FieldLookup$#.#$FieldDecimal$# + #$FieldLookup$#.#$FieldLookup$#.#$FieldLookup$#.#$FieldDecimal$#")] // "Multiple Lookups"
        [InlineData("Other.Data + 'Self Reference'.Price", "#$FieldLookup$#.#$FieldDecimal$# + #$FieldLookup$#.#$FieldDecimal$#")] // "Self Reference"
        [InlineData("If(true, \"random string\", Text(Price))", "If(#$boolean$#, #$string$#, Text(#$FieldDecimal$#))")] // "Function"
        [InlineData("If(Rating = 'Rating (Locals)'.Hot, Quantity, Price)", "If(#$FieldPicklist$# = #$OptionSet$#.#$righthandid$#, #$FieldDecimal$#, #$FieldDecimal$#)")] // "CDS Enum literal"
        [InlineData("If('Global Picklist' = [@'Global Picklist'].Medium, Quantity, Price)", "If(#$FieldPicklist$# = #$FieldPicklist$#.#$righthandid$#, #$FieldDecimal$#, #$FieldDecimal$#)")] // "CDS Global Enum literal"
        [InlineData("DateAdd(UTCToday(), Quantity, TimeUnit.Months)", "DateAdd(UTCToday(), #$FieldDecimal$#, #$Enum$#.#$righthandid$#)")] // "Enum literal"
        [InlineData("/* Comment */\n\n\t  'Conflict (conflict1)'\n\n\t  \n -'Conflict (conflict2)'", "#$FieldDecimal$# + -#$FieldDecimal$#")] // "Preserves whitespace and comments"

        public void Sanitize(string expr, string sanitized)
        {
            var provider = new MockXrmMetadataProvider(RelationshipModels);
            var engine = new PowerFx2SqlEngine(RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider));

            var options = new SqlCompileOptions();
            var result = engine.Compile(expr, options);

            Assert.Equal(sanitized, result.SanitizedFormula);
        }

        [Theory]
        [InlineData("01,200", null, null, false)] // "Default numeric literal with comma"
        [InlineData("01,200", "en-US", null, false)] // "English numeric literal with comma"
        [InlineData("01,200", "fr-FR", "01.200", true)] // "French numeric literal with comma (decimal)"
        [InlineData("01,000", "fr-FR", "01.000", true)] // "French numeric literal with comma (decimal) with all decimal zeros"
        [InlineData("01.200", null, "01.200", true)] // "Default numeric literal with period"
        [InlineData("01.200", "en-US", "01.200", true)] // "English numeric literal with period"
        [InlineData("01.200", "fr-FR", "01.200", false)] // "French numeric literal with period"
        [InlineData("123", null, "123", true)] // "Default whole number literal"
        [InlineData("123", "en-US", "123", true)] // "English whole number literal"
        [InlineData("123", "fr-FR", "123", true)] // "French whole number literal"
        [InlineData("If(true, 1.1, 2)", null, "If(true, 1.1, 2)", true)] // "Default If with comma separators"
        [InlineData("If(true, 1.1, 2)", "en-US", "If(true, 1.1, 2)", true)] // "English If with comma separators"
        [InlineData("If(true, 1, 2)", "fr-FR", null, false)] // "French If with comma separators"
        [InlineData("If(true; 1,1; 2)", "fr-FR", "If(true, 1.1, 2)", true)] // "French If with semicolon separators and comma (decimal)"
        public void CompileLocalizedScripts(string expr, string localeName, string logicalFormula, bool success)
        {
            CultureInfo culture = localeName switch
            {
                null => CultureInfo.InvariantCulture,
                _ => CultureInfo.CreateSpecificCulture(localeName),
            };            

            var engine = new PowerFx2SqlEngine(culture: culture);

            var result = engine.Compile(expr, new SqlCompileOptions());
            if (success)
            {
                Assert.True(result.IsSuccess);
                Assert.Equal(logicalFormula, result.LogicalFormula);

                var translation = engine.ConvertToDisplay(logicalFormula);
                Assert.Equal(expr, translation);
            }
            else
            {
                Assert.False(result.IsSuccess);
            }
        }

        [Fact]
        public void Coalesce()
        {
            // Once we add Coalesce to Library.cs, remove TryCoalesceNum.
            var ok = Functions.Library.TryLookup(Microsoft.PowerFx.Core.Texl.BuiltinFunctionsCore.Coalesce, out var ptr);
            Assert.False(ok);
            Assert.Null(ptr);
        }

        private static string ToStableString(IEnumerable<string> items)
        {
            var array = items.ToArray();
            Array.Sort(array);
            return string.Join(',', array);
        }

        [Fact]
        public async Task UpdateFieldWithError()
        {
            string columnName = "column1";
            FormulaType columnType = new StringType();
            string entityName = "some Entity";
            string errorMessage = "Oups!";

            RecordType recordType = RecordType.Empty().Add(new NamedFormulaType(columnName, columnType));
            DataverseRecordValue dataverseRecordValue = new DataverseRecordValue(new Entity(entityName, Guid.NewGuid()), new EntityMetadata() { LogicalName = entityName }, recordType, new FakeConnectionValueContext());
            RecordValue recordValue = FormulaValue.NewRecordFromFields(new NamedValue(columnName, new ErrorValue(Core.IR.IRContext.NotInSource(columnType), new ExpressionError() { Message = errorMessage })));

            DValue<RecordValue> result = await dataverseRecordValue.UpdateFieldsAsync(recordValue, CancellationToken.None).ConfigureAwait(false);

            Assert.NotNull(result);
            Assert.Null(result.Value);
            Assert.NotNull(result.Error);

            Assert.Equal($"Field {columnName} is of type ErrorValue: {errorMessage}", result.Error.Errors[0].Message);
        }
    }

    // Helpers to leverage the EditorContextScope
    public static class DataverseEngineExtensions
    {
        public static string ConvertToDisplay(this DataverseEngine engine, string expression)
        {
            IPowerFxScope scope = engine.CreateEditorScope(symbols: null);
            var displayExpr = scope.ConvertToDisplay(expression);
            return displayExpr;
        }

        public static IIntellisenseResult Suggest(this DataverseEngine engine, string expression, int cursorPosition)
        {
            IPowerFxScope scope = engine.CreateEditorScope(symbols: null);
            var results = scope.Suggest(expression, cursorPosition);
            return results;
        }
    }

    public class FakeConnectionValueContext : IConnectionValueContext
    {
        public IDataverseServices Services => throw new NotImplementedException();

        public int MaxRows => throw new NotImplementedException();

        public EntityMetadata GetMetadataOrThrow(string tableLogicalName)
        {
            throw new NotImplementedException();
        }

        public RecordType GetRecordType(string tableLogicalName)
        {
            throw new NotImplementedException();
        }

        public string GetSerializationName(string tableLogicalName)
        {
            throw new NotImplementedException();
        }

        public RecordValue Marshal(Entity entity)
        {
            throw new NotImplementedException();
        }
    }
}
