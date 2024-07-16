//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Dataverse.EntityMock;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Dataverse.Functions;
using Microsoft.PowerFx.Dataverse.Tests.DelegationTests;
using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Xunit;
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

        public const string FloatMinMaxUDF = @"CREATE FUNCTION fn_testUdf1(
    @v0 float, -- new_field
    @v1 float -- new_field1
) RETURNS float
  WITH SCHEMABINDING
AS BEGIN
    DECLARE @v2 float
    DECLARE @v3 decimal(23,10)
    DECLARE @v4 float
    DECLARE @v5 float

    -- expression body
    SET @v2 = TRY_CAST((ISNULL(@v0,0) * ISNULL(@v1,0)) AS FLOAT)
    IF(@v2 IS NULL) BEGIN RETURN NULL END
    IF(@v2<-100000000000 OR @v2>100000000000) BEGIN RETURN NULL END
    SET @v3 = 2.0
    SET @v4 = @v3
    SET @v5 = TRY_CAST((ISNULL(@v2,0) * ISNULL(@v4,0)) AS FLOAT)
    IF(@v5 IS NULL) BEGIN RETURN NULL END
    -- end expression body

    IF(@v5<-100000000000 OR @v5>100000000000) BEGIN RETURN NULL END
    RETURN ROUND(@v5, 3)
END
";

        public const string FloatDefaultMinMaxUDF = @"CREATE FUNCTION fn_testUdf1(
    @v0 float, -- new_field
    @v1 float -- new_field1
) RETURNS float
  WITH SCHEMABINDING
AS BEGIN
    DECLARE @v2 float
    DECLARE @v3 decimal(23,10)
    DECLARE @v4 float
    DECLARE @v5 float

    -- expression body
    SET @v2 = TRY_CAST((ISNULL(@v0,0) * ISNULL(@v1,0)) AS FLOAT)
    IF(@v2 IS NULL) BEGIN RETURN NULL END
    IF(@v2<-100000000000 OR @v2>100000000000) BEGIN RETURN NULL END
    SET @v3 = 2.0
    SET @v4 = @v3
    SET @v5 = TRY_CAST((ISNULL(@v2,0) * ISNULL(@v4,0)) AS FLOAT)
    IF(@v5 IS NULL) BEGIN RETURN NULL END
    -- end expression body

    IF(@v5<-100000000000 OR @v5>100000000000) BEGIN RETURN NULL END
    RETURN @v5
END
";

        [Fact]
        public void ValidateCompileFloatMinMaxPrecisionBehavior()
        {
            var expr = "field*field1*2.0";
            var model = new EntityMetadataModel
            {
                Attributes = new AttributeMetadataModel[]
                {
                    new AttributeMetadataModel
                     {
                         LogicalName= "new_field",
                         DisplayName = "field",
                         AttributeType = AttributeTypeCode.Double
                     },
                    new AttributeMetadataModel
                     {
                         LogicalName= "new_field1",
                         DisplayName = "field1",
                         AttributeType = AttributeTypeCode.Double
                     },
                }
            };

            var metadata = model.ToXrm();

            var engine = new PowerFx2SqlEngine(metadata, dataverseFeatures: new DataverseFeatures() { IsFloatingPointEnabled = true });

            SqlCompileOptions options = new SqlCompileOptions() { CreateMode = SqlCompileOptions.Mode.Create, UdfName = "fn_testUdf1" };

            // in case client is supplying min and max values for float, those values will not be honored and default float min max values will be entertained
            // it will only entertain precision coming in type hints and will do round off based on that precision
            options.TypeHints = new SqlCompileOptions.TypeDetails { TypeHint = AttributeTypeCode.Double, MinValue = 2, MaxValue = 10, Precision = 3 };
            
            var result = engine.Compile(expr, options);

            Assert.NotNull(result);
            Assert.NotNull(result.SqlFunction);
            Assert.NotNull(result.SqlCreateRow);
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Errors);

            Assert.Equal(FloatMinMaxUDF, result.SqlFunction);
            Assert.True(result.ReturnType is NumberType);
            Assert.Equal(2,result.TopLevelIdentifiers.Count);
            Assert.Equal("new_field", result.TopLevelIdentifiers.First());
            Assert.Equal("new_field*new_field1*2.0", result.LogicalFormula);

            // in case no type hints are coming, then it uses default metadata min max values for float and will do round off at end,
            // compiler will not assume any precision by itself
            options = new SqlCompileOptions() { CreateMode = SqlCompileOptions.Mode.Create, UdfName = "fn_testUdf1" };
            result = engine.Compile(expr, options);

            Assert.NotNull(result);
            Assert.NotNull(result.SqlFunction);
            Assert.NotNull(result.SqlCreateRow);
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Errors);

            Assert.Equal(FloatDefaultMinMaxUDF, result.SqlFunction);
            Assert.True(result.ReturnType is NumberType);
            Assert.Equal(2, result.TopLevelIdentifiers.Count);
            Assert.Equal("new_field", result.TopLevelIdentifiers.First());
            Assert.Equal("new_field*new_field1*2.0", result.LogicalFormula);
        }

        public const string BaselineCurrencyFunction = @"CREATE FUNCTION fn_testUdf1(
    @v0 decimal(23,10), -- new_field
    @v1 decimal(38,10) -- new_field1
) RETURNS decimal(23,10)
  WITH SCHEMABINDING
AS BEGIN
    DECLARE @v2 decimal(23,10)
    DECLARE @v3 decimal(23,10)
    DECLARE @v4 decimal(23,10)
    DECLARE @v5 decimal(23,10)

    -- expression body
    SET @v2 = TRY_CAST((ISNULL(@v1,0)) AS decimal(23,10))
    IF(@v2 IS NULL) BEGIN RETURN NULL END
    IF(@v2<-100000000000 OR @v2>100000000000) BEGIN RETURN NULL END
    SET @v3 = TRY_CAST((ISNULL(@v0,0) * ISNULL(@v2,0)) AS decimal(23,10))
    IF(@v3 IS NULL) BEGIN RETURN NULL END
    IF(@v3<-100000000000 OR @v3>100000000000) BEGIN RETURN NULL END
    SET @v4 = 2.0
    SET @v5 = TRY_CAST((ISNULL(@v3,0) * ISNULL(@v4,0)) AS decimal(23,10))
    IF(@v5 IS NULL) BEGIN RETURN NULL END
    -- end expression body

    IF(@v5<-100000000000 OR @v5>100000000000) BEGIN RETURN NULL END
    RETURN ROUND(@v5, 10)
END
";
        [Fact]
        public void CheckCurrencyCompile()
        {
            var expr = "\t\t\nfield*Decimal(field1)*\n2.0\t";

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
            Assert.Equal(2, result.TopLevelIdentifiers.Count);
            Assert.Equal("new_field", result.TopLevelIdentifiers.First());
            Assert.Equal("\t\t\nnew_field*Decimal(new_field1)*\n2.0\t", result.LogicalFormula);
        }

        public const string BaselineSingleCurrencyFunction = @"CREATE FUNCTION fn_testUdf1(
    @v0 decimal(38,10) -- new_field
) RETURNS decimal(23,10)
  WITH SCHEMABINDING
AS BEGIN
    DECLARE @v1 decimal(23,10)

    -- expression body
    SET @v1 = TRY_CAST((ISNULL(@v0,0)) AS decimal(23,10))
    IF(@v1 IS NULL) BEGIN RETURN NULL END
    -- end expression body

    IF(@v1<-100000000000 OR @v1>100000000000) BEGIN RETURN NULL END
    RETURN ROUND(@v1, 10)
END
";

        [Fact]
        public void CheckSingleCurrencyFieldCompile()
        {
            var expr = "Decimal(field)";

            var model = new EntityMetadataModel
            {
                Attributes = new AttributeMetadataModel[]
                {
                    new AttributeMetadataModel
                     {
                         LogicalName= "new_field",
                         DisplayName = "field",
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

            Assert.Equal(BaselineSingleCurrencyFunction, result.SqlFunction);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Errors);

            Assert.True(result.ReturnType is DecimalType);
            Assert.Single(result.TopLevelIdentifiers);
            Assert.Equal("new_field", result.TopLevelIdentifiers.First());
            Assert.Equal("Decimal(new_field)", result.LogicalFormula);
        }

        public const string BaselineSingleCurrencyFunctionWithHints = @"CREATE FUNCTION fn_testUdf1(
    @v0 decimal(38,10) -- new_field
) RETURNS decimal(23,10)
  WITH SCHEMABINDING
AS BEGIN
    DECLARE @v1 decimal(23,10)

    -- expression body
    SET @v1 = TRY_CAST((ISNULL(@v0,0)) AS decimal(23,10))
    IF(@v1 IS NULL) BEGIN RETURN NULL END
    -- end expression body

    IF(@v1<-100000000000 OR @v1>100000000000) BEGIN RETURN NULL END
    RETURN ROUND(@v1, 3)
END
";

        [Fact]
        public void CheckSingleCurrencyFieldCompileWithHints()
        {
            var expr = "Decimal(field)";

            var model = new EntityMetadataModel
            {
                Attributes = new AttributeMetadataModel[]
                {
                    new AttributeMetadataModel
                     {
                         LogicalName= "new_field",
                         DisplayName = "field",
                         AttributeType = AttributeTypeCode.Money
                     },
                }
            };

            var options = new SqlCompileOptions
            {
                CreateMode = SqlCompileOptions.Mode.Create,
                UdfName = "fn_testUdf1"
            };

            options.TypeHints = new SqlCompileOptions.TypeDetails
            {
                TypeHint = AttributeTypeCode.Decimal,
                Precision = 3
            };

            var metadata = model.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);
            var result = engine.Compile(expr, options);

            Assert.NotNull(result);
            Assert.NotNull(result.SqlFunction);
            Assert.NotNull(result.SqlCreateRow);

            Assert.Equal(BaselineSingleCurrencyFunctionWithHints, result.SqlFunction);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Errors);

            Assert.True(result.ReturnType is DecimalType);
            Assert.Single(result.TopLevelIdentifiers);
            Assert.Equal("new_field", result.TopLevelIdentifiers.First());
            Assert.Equal("Decimal(new_field)", result.LogicalFormula);
        }

        public const string BaselineExchangeFunction = @"CREATE FUNCTION fn_testUdf1(
    @v0 decimal(28,12), -- exchangerate
    @v2 decimal(38,10) -- new_field1
) RETURNS decimal(23,10)
  WITH SCHEMABINDING
AS BEGIN
    DECLARE @v1 decimal(23,10)
    DECLARE @v3 decimal(23,10)
    DECLARE @v4 decimal(23,10)
    DECLARE @v5 decimal(23,10)
    DECLARE @v6 decimal(23,10)

    -- expression body
    SET @v1 = TRY_CAST((ISNULL(@v0,0)) AS decimal(23,10))
    IF(@v1 IS NULL) BEGIN RETURN NULL END
    IF(@v1<-100000000000 OR @v1>100000000000) BEGIN RETURN NULL END
    SET @v3 = TRY_CAST((ISNULL(@v2,0)) AS decimal(23,10))
    IF(@v3 IS NULL) BEGIN RETURN NULL END
    IF(@v3<-100000000000 OR @v3>100000000000) BEGIN RETURN NULL END
    SET @v4 = TRY_CAST((ISNULL(@v1,0) * ISNULL(@v3,0)) AS decimal(23,10))
    IF(@v4 IS NULL) BEGIN RETURN NULL END
    IF(@v4<-100000000000 OR @v4>100000000000) BEGIN RETURN NULL END
    SET @v5 = 2.0
    SET @v6 = TRY_CAST((ISNULL(@v4,0) * ISNULL(@v5,0)) AS decimal(23,10))
    IF(@v6 IS NULL) BEGIN RETURN NULL END
    -- end expression body

    IF(@v6<-100000000000 OR @v6>100000000000) BEGIN RETURN NULL END
    RETURN ROUND(@v6, 10)
END
";

        [Fact]
        public void CheckExchangeRateCompile()
        {
            var expr = "\t\t\nDecimal(exchangerate)*Decimal(field1)*\n2.0\t";

            var model = new EntityMetadataModel
            {
                Attributes = new AttributeMetadataModel[]
                {
                    new AttributeMetadataModel
                     {
                         LogicalName= "exchangerate",
                         DisplayName = "exchangerate",
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

            Assert.Equal(BaselineExchangeFunction, result.SqlFunction);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Errors);

            Assert.True(result.ReturnType is DecimalType);
            Assert.Equal(2, result.TopLevelIdentifiers.Count);
            Assert.Equal("exchangerate", result.TopLevelIdentifiers.First());
            Assert.Equal("\t\t\nDecimal(exchangerate)*Decimal(new_field1)*\n2.0\t", result.LogicalFormula);
        }

        [Fact]
        public void CheckCurrencyExchangeRateCompile()
        {
            var expr = "decimal + money";

            var model = new EntityMetadataModel
            {
                Attributes = new AttributeMetadataModel[]
                {
                    new AttributeMetadataModel
                    {
                         LogicalName= "exchangerate",
                         AttributeType = AttributeTypeCode.Decimal
                    },
                    new AttributeMetadataModel
                    {
                         LogicalName= "money",
                         AttributeType = AttributeTypeCode.Money
                    },
                    new AttributeMetadataModel
                    {
                         LogicalName= "decimal",
                         AttributeType = AttributeTypeCode.Decimal
                    }
                }
            };

            var metadata = model.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);
            var result = engine.Compile(expr, new SqlCompileOptions());
            Assert.False(result.IsSuccess);
            Assert.Equal("Error 10-15: Direct use of currency fields is not yet supported. Use Decimal(CurrencyField) as a workaround but note that decimal has a smaller range than currency.", result.Errors.First().ToString());

            expr = "exchangerate * money";
            result = engine.Compile(expr, new SqlCompileOptions());
            Assert.False(result.IsSuccess);
            Assert.Equal("Error 0-12: Direct use of currency fields is not yet supported. Use Decimal(CurrencyField) as a workaround but note that decimal has a smaller range than currency.", result.Errors.First().ToString());

        }

        public const string FloatingPointArithmeticOperationsUDF = @"CREATE FUNCTION fn_testUdf1(
    @v0 float -- field
) RETURNS float
  WITH SCHEMABINDING
AS BEGIN
    DECLARE @v1 decimal(23,10)
    DECLARE @v2 float
    DECLARE @v3 float

    -- expression body
    SET @v1 = 2
    SET @v2 = @v1
    SET @v3 = TRY_CAST((ISNULL(@v0,0) * ISNULL(@v2,0)) AS FLOAT)
    IF(@v3 IS NULL) BEGIN RETURN NULL END
    -- end expression body

    IF(@v3<-100000000000 OR @v3>100000000000) BEGIN RETURN NULL END
    RETURN @v3
END
";

        [Fact]
        public void CheckArithmeticOperationsFloatingPoint()
        {
            var expr = "field*2";

            var model = new EntityMetadataModel
            {
                Attributes = new AttributeMetadataModel[]
                {
                    new AttributeMetadataModel
                    {
                         LogicalName= "field",
                         AttributeType = AttributeTypeCode.Double
                    },
                    new AttributeMetadataModel
                    {
                         LogicalName= "field1",
                         AttributeType = AttributeTypeCode.Decimal
                    }
                }
            };

            var metadata = model.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata, dataverseFeatures : new DataverseFeatures() { IsFloatingPointEnabled = true});
            var result = engine.Compile(expr, new SqlCompileOptions() { UdfName = "fn_testUdf1" });
            Assert.True(result.IsSuccess);
            Assert.Equal(FormulaType.Number, result.ReturnType);
            Assert.Equal(FloatingPointArithmeticOperationsUDF, result.SqlFunction);

            expr = "field*field1*2";

            result = engine.Compile(expr, new SqlCompileOptions());
            Assert.True(result.IsSuccess);
            Assert.Equal(FormulaType.Number, result.ReturnType);

            expr = "field/field1";

            result = engine.Compile(expr, new SqlCompileOptions());
            Assert.True(result.IsSuccess);
            Assert.Equal(FormulaType.Number, result.ReturnType);

            expr = "field+field1";

            result = engine.Compile(expr, new SqlCompileOptions());
            Assert.True(result.IsSuccess);
            Assert.Equal(FormulaType.Number, result.ReturnType);

            expr = "field1-field";

            result = engine.Compile(expr, new SqlCompileOptions());
            Assert.True(result.IsSuccess);
            Assert.Equal(FormulaType.Number, result.ReturnType);
            Assert.Equal("#$FieldDecimal$# + -#$FieldDouble$#", result.SanitizedFormula);

            expr = "field%";

            result = engine.Compile(expr, new SqlCompileOptions());
            Assert.True(result.IsSuccess);
            Assert.Equal(FormulaType.Number, result.ReturnType);
            Assert.Equal("#$FieldDouble$#%", result.SanitizedFormula);

        }

        public const string DecimalFormulaProducingFloatUDF = @"CREATE FUNCTION fn_testUdf1(
    @v0 decimal(23,10) -- field1
) RETURNS float
  WITH SCHEMABINDING
AS BEGIN
    DECLARE @v1 decimal(23,10)
    DECLARE @v2 decimal(23,10)

    -- expression body
    SET @v1 = 2
    SET @v2 = TRY_CAST((ISNULL(@v0,0) * ISNULL(@v1,0)) AS decimal(23,10))
    IF(@v2 IS NULL) BEGIN RETURN NULL END
    -- end expression body

    IF(@v2<-100000000000 OR @v2>100000000000) BEGIN RETURN NULL END
    RETURN ROUND(@v2, 5)
END
";


        [Theory]
        [InlineData("Error 5-6: The result type for this formula is expected to be Decimal, but the actual result type is Float. The result type of a formula column cannot be changed.")] 
        public void CheckFloatingPointWithHint(string errorMessage)
        {
            /*
            * Formula Producing Float don't honor hints because Float is approximate data type and is unassignable to other data type
            * like whole no/decimal/currency
            */
            var expr = "field*2";

            var model = new EntityMetadataModel
            {
                Attributes = new AttributeMetadataModel[]
                {
                    new AttributeMetadataModel
                    {
                         LogicalName= "field",
                         AttributeType = AttributeTypeCode.Double
                    },
                    new AttributeMetadataModel
                    {
                         LogicalName= "field1",
                         AttributeType = AttributeTypeCode.Decimal
                    }
                }
            };

            var metadata = model.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata, dataverseFeatures: new DataverseFeatures() { IsFloatingPointEnabled = true });

            var options = new SqlCompileOptions
            {
                TypeHints = new SqlCompileOptions.TypeDetails { TypeHint = AttributeTypeCode.Decimal }
            };

            var result = engine.Compile(expr, options);
            
            Assert.False(result.IsSuccess);
            
            Assert.NotEmpty(result.Errors);
            var errors = result.Errors.ToArray();
            Assert.Single(errors);
            Assert.Equal(errorMessage, errors[0].ToString());

            Assert.False(result.IsHintApplied);

            /*
            * Formula Producing decimal can be converted to Float if Float is coming in hint then it will honor hint and will produce float
            * because decimal is accurate data type and is assignable to approximate data type like real/float
            */
            expr = "field1*2";

            options = new SqlCompileOptions
            {
                TypeHints = new SqlCompileOptions.TypeDetails { TypeHint = AttributeTypeCode.Double, Precision = 5 },
                UdfName = "fn_testUdf1"
            };

            result = engine.Compile(expr, options);

            Assert.True(result.IsSuccess);
            Assert.Equal(FormulaType.Number, result.ReturnType);
            Assert.Equal(DecimalFormulaProducingFloatUDF, result.SqlFunction);
        }

        public const string PercentIntermediateOperationsUDF = @"CREATE FUNCTION fn_testUdf1(
) RETURNS decimal(23,10)
  WITH SCHEMABINDING
AS BEGIN
    DECLARE @v0 decimal(23,10)
    DECLARE @v1 decimal(23,10)
    DECLARE @v2 decimal(23,10)
    DECLARE @v3 decimal(23,10)

    -- expression body
    SET @v0 = 12
    SET @v1 = (ISNULL(@v0,0)/100.0)
    IF(@v1<-100000000000 OR @v1>100000000000) BEGIN RETURN NULL END
    SET @v2 = 1
    SET @v3 = TRY_CAST((ISNULL(@v1,0) + ISNULL(@v2,0)) AS decimal(23,10))
    IF(@v3 IS NULL) BEGIN RETURN NULL END
    -- end expression body

    IF(@v3<-100000000000 OR @v3>100000000000) BEGIN RETURN NULL END
    RETURN ROUND(@v3, 10)
END
";

        [Fact]
        public void PercentIntermediateOperationsTest()
        {
            var expr = "12% + 1";

            var engine = new PowerFx2SqlEngine();
            var result = engine.Compile(expr, new SqlCompileOptions() { UdfName = "fn_testUdf1" });

            Assert.NotNull(result);
            Assert.NotNull(result.SqlFunction);
            Assert.Equal(PercentIntermediateOperationsUDF, result.SqlFunction);
        }


        [Fact]
        public void PowerFunctionBlockedTest()
        {
            var expr = "Power(2,5)";

            var engine = new PowerFx2SqlEngine(dataverseFeatures: new DataverseFeatures() { IsFloatingPointEnabled = true });
            var result = engine.Compile(expr, new SqlCompileOptions());

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Errors);
            Assert.True(result.ReturnType is NumberType);

            engine = new PowerFx2SqlEngine(dataverseFeatures: new DataverseFeatures() { IsFloatingPointEnabled = false });
            result = engine.Compile(expr, new SqlCompileOptions());

            Assert.NotNull(result);
            Assert.False(result.IsSuccess);
            Assert.NotEmpty(result.Errors);
            var errors = result.Errors.ToArray();
            Assert.Single(errors);
            Assert.Equal("'Power' is an unknown or unsupported function.", errors[0].Message);
        }

        [Fact]
        public void SqrtFunctionTest()
        {
            var expr = "Sqrt(16)";

            var engine = new PowerFx2SqlEngine(dataverseFeatures: new DataverseFeatures() { IsFloatingPointEnabled = true });
            var result = engine.Compile(expr, new SqlCompileOptions());

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Errors);
            Assert.True(result.ReturnType is NumberType);

            engine = new PowerFx2SqlEngine(dataverseFeatures: new DataverseFeatures() { IsFloatingPointEnabled = false });
            result = engine.Compile(expr, new SqlCompileOptions());

            Assert.NotNull(result);
            Assert.False(result.IsSuccess);
            Assert.NotEmpty(result.Errors);
            var errors = result.Errors.ToArray();
            Assert.Single(errors);
            Assert.Equal("'Sqrt' is an unknown or unsupported function.", errors[0].Message);
        }

        [Fact]
        public void LnFunctionTest()
        {
            var expr = "Ln(20)";

            var engine = new PowerFx2SqlEngine(dataverseFeatures: new DataverseFeatures() { IsFloatingPointEnabled = true });
            var result = engine.Compile(expr, new SqlCompileOptions());

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Errors);
            Assert.True(result.ReturnType is NumberType);

            engine = new PowerFx2SqlEngine(dataverseFeatures: new DataverseFeatures() { IsFloatingPointEnabled = false });
            result = engine.Compile(expr, new SqlCompileOptions());

            Assert.NotNull(result);
            Assert.False(result.IsSuccess);
            Assert.NotEmpty(result.Errors);
            var errors = result.Errors.ToArray();
            Assert.Single(errors);
            Assert.Equal("'Ln' is an unknown or unsupported function.", errors[0].Message);
        }

        [Fact]
        public void ExpFunctionTest()
        {
            var expr = "Exp(10)";

            var engine = new PowerFx2SqlEngine(dataverseFeatures: new DataverseFeatures() { IsFloatingPointEnabled = true });
            var result = engine.Compile(expr, new SqlCompileOptions());

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Errors);
            Assert.True(result.ReturnType is NumberType);

            engine = new PowerFx2SqlEngine(dataverseFeatures: new DataverseFeatures() { IsFloatingPointEnabled = false });
            result = engine.Compile(expr, new SqlCompileOptions());

            Assert.NotNull(result);
            Assert.False(result.IsSuccess);
            Assert.NotEmpty(result.Errors);
            var errors = result.Errors.ToArray();
            Assert.Single(errors);
            Assert.Equal("'Exp' is an unknown or unsupported function.", errors[0].Message);
        }

        public const string FloatFunctionUDF = @"CREATE FUNCTION fn_testUdf1(
) RETURNS float
  WITH SCHEMABINDING
AS BEGIN
    DECLARE @v0 decimal(23,10)
    DECLARE @v1 float

    -- expression body
    SET @v0 = 5
    SET @v1 = @v0
    -- end expression body

    IF(@v1<-100000000000 OR @v1>100000000000) BEGIN RETURN NULL END
    RETURN @v1
END
";

        [Fact]
        public void CheckDecimalFloatFunctions()
        {
            var engine = new PowerFx2SqlEngine(dataverseFeatures: new DataverseFeatures() { IsFloatingPointEnabled = true });
            var result = engine.Compile("Float(5)", new SqlCompileOptions() { UdfName = "fn_testUdf1" });
            Assert.True(result.IsSuccess);
            Assert.Equal(FormulaType.Number, result.ReturnType);
            Assert.Equal(FloatFunctionUDF, result.SqlFunction);

            result = engine.Compile("Float(Decimal(5))", new SqlCompileOptions());
            Assert.True(result.IsSuccess);
            Assert.Equal(FormulaType.Number, result.ReturnType);

            result = engine.Compile("Decimal(5)", new SqlCompileOptions()); 
            Assert.True(result.IsSuccess);

            result = engine.Compile("Decimal(Ln(20))", new SqlCompileOptions()); 
            Assert.True(result.IsSuccess);
            Assert.Equal(FormulaType.Decimal, result.ReturnType);

            result = engine.Compile("Decimal(Float(25))", new SqlCompileOptions());
            Assert.True(result.IsSuccess);
            Assert.Equal(FormulaType.Decimal, result.ReturnType);

            result = engine.Compile("RoundUp(1.15,1)", new SqlCompileOptions() { UdfName = "fn_testUdf1" });
            Assert.Equal("RoundUp:w(1.15:w, Coalesce:n(Float:n(1:w), 0:n))", result.ApplyIR().TopNode.ToString()); // Decimal and Float functions are supported from IR
            Assert.True(result.IsSuccess);

            // Floating Point feature disabled
           
            engine = new PowerFx2SqlEngine(dataverseFeatures: new DataverseFeatures() { IsFloatingPointEnabled = false });
            result = engine.Compile("Float(5)", new SqlCompileOptions());
            Assert.NotNull(result);
            Assert.False(result.IsSuccess);
            Assert.NotEmpty(result.Errors);
            var errors = result.Errors.ToArray();
            Assert.Single(errors);
            Assert.Equal("'Float' is an unknown or unsupported function.", errors[0].Message);

            result = engine.Compile("Decimal(Float(25))", new SqlCompileOptions());
            Assert.NotNull(result);
            Assert.False(result.IsSuccess);
            Assert.NotEmpty(result.Errors);
            errors = result.Errors.ToArray();
            Assert.Equal(2, errors.Length);
            Assert.Equal("'Float' is an unknown or unsupported function.", errors[0].Message);

            // Float functions is internally supported from IR even though Floating Point feature is disabled and it will produce decimal 
            // in that case to be in parity with GA behavior
            result = engine.Compile("RoundUp(1.15,1)", new SqlCompileOptions());
            Assert.Equal("RoundUp:w(1.15:w, Coalesce:n(Float:n(1:w), 0:n))", result.ApplyIR().TopNode.ToString()); 
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void CheckOverloadedModFunction()
        {
            // Mod function is overloaded to produce Decimal or Float based on the expression passed
            CallEngineAndVerifyResult("Mod(2,4)", FormulaType.Decimal, "Mod(#$decimal$#, #$decimal$#)"); // producing decimal
            CallEngineAndVerifyResult("Mod(Float(2),4)", FormulaType.Number, "Mod(Float(#$decimal$#), #$decimal$#)"); // producing float
            CallEngineAndVerifyResult("Mod(4, Float(2))", FormulaType.Number, "Mod(#$decimal$#, Float(#$decimal$#))"); // producing decimal because first arg derives the return type of formula

            // if floating point FCB is disabled then user can't use Float function directly in formula but internally from IR, it would be supported
            CallEngineAndVerifyResult("Mod(Float(2),4)", null, "Mod(Float(#$decimal$#), #$decimal$#)", isFloatingPointEnabled : false, isSuccess : false, errorMsg : "'Float' is an unknown or unsupported function."); 
            CallEngineAndVerifyResult("Mod(4, Float(2))", null, "Mod(#$decimal$#, Float(#$decimal$#))", isFloatingPointEnabled: false, isSuccess: false, errorMsg: "'Float' is an unknown or unsupported function."); 
        }

        [Fact]
        public void CheckOverloadedRoundFunction()
        {
            // Round function is overloaded to produce Decimal or Float based on the expression passed
            CallEngineAndVerifyResult("Round(132.133,2)", FormulaType.Decimal, "Round(#$decimal$#, #$decimal$#)"); // producing decimal
            CallEngineAndVerifyResult("Round(Float(132.133),2)", FormulaType.Number, "Round(Float(#$decimal$#), #$decimal$#)"); // producing float
            CallEngineAndVerifyResult("Round(4, Float(132.22))", FormulaType.Decimal, "Round(#$decimal$#, Float(#$decimal$#))"); // producing decimal because first arg derives the return type of formula

            // RoundUp function is overloaded to produce Decimal or Float based on the expression passed
            CallEngineAndVerifyResult("RoundUp(132.133,2)", FormulaType.Decimal, "RoundUp(#$decimal$#, #$decimal$#)");
            CallEngineAndVerifyResult("RoundUp(Float(132.133),2)", FormulaType.Number, "RoundUp(Float(#$decimal$#), #$decimal$#)"); 
            CallEngineAndVerifyResult("RoundUp(4, Float(132.22))", FormulaType.Decimal, "RoundUp(#$decimal$#, Float(#$decimal$#))");

            // RoundDown function is overloaded to produce Decimal or Float based on the expression passed
            CallEngineAndVerifyResult("RoundDown(132.133,2)", FormulaType.Decimal, "RoundDown(#$decimal$#, #$decimal$#)");
            CallEngineAndVerifyResult("RoundDown(Float(132.133),2)", FormulaType.Number, "RoundDown(Float(#$decimal$#), #$decimal$#)");
            CallEngineAndVerifyResult("RoundDown(4, Float(132.22))", FormulaType.Decimal, "RoundDown(#$decimal$#, Float(#$decimal$#))");


            // FCB Floating Point disabled
            CallEngineAndVerifyResult("Round(Float(132.133),2)", null, "Round(Float(#$decimal$#), #$decimal$#)", isFloatingPointEnabled: false, isSuccess: false, errorMsg: "'Float' is an unknown or unsupported function.");
            CallEngineAndVerifyResult("Round(4, Float(132.22))", null, "Round(#$decimal$#, Float(#$decimal$#))", isFloatingPointEnabled: false, isSuccess: false, errorMsg: "'Float' is an unknown or unsupported function."); 

            CallEngineAndVerifyResult("RoundUp(Float(132.133),2)", null, "RoundUp(Float(#$decimal$#), #$decimal$#)", isFloatingPointEnabled: false, isSuccess: false, errorMsg: "'Float' is an unknown or unsupported function.");
            CallEngineAndVerifyResult("RoundUp(4, Float(132.22))", null, "RoundUp(#$decimal$#, Float(#$decimal$#))", isFloatingPointEnabled: false, isSuccess: false, errorMsg: "'Float' is an unknown or unsupported function.");

            CallEngineAndVerifyResult("RoundDown(Float(132.133),2)", null, "RoundDown(Float(#$decimal$#), #$decimal$#)", isFloatingPointEnabled: false, isSuccess: false, errorMsg: "'Float' is an unknown or unsupported function.");
            CallEngineAndVerifyResult("RoundDown(4, Float(132.22))", null, "RoundDown(#$decimal$#, Float(#$decimal$#))", isFloatingPointEnabled: false, isSuccess: false, errorMsg: "'Float' is an unknown or unsupported function.");

        }

        [Fact]
        public void CheckOverloadedTruncFunction()
        {
            // Trunc function is overloaded to produce Decimal or Float based on the expression passed
            CallEngineAndVerifyResult("Trunc(132.133)", FormulaType.Decimal, "Trunc(#$decimal$#)"); 
            CallEngineAndVerifyResult("Trunc(Float(132.133))", FormulaType.Number, "Trunc(Float(#$decimal$#))"); 
            CallEngineAndVerifyResult("Trunc(4, Float(132.22))", FormulaType.Decimal, "Trunc(#$decimal$#, Float(#$decimal$#))");
            CallEngineAndVerifyResult("Trunc(Float(132.22), 3)", FormulaType.Number, "Trunc(Float(#$decimal$#), #$decimal$#)"); 
            CallEngineAndVerifyResult("Trunc(Float(132.22), Float(132.22))", FormulaType.Number, "Trunc(Float(#$decimal$#), Float(#$decimal$#))");

            // FCB Floating Point  disabled
            CallEngineAndVerifyResult("Trunc(Float(132.133))", null, "Trunc(Float(#$decimal$#))", isFloatingPointEnabled: false, isSuccess: false, errorMsg: "'Float' is an unknown or unsupported function.");
            CallEngineAndVerifyResult("Trunc(4, Float(132.22))", null, "Trunc(#$decimal$#, Float(#$decimal$#))", isFloatingPointEnabled: false, isSuccess: false, errorMsg: "'Float' is an unknown or unsupported function.");
            CallEngineAndVerifyResult("Trunc(Float(132.22), 3)", null, "Trunc(Float(#$decimal$#), #$decimal$#)", isFloatingPointEnabled: false, isSuccess: false, errorMsg: "'Float' is an unknown or unsupported function.");
            CallEngineAndVerifyResult("Trunc(Float(132.22), Float(132.22))", null, "Trunc(Float(#$decimal$#), Float(#$decimal$#))", isFloatingPointEnabled: false, isSuccess: false, errorMsg: "'Float' is an unknown or unsupported function.");

        }

        [Fact]
        public void CheckLeftRightOverloadedFunction()
        {
            // Left n Right function treats 2nd arg as float even if literal value is given
            CallEngineAndVerifyResult("Left(\"abc\", 1)", FormulaType.String, "Left(#$string$#, #$decimal$#)");
            CallEngineAndVerifyResult("Left(\"abc\", Float(1))", FormulaType.String, "Left(#$string$#, Float(#$decimal$#))");
            CallEngineAndVerifyResult("Right(\"abc\", 1)", FormulaType.String, "Right(#$string$#, #$decimal$#)");
            CallEngineAndVerifyResult("Right(\"abc\", Float(1))", FormulaType.String, "Right(#$string$#, Float(#$decimal$#))");

            // FCB Floating Point  disabled
            CallEngineAndVerifyResult("Left(\"abc\", 1)", FormulaType.String, "Left(#$string$#, #$decimal$#)", isFloatingPointEnabled: false);
            CallEngineAndVerifyResult("Left(\"abc\", Float(1))", null, "Left(#$string$#, Float(#$decimal$#))", isFloatingPointEnabled: false, isSuccess: false, errorMsg: "'Float' is an unknown or unsupported function.");
            CallEngineAndVerifyResult("Right(\"abc\", 1)", FormulaType.String, "Right(#$string$#, #$decimal$#)", isFloatingPointEnabled: false);
            CallEngineAndVerifyResult("Right(\"abc\", Float(1))", null, "Right(#$string$#, Float(#$decimal$#))", isFloatingPointEnabled: false, isSuccess: false, errorMsg: "'Float' is an unknown or unsupported function.");
        }

        [Fact]
        public void CheckSubstituteAndReplaceOverloadedFunction()
        {
            CallEngineAndVerifyResult("Replace(\"abc\", 1, 2, \"ae\")", FormulaType.String, "Replace(#$string$#, #$decimal$#, #$decimal$#, #$string$#)");
            CallEngineAndVerifyResult("Replace(\"abc\", Float(1), 2, \"ae\")", FormulaType.String, "Replace(#$string$#, Float(#$decimal$#), #$decimal$#, #$string$#)");
            CallEngineAndVerifyResult("Substitute(\"abc\", \"a\", \"e\", 1)", FormulaType.String, "Substitute(#$string$#, #$string$#, #$string$#, #$decimal$#)");
            CallEngineAndVerifyResult("Substitute(\"abc\", \"a\", \"e\", Float(1))", FormulaType.String, "Substitute(#$string$#, #$string$#, #$string$#, Float(#$decimal$#))");

            // FCB Floating Point disabled
            CallEngineAndVerifyResult("Replace(\"abc\", 1, 2, \"ae\")", FormulaType.String, "Replace(#$string$#, #$decimal$#, #$decimal$#, #$string$#)", isFloatingPointEnabled: false);
            CallEngineAndVerifyResult("Replace(\"abc\", Float(1), 2, \"ae\")", null, "Replace(#$string$#, Float(#$decimal$#), #$decimal$#, #$string$#)", isFloatingPointEnabled: false, isSuccess: false, errorMsg: "'Float' is an unknown or unsupported function.");
            CallEngineAndVerifyResult("Substitute(\"abc\", \"a\", \"e\", 1)", FormulaType.String, "Substitute(#$string$#, #$string$#, #$string$#, #$decimal$#)", isFloatingPointEnabled: false);
            CallEngineAndVerifyResult("Substitute(\"abc\", \"a\", \"e\", Float(1))", null, "Substitute(#$string$#, #$string$#, #$string$#, Float(#$decimal$#))", isFloatingPointEnabled: false, isSuccess: false, errorMsg: "'Float' is an unknown or unsupported function.");
        }

        [Fact]
        public void CheckOverloadedAbsFunction()
        {
            CallEngineAndVerifyResult("Abs(-132)", FormulaType.Decimal, "Abs(-#$decimal$#)"); // producing decimal
            CallEngineAndVerifyResult("Abs(Float(-132.133))", FormulaType.Number, "Abs(Float(-#$decimal$#))"); // producing float  

            // FCB Floating Point  disabled
            CallEngineAndVerifyResult("Abs(Float(-132.133))", null, "Abs(Float(-#$decimal$#))", isFloatingPointEnabled:false, isSuccess: false, errorMsg: "'Float' is an unknown or unsupported function."); 
        }

        [Fact]
        public void CheckOverloadedMaxFunction()
        {
            CallEngineAndVerifyResult("Max(1,2,3,4)", FormulaType.Decimal, "Max(#$decimal$#, #$decimal$#, #$decimal$#, #$decimal$#)"); // producing decimal
            CallEngineAndVerifyResult("Max(Float(1),2,3,4)", FormulaType.Number, "Max(Float(#$decimal$#), #$decimal$#, #$decimal$#, #$decimal$#)"); // producing float
            CallEngineAndVerifyResult("Max(1, Float(2),3,4)", FormulaType.Decimal, "Max(#$decimal$#, Float(#$decimal$#), #$decimal$#, #$decimal$#)"); // producing decimal

            // FCB Floating Point  disabled
            CallEngineAndVerifyResult("Max(Float(1),2,3,4)", null, "Max(Float(#$decimal$#), #$decimal$#, #$decimal$#, #$decimal$#)", isFloatingPointEnabled: false, isSuccess: false, errorMsg: "'Float' is an unknown or unsupported function."); 
            CallEngineAndVerifyResult("Max(1, Float(2),3,4)", null, "Max(#$decimal$#, Float(#$decimal$#), #$decimal$#, #$decimal$#)", isFloatingPointEnabled: false, isSuccess: false, errorMsg: "'Float' is an unknown or unsupported function."); 
        }


        private void CallEngineAndVerifyResult(string expr, FormulaType returnType, string sanitizedFormula, bool isFloatingPointEnabled = true,
            bool isSuccess = true, string errorMsg = null)
        {
            var engine = new PowerFx2SqlEngine(dataverseFeatures: new DataverseFeatures() { IsFloatingPointEnabled = isFloatingPointEnabled });
            var result = engine.Compile(expr, new SqlCompileOptions());

            Assert.NotNull(result);
            Assert.Equal(isSuccess, result.IsSuccess);
            Assert.Equal(sanitizedFormula, result.SanitizedFormula);

            if (result.IsSuccess)
            {
                Assert.Empty(result.Errors);
                Assert.Equal(returnType, result.ReturnType);
            }
            else
            {
                Assert.NotEmpty(result.Errors);
                var errors = result.Errors.ToArray();
                Assert.Equal(errorMsg, errors[0].Message);
            }
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
    DECLARE @v3 decimal(23,10)
    DECLARE @v5 decimal(23,10)
    SELECT TOP(1) @v1 = [new_Calc_Schema] FROM [dbo].[AccountBase] WHERE[AccountId] = @v2
    SELECT TOP(1) @v4 = [address1_latitude] FROM [dbo].[Account] WHERE[AccountId] = @v2

    -- expression body
    SET @v3 = TRY_CAST((ISNULL(@v0,0) + ISNULL(@v1,0)) AS decimal(23,10))
    IF(@v3 IS NULL) BEGIN RETURN NULL END
    IF(@v3<-100000000000 OR @v3>100000000000) BEGIN RETURN NULL END
    SET @v5 = TRY_CAST((ISNULL(@v3,0) + ISNULL(@v4,0)) AS decimal(23,10))
    IF(@v5 IS NULL) BEGIN RETURN NULL END
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

            Assert.Equal("accountid,address1_latitude,new_Calc,new_CurrencyPrice", ToStableString(result.TopLevelIdentifiers));

            Assert.Equal(BaselineFunction, result.SqlFunction);

            Assert.Equal(BaselineCreateRow, result.SqlCreateRow);
            Assert.Equal(BaselineLogicalFormula, result.LogicalFormula);
        }

        [Fact]
        public void CheckCompileAllAttributeTypes()
        {
            var expr = "field * Int - Decimal(Money) + If(Boolean || Picklist = 'Picklist (All Attributes)'.One, Value(String), 2)";

            var metadata = MockModels.AllAttributeModel.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);
            var result = engine.Compile(expr, new SqlCompileOptions());

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.Equal("new_field * int - Decimal(money) + If(boolean || picklist = allattributes_picklist_optionSet.'1', Value(string), 2)", result.LogicalFormula);
        }

        [Fact]
        public void CheckMoney()
        {
            var expr = "Decimal(Money)"; // resolve to Money filed

            var metadata = MockModels.AllAttributeModel.ToXrm();

            var metadataProvider = new CdsEntityMetadataProvider(null)
            {
                NumberIsFloat = DataverseEngine.NumberIsFloat
            };

            var engine = new PowerFx2SqlEngine(metadata, metadataProvider);
            var result = engine.Check(expr);

            Assert.NotNull(result);

            Assert.True(result.IsSuccess);
            Assert.Equal("Decimal(money)", result.ApplyGetInvariant());
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
        public void InvariantFormulaTest()
        {
            var xrmModel = MockModels.AllAttributeModel.ToXrm();
            var provider = new MockXrmMetadataProvider(MockModels.AllAttributeModels);
            var expr = "Concatenate(\"" + (new string('a', 977)) + "\", new_test)";
            Assert.True(expr.Length > DataverseEngine.MaxExpressionLength);
            Assert.True(expr.Length < DataverseEngine.MaxInvariantExpressionLength);

            var engine = new PowerFx2SqlEngine(xrmModel, new CdsEntityMetadataProvider(provider));
            var result = engine.Compile(expr, new SqlCompileOptions());
            Assert.False(result.IsSuccess);
            Assert.Contains("Expression can't be more than 1000 characters", result.Errors.First().ToString());

            engine = new PowerFx2SqlEngine(xrmModel, new CdsEntityMetadataProvider(provider), dataverseFeatures: new DataverseFeatures() { UseMaxInvariantExpressionLength = true });
            result = engine.Compile(expr, new SqlCompileOptions());
            Assert.True(result.IsSuccess);

            var displayExp = "Concatenate(\"" + (new string('a', 977)) + "\", Test)";
            Assert.True(displayExp.Length < DataverseEngine.MaxExpressionLength);

            result = engine.Compile(displayExp, new SqlCompileOptions());
            Assert.True(result.IsSuccess);

            var invariantExp = result.LogicalFormula;
            Assert.True(invariantExp.Length > DataverseEngine.MaxExpressionLength);

            result = engine.Compile(invariantExp, new SqlCompileOptions());
            Assert.True(result.IsSuccess);
        }

        [Theory]
        [InlineData("Value(currency)", true)]
        [InlineData("Decimal(currency)", true)]
        [InlineData("Int(currency)", false, "Error 4-12: Direct use of currency fields is not yet supported. Use Decimal(CurrencyField) as a workaround but note that decimal has a smaller range than currency.")] 
        [InlineData("Decimal(currency + 0)", false, "Error 8-16: Direct use of currency fields is not yet supported. Use Decimal(CurrencyField) as a workaround but note that decimal has a smaller range than currency.")]
        [InlineData("currency + 0", false, "Error 0-8: Direct use of currency fields is not yet supported. Use Decimal(CurrencyField) as a workaround but note that decimal has a smaller range than currency.")]
        [InlineData("currency > 0", false, "Error 0-8: Direct use of currency fields is not yet supported. Use Decimal(CurrencyField) as a workaround but note that decimal has a smaller range than currency.")]
        [InlineData("Decimal(currency) - currency", false, "Error 20-28: Direct use of currency fields is not yet supported. Use Decimal(CurrencyField) as a workaround but note that decimal has a smaller range than currency.")]
        public void VerifyCurrencyUsage(string expr, bool success, string message = null)
        {
            var model = new EntityMetadataModel
            {
                Attributes = new AttributeMetadataModel[]
                {
                    new AttributeMetadataModel
                     {
                         LogicalName= "new_currency",
                         DisplayName = "currency",
                         AttributeType = AttributeTypeCode.Money
                     },
                }
            };

            var metadata = model.ToXrm();
            var engine = new PowerFx2SqlEngine(metadata);
            var result = engine.Check(expr);

            Assert.NotNull(result);
            Assert.Equal(success, result.IsSuccess);
            if (!success)
            {
                Assert.NotNull(result.Errors);
                Assert.Single(result.Errors);
                Assert.Equal(message, result.Errors.First().ToString());
                Assert.Equal(SqlCompileException.DirectCurrencyNotSupported.Key, result.Errors.First().MessageKey);
            }
        }

        public const string IntegerFunction = @"CREATE FUNCTION fn_testUdf1(
    @v0 decimal(23,10) -- new_field
) RETURNS int
  WITH SCHEMABINDING
AS BEGIN
    DECLARE @v1 decimal(23,10)
    DECLARE @v2 decimal(23,10)

    -- expression body
    SET @v1 = 2.0
    SET @v2 = TRY_CAST((ISNULL(@v0,0) * ISNULL(@v1,0)) AS decimal(23,10))
    IF(@v2 IS NULL) BEGIN RETURN NULL END
    -- end expression body

    IF(@v2<-2147483648 OR @v2>2147483647) BEGIN RETURN NULL END
    RETURN ROUND(@v2, 0)
END
";

        [Fact]
        public void CompileIntegerTypeHint()
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

            // SQL Compiler will always produce Number/Decimal for Numeric types. There is no concept of whole no here, Although
            // the UDF will return integer in this case but Sql Compile Result would be Decimal only
            var options = new SqlCompileOptions 
            {
                TypeHints = new SqlCompileOptions.TypeDetails { TypeHint = AttributeTypeCode.Integer  },
                UdfName = "fn_testUdf1"
            };
            
            var result = engine.Compile(expr, options);

            Assert.NotNull(result);
            Assert.NotNull(result.SqlFunction);
            Assert.NotNull(result.SqlCreateRow);
            Assert.Empty(result.Errors);

            Assert.True(result.IsHintApplied);

            Assert.Equal(IntegerFunction, result.SqlFunction);
            Assert.True(result.ReturnType is DecimalType);
        }

        public const string WholeNumUDFForTimeZoneFormat = @"CREATE FUNCTION fn_testUdf1(
    @v0 decimal(23,10) -- new_field
) RETURNS int
  WITH SCHEMABINDING
AS BEGIN
    DECLARE @v1 decimal(23,10)
    DECLARE @v2 decimal(23,10)

    -- expression body
    SET @v1 = 100
    SET @v2 = TRY_CAST((ISNULL(@v0,0) * ISNULL(@v1,0)) AS decimal(23,10))
    IF(@v2 IS NULL) BEGIN RETURN NULL END
    -- end expression body

    IF(@v2<-1500 OR @v2>1500) BEGIN RETURN NULL END
    RETURN ROUND(@v2, 0)
END
";
        public const string WholeNumUDFForLanguageFormat = @"CREATE FUNCTION fn_testUdf1(
    @v0 decimal(23,10), -- new_field
    @v1 uniqueidentifier -- new_lookup
) RETURNS int
AS BEGIN
    DECLARE @v2 decimal(23,10)
    DECLARE @v3 decimal(23,10)
    DECLARE @v4 decimal(23,10)
    DECLARE @v5 decimal(23,10)
    SELECT TOP(1) @v2 = [data3] FROM [dbo].[tripleremoteBase] WHERE[tripleremoteid] = @v1

    -- expression body
    SET @v3 = TRY_CAST((ISNULL(@v0,0) * ISNULL(@v2,0)) AS decimal(23,10))
    IF(@v3 IS NULL) BEGIN RETURN NULL END
    IF(@v3<-100000000000 OR @v3>100000000000) BEGIN RETURN NULL END
    SET @v4 = 100
    SET @v5 = TRY_CAST((ISNULL(@v3,0) * ISNULL(@v4,0)) AS decimal(23,10))
    IF(@v5 IS NULL) BEGIN RETURN NULL END
    -- end expression body

    IF(@v5<0 OR @v5>2147483647) BEGIN RETURN NULL END
    RETURN ROUND(@v5, 0)
END
";
        [Fact]
        public void CompileIntegerTypeHintMinMaxRangeTest()
        {
            var xrmModel = MockModels.AllAttributeModel.ToXrm();
            var provider = new CdsEntityMetadataProvider(new MockXrmMetadataProvider(MockModels.AllAttributeModels));
            var engine = new PowerFx2SqlEngine(xrmModel, provider);
            var options = new SqlCompileOptions
            {
                TypeHints = new SqlCompileOptions.TypeDetails { 
                    TypeHint = AttributeTypeCode.Integer,
                    IntegerFormatValue = IntegerFormat.TimeZone
                },
                UdfName = "fn_testUdf1",
            };

            var result = engine.Compile("field * 100", options);

            Assert.True(result.IsSuccess);
            Assert.Equal(WholeNumUDFForTimeZoneFormat, result.SqlFunction);

            options.TypeHints.IntegerFormatValue = IntegerFormat.Language;

            result = engine.Compile("field * lookup.data3 * 100", options);

            Assert.True(result.IsSuccess);
            Assert.Equal(WholeNumUDFForLanguageFormat, result.SqlFunction);
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
        [InlineData("MultiSelect", "Error 0-11: Columns of type Multi-Select Option Set are not supported in formula columns.")] // "Multi Select Picklist"
        public void CompileInvalidTypes(string expr, string error)
        {
            var provider = new MockXrmMetadataProvider(MockModels.AllAttributeModels);
            var engine = new PowerFx2SqlEngine(MockModels.AllAttributeModels[0].ToXrm(), new CdsEntityMetadataProvider(provider) { NumberIsFloat = DataverseEngine.NumberIsFloat });

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
        [InlineData("Decimal(Money)", typeof(DecimalType))] // "Money returns Decimal"
        [InlineData("Value(Money)", typeof(DecimalType))] // "Money returns Decimal"
        [InlineData("Int", typeof(DecimalType))] // "Int returns Decimal"
        [InlineData("String", typeof(StringType))] // "String"
        [InlineData("\"foo\"", typeof(StringType))] // "String literal returns String"
        [InlineData("Boolean", typeof(BooleanType))] // "Boolean"
        [InlineData("true", typeof(BooleanType))] // "Boolean literal returns Boolean"
        [InlineData("Mod(int, int)", typeof(DecimalType))] // "Int from function returns decimal"
        public void CompileValidReturnType(string expr, Type returnType)
        {
            var provider = new MockXrmMetadataProvider(MockModels.AllAttributeModels);
            var engine = new PowerFx2SqlEngine(MockModels.AllAttributeModels[0].ToXrm(), new CdsEntityMetadataProvider(provider) { NumberIsFloat = DataverseEngine.NumberIsFloat });

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

        // Verify that MockModels.AllAttributeModel has an attribute of each type
        [Fact]
        public void VerifyAllAttributes()
        {
            var set = new HashSet<AttributeTypeCode>();
            foreach (AttributeTypeCode val in Enum.GetValues(typeof(AttributeTypeCode)))
            {
                set.Add(val);
            }

            foreach (var attr in MockModels.AllAttributeModel.Attributes)
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
            var code = attr.AttributeType.Value;

            // IsBlank should accept any type, and always return a boolean.
            expressions.Add(code == AttributeTypeCode.Money ? $"IsBlank(Decimal({attr.LogicalName}))": $"IsBlank({attr.LogicalName})");

            switch (code)
            {
                // Should be able to use arithmetic operator on any numerical type
                case AttributeTypeCode.BigInt:
                case AttributeTypeCode.Decimal:
                case AttributeTypeCode.Double:
                case AttributeTypeCode.Integer:
                    expressions.Add($"{attr.LogicalName} > 0");
                    break;

                case AttributeTypeCode.Money:
                    expressions.Add($"Decimal({attr.LogicalName}) > 0");
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
                expressions.Add(code == AttributeTypeCode.Money ? $"Decimal({attr.LogicalName}) = Decimal('{attr.DisplayName}')" : $"{attr.LogicalName} = '{attr.DisplayName}'");
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
        // Explicitly iterate over every attribute in MockModels.AllAttributeModels (to ensure we're being comprehensive.
        // And if we can't consume it, verify the error. 
        [Fact]
        public void VerifyProduceAndConsumeAllTypes()
        {
            // mapping of field's logicalName --> fragment of Error received when trying to consume the type. 
            var unsupportedConsumer = new Dictionary<string, string>
            {
                { "multiSelect", "Columns of type Multi-Select Option Set are not supported in formula columns." },
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

            var unsupportedProducer = new Dictionary<string, string>
            {
                { "guid", "The result type Guid is not supported in formula columns." },
                { "allid", "The result type Guid is not supported in formula columns." },
                { "ownerid", "The result type Record is not supported in formula columns." },
                { "multiSelect", "Columns of type Multi-Select Option Set are not supported in formula columns." }
            };

            var provider = new MockXrmMetadataProvider(MockModels.AllAttributeModels);
            var engine = new PowerFx2SqlEngine(MockModels.AllAttributeModels[0].ToXrm(), new CdsEntityMetadataProvider(provider) { NumberIsFloat = DataverseEngine.NumberIsFloat }
                    , dataverseFeatures: new DataverseFeatures() { IsFloatingPointEnabled = true, IsOptionSetEnabled = true });

            foreach (var attr in MockModels.AllAttributeModel.Attributes)
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
                    var expr = attr.AttributeType.Value == AttributeTypeCode.Money? $"Decimal({attr.LogicalName})" : $"{attr.LogicalName}";
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
            "localid,logicalid",
            "remote=>data",
            "local=>logical")] // "Logical Lookup"
        [InlineData("7 + 2", "")] // "Literals"
        public void CompileIdentifiers(string expr, string topLevelFields, string relatedFields = null, string relationships = null)
        {
            var provider = new MockXrmMetadataProvider(MockModels.RelationshipModels);
            var engine = new PowerFx2SqlEngine(MockModels.RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider) { NumberIsFloat = DataverseEngine.NumberIsFloat });
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
        [InlineData("Text(123, \"[$-fr-FR]#\")", false, false, "Error 10-22: Locale-specific formatting tokens such as \".\" and \",\" are not supported in formula columns.")] // "Locale token at start of format string not supported"
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

        public const string InOperatorTestUDF1 = @"CREATE FUNCTION test(
) RETURNS bit
  WITH SCHEMABINDING
AS BEGIN
    DECLARE @v0 nvarchar(4000)
    DECLARE @v1 bit

    -- expression body
    SET @v0 = N'testa_a'
    SET @v1 = IIF((ISNULL(@v0,N'') LIKE N'%a[_]a%'), 1, 0)
    -- end expression body

    RETURN @v1
END
";
        public const string InOperatorTestUDF2 = @"CREATE FUNCTION test(
) RETURNS bit
  WITH SCHEMABINDING
AS BEGIN
    DECLARE @v0 nvarchar(4000)
    DECLARE @v1 bit

    -- expression body
    SET @v0 = N'100%'
    SET @v1 = IIF((ISNULL(@v0,N'') LIKE N'%0[%]%'), 1, 0)
    -- end expression body

    RETURN @v1
END
";
        [Fact]
        public void InOperatorTests()
        {
            var engine = new PowerFx2SqlEngine();
            var result = engine.Compile("(\"a_a\" in \"testa_a\")", new SqlCompileOptions() { UdfName = "test" });
            Assert.True(result.IsSuccess);
            Assert.Equal(InOperatorTestUDF1, result.SqlFunction);

            result = engine.Compile("(\"0%\" in \"100%\")", new SqlCompileOptions() { UdfName = "test" });
            Assert.True(result.IsSuccess);
            Assert.Equal(InOperatorTestUDF2, result.SqlFunction);
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
        [InlineData("WeekNum(dateOnly, 2)", false, typeof(DecimalType), "Error 18-19: Invalid argument type (Decimal). Expecting a Enum (StartOfWeek) value instead.")]
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

            var engine = new PowerFx2SqlEngine(localModel.ToXrm(), dataverseFeatures: new DataverseFeatures() { IsFloatingPointEnabled = true });
            var result = engine.Check("'Picklist (global1)' = [@Picklist].'Eeny (1)' || 'Picklist (global2)' = [@Picklist].'Eeny (3)'");

            Assert.True(result.IsSuccess);
        }

        [Theory]
        [InlineData("Float", true)] // "Local Float"
        [InlineData("Other.Float", true)] // "Remote non-float with name collision"
        [InlineData("Other.'Actual Float'", true)] // "Remote float"
        public void CheckFloatingPoint(string expr, bool success, string error = null)
        {
            var provider = new MockXrmMetadataProvider(MockModels.RelationshipModels);
            var engine = new PowerFx2SqlEngine(MockModels.RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider) { NumberIsFloat = DataverseEngine.NumberIsFloat }
                ,dataverseFeatures: new DataverseFeatures() { IsFloatingPointEnabled = true });
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
            var provider = new MockXrmMetadataProvider(MockModels.RelationshipModels);
            var engine = new PowerFx2SqlEngine(MockModels.RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider) { NumberIsFloat = DataverseEngine.NumberIsFloat });
            AssertReturnTypeOrError(engine, expr, false, null, errors);
        }

        [Fact]
        public void CompileLogicalLookup()
        {
            var provider = new MockXrmMetadataProvider(MockModels.RelationshipModels);
            var engine = new PowerFx2SqlEngine(MockModels.RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider) { NumberIsFloat = DataverseEngine.NumberIsFloat });
            var options = new SqlCompileOptions { UdfName = "fn_udf_Logical" };
            var result = engine.Compile("'Logical Lookup'.Data", options);

            Assert.True(result.IsSuccess);
            // the SqlCreateRow has an embedded newline
            Assert.Equal(@"fn_udf_Logical([localid])
", result.SqlCreateRow);
        }

        [Fact]
        public void CheckGlobalOptionSets()
        {
            var xrmModel = MockModels.AllAttributeModel.ToXrm();
            var provider = new MockXrmMetadataProvider(MockModels.AllAttributeModels);

            // Global optionsets - 'global1', 'global2' are not used by any attribute of the entity, so will not be present in the metadatacache optionsets
            var engine = new PowerFx2SqlEngine(xrmModel, new CdsEntityMetadataProvider(provider));
            var result = engine.Compile("Global2", new SqlCompileOptions());
            Assert.False(result.IsSuccess);
            Assert.Contains("Name isn't valid. 'Global2' isn't recognized", result.Errors.First().ToString());

            // passing list of these global optionsets so that these option sets will also be processed and added to metadatacache optionsets
            var engine2 = new PowerFx2SqlEngine(xrmModel, new CdsEntityMetadataProvider(provider, globalOptionSets: MockModels.GlobalOptionSets),
                dataverseFeatures: new() { IsOptionSetEnabled = true });

            var result2 = engine2.Compile("Global2", new SqlCompileOptions());
            Assert.False(result2.IsSuccess);
            Assert.Contains("Not supported in formula columns.", result2.Errors.First().ToString());

            result2 = engine2.Compile("(Global2.Three = Global2.Four)", new SqlCompileOptions());
            Assert.True(result2.IsSuccess);

            result2 = engine2.Compile("If(1 > 2, Global2.Three, Global2.Four)", new SqlCompileOptions());
            Assert.True(result2.IsSuccess);
            Assert.True(result2.ReturnType is OptionSetValueType);
        }

        [Fact]
        public void OptionSetsTests()
        {
            var xrmModel = MockModels.AllAttributeModel.ToXrm();
            var provider = new CdsEntityMetadataProvider(new MockXrmMetadataProvider(MockModels.AllAttributeModels), globalOptionSets: MockModels.GlobalOptionSets);
            var engine = new PowerFx2SqlEngine(xrmModel, provider);
            provider.TryGetOptionSet(new Core.Utils.DName("Picklist (All Attributes)"), out var optionSet);
            provider.TryGetOptionSet(new Core.Utils.DName("Global2"), out var globalOptionSet);

            // OptionSet returntype is not supported with FCB - "IsOptionSetEnabled" not enabled.
            var result = engine.Compile("If(1>2,'Picklist (All Attributes)'.One, 'Picklist (All Attributes)'.Two)", new SqlCompileOptions());
            Assert.False(result.IsSuccess);
            Assert.Equal("Error 0-72: The result type OptionSetValue (allattributes_picklist_optionSet) is not supported in formula columns.", result.Errors.First().ToString());

            engine = new PowerFx2SqlEngine(xrmModel, provider, dataverseFeatures: new() { IsOptionSetEnabled = true });

            // Using related entity optionset in result value is not supported.
            result = engine.Compile("If(lookup.data3>1,'Optionset Field (Triple Remotes)'.One, 'Optionset Field (Triple Remotes)'.Two)", new SqlCompileOptions());
            Assert.False(result.IsSuccess);
            Assert.Equal("Error 0-97: The Option Set with name 'Optionset Field (Triple Remotes)' from related tables is not currently supported in formula columns.", result.Errors.First().ToString());

            result = engine.Compile("If('Picklist (All Attributes)'.One = 'Picklist (All Attributes)'.Two,'Picklist (All Attributes)'.One, 'Picklist (All Attributes)'.Two)", new SqlCompileOptions());
            Assert.True(result.IsSuccess);
            Assert.Single(result.TopLevelIdentifiers);
            Assert.Equal("picklist", result.TopLevelIdentifiers.ElementAt(0));
            Assert.NotEqual(Guid.Empty, result.OptionSetId);
            Assert.Equal(optionSet.OptionSetId, result.OptionSetId);

            result = engine.Compile("picklist", new SqlCompileOptions());
            Assert.True(result.IsSuccess);
            Assert.Single(result.TopLevelIdentifiers);
            Assert.Equal("picklist", result.TopLevelIdentifiers.ElementAt(0));
            Assert.NotEqual(Guid.Empty, result.OptionSetId);
            Assert.Equal(optionSet.OptionSetId, result.OptionSetId);

            // Dependent optionsets used in formula, other than in the result values are returned in SqlCompileResult.DependentOptionSetIds.
            result = engine.Compile("If('Picklist (All Attributes)'.One = 'Picklist (All Attributes)'.Two, 1, Global2.Three = Global2.Four, 2, 3)", new SqlCompileOptions());
            Assert.True(result.IsSuccess);
            Assert.Single(result.TopLevelIdentifiers);
            Assert.Equal("picklist", ToStableString(result.TopLevelIdentifiers));
            Assert.Single(result.DependentGlobalOptionSetIds);
            Assert.NotEqual(Guid.Empty, globalOptionSet.OptionSetId);
            Assert.Contains(globalOptionSet.OptionSetId, result.DependentGlobalOptionSetIds);
            Assert.Equal(Guid.Empty, result.OptionSetId);
        }

        [Theory]
        [InlineData("Text('Picklist (All Attributes)'.One)", true, false, "Error 32-36: This argument cannot be passed as type OptionSetValueType in formula columns.")]
        [InlineData("Text('Picklist (All Attributes)'.One)", false, true)]
        [InlineData("Text(picklist)", true, false, "Error 5-13: This argument cannot be passed as type OptionSetValueType in formula columns.")]
        [InlineData("Text(picklist)", false, false, "Error 5-13: This argument cannot be passed as type OptionSetValueType in formula columns.")]
        [InlineData("Text(TimeUnit.Days)", true, true)]
        [InlineData("Text(TimeUnit.Days)", false, true)]
        public void OptionSetToTextOperationTest(string expr, bool isOptionSetEnabled, bool success, string error = null)
        {
            var xrmModel = MockModels.AllAttributeModel.ToXrm();
            var provider = new CdsEntityMetadataProvider(new MockXrmMetadataProvider(MockModels.AllAttributeModels));
            var engine = new PowerFx2SqlEngine(xrmModel, provider, dataverseFeatures: new() { IsOptionSetEnabled = isOptionSetEnabled });
            var result = engine.Compile(expr, new SqlCompileOptions());

            Assert.Equal(success, result.IsSuccess);
            if (!success)
            {
                Assert.NotNull(result.Errors);
                Assert.Single(result.Errors);
                Assert.Equal(error, result.Errors.First().ToString());
            }
        }

        [Theory]
        [InlineData("If(lookup.data3>1,'Picklist (All Attributes)'.One, 'Optionset Field (Triple Remotes)'.One)", false, "Error 85-89: The operation cannot be performed on multiple Option Sets. Please use single Option Set as result type.")]
        [InlineData("If(2>1,'Picklist (All Attributes)'.One, Global2.Three)", false, "Error 47-53: The operation cannot be performed on multiple Option Sets. Please use single Option Set as result type.")]
        [InlineData("Switch(1,1,picklist,2,Global2.Four)", false, "Error 29-34: The operation cannot be performed on multiple Option Sets. Please use single Option Set as result type.")]
        [InlineData("IfError('Picklist (All Attributes)'.One, Global2.Four)", false, "Error 35-39: The operation cannot be performed on multiple Option Sets. Please use single Option Set as result type.")]
        [InlineData("If(3>1,'Picklist (All Attributes)'.One, picklist)", true)]
        public void MultipleOptionSetsUsedInFormula(string expr, bool success, string error = null)
        {
            var xrmModel = MockModels.AllAttributeModel.ToXrm();
            var provider = new CdsEntityMetadataProvider(new MockXrmMetadataProvider(MockModels.AllAttributeModels), globalOptionSets: MockModels.GlobalOptionSets);
            var engine = new PowerFx2SqlEngine(xrmModel, provider, dataverseFeatures: new() { IsOptionSetEnabled = true });
            var result = engine.Compile(expr, new SqlCompileOptions());

            Assert.Equal(success, result.IsSuccess);
            if (!success)
            {
                Assert.NotNull(result.Errors);
                Assert.Single(result.Errors);
                Assert.Equal(error, result.Errors.First().ToString());
            }
        }

        [Theory]
        [InlineData("If(boolean, 1, 2)", true)]
        [InlineData("If('Boolean (All Attributes)'.Yes, 1, 2)", true)]
        [InlineData("If(picklist, 1, 2)", false, "Invalid argument type (OptionSetValue (allattributes_picklist_optionSet)). Expecting a Boolean value instead.")] // non-boolean backed optionset
        [InlineData("If('Picklist (All Attributes)'.One, 1, 2)", false, "Invalid argument type (OptionSetValue (allattributes_picklist_optionSet)). Expecting a Boolean value instead.")] // non-boolean backed optionset
        public void BooleanOptionSetTests(string expr, bool success, string error = null)
        {
            var xrmModel = MockModels.AllAttributeModel.ToXrm();
            var provider = new CdsEntityMetadataProvider(new MockXrmMetadataProvider(MockModels.AllAttributeModels));
            var engine = new PowerFx2SqlEngine(xrmModel, provider, dataverseFeatures: new() { IsOptionSetEnabled = true });
            var result = engine.Compile(expr, new SqlCompileOptions());

            Assert.Equal(success, result.IsSuccess);
            if (!success)
            {
                Assert.NotNull(result.Errors);
                Assert.Contains(result.Errors, err => err.Message.Contains(error));
            }
        }

        [Theory]
        [InlineData("Value('Picklist (All Attributes)'.One)", true)]
        [InlineData("Value(picklist)", true)]
        [InlineData("Value(TimeUnit.Days)", false, "The function 'Value' has some invalid arguments")]
        public void OptionSetWithValueFnTests(string expr, bool success, string error = null)
        {
            var xrmModel = MockModels.AllAttributeModel.ToXrm();
            var provider = new CdsEntityMetadataProvider(new MockXrmMetadataProvider(MockModels.AllAttributeModels), globalOptionSets: MockModels.GlobalOptionSets);
            var engine = new PowerFx2SqlEngine(xrmModel, provider, dataverseFeatures: new() { IsOptionSetEnabled = true });
            var result = engine.Compile(expr, new SqlCompileOptions() { UdfName = "test" });

            Assert.Equal(success, result.IsSuccess);
            if (success)
            {
                Assert.NotNull(result.SqlFunction);
            }
            else
            {
                Assert.NotNull(result.Errors);
                Assert.Contains(result.Errors, err => err.Message.Contains(error));
            }
        }

        public const string OptionSetTestUDF = @"CREATE FUNCTION test(
) RETURNS int
  WITH SCHEMABINDING
AS BEGIN
    DECLARE @v0 int
    DECLARE @v1 decimal(23,10)
    DECLARE @v2 decimal(23,10)
    DECLARE @v3 bit
    DECLARE @v4 int
    DECLARE @v5 int

    -- expression body
    SET @v1 = 1
    SET @v2 = 2
    SET @v3 = IIF((ISNULL(@v1,0) > ISNULL(@v2,0)), 1, 0)
    IF ((ISNULL(@v3,0)=1)) BEGIN
        SET @v4 = 1
        SET @v0 = @v4
    END ELSE BEGIN
        SET @v5 = 2
        SET @v0 = @v5
    END
    -- end expression body

    RETURN @v0
END
";
        [Fact]
        public void OptionSetUDFTest()
        {
            var xrmModel = MockModels.AllAttributeModel.ToXrm();
            var provider = new CdsEntityMetadataProvider(new MockXrmMetadataProvider(MockModels.AllAttributeModels));
            var engine = new PowerFx2SqlEngine(xrmModel, provider, dataverseFeatures: new() { IsOptionSetEnabled = true });

            var result = engine.Compile("If(1 > 2,'Picklist (All Attributes)'.One, 'Picklist (All Attributes)'.Two)", new SqlCompileOptions() { UdfName = "test" });
            Assert.True(result.IsSuccess);
            Assert.Single(result.TopLevelIdentifiers);
            Assert.Equal(OptionSetTestUDF, result.SqlFunction);
        }

        [Fact]
        public void BlankFnUsedInControlFnsTest()
        {
            var engine = new PowerFx2SqlEngine(dataverseFeatures: new() { IsOptionSetEnabled = true });
            var result = engine.Compile("If(1>2,1,Blank())", new SqlCompileOptions());
            Assert.True(result.IsSuccess);

            result = engine.Compile("Switch(1,1,\"abc\", Blank())", new SqlCompileOptions());
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void CheckRelatedEntityCurrencyUsedInFormula()
        {
            var xrmModel = MockModels.AllAttributeModel.ToXrm();
            var provider = new MockXrmMetadataProvider(MockModels.AllAttributeModels);
            var engine = new PowerFx2SqlEngine(xrmModel, new CdsEntityMetadataProvider(provider) { NumberIsFloat = DataverseEngine.NumberIsFloat });
            var result = engine.Compile("Decimal(money) + lookup.data3 + Decimal(lookup.currencyField) + 11", new SqlCompileOptions());
            Assert.False(result.IsSuccess);
            Assert.Equal("Error 46-60: Calculations with currency columns in related tables are not currently supported in formula columns.", result.Errors.First().ToString());
        }

        public const string guidTestUDF = @"CREATE FUNCTION test(
    @v0 uniqueidentifier -- guid
) RETURNS nvarchar(4000)
  WITH SCHEMABINDING
AS BEGIN
    DECLARE @v1 nvarchar(4000)
    DECLARE @v2 nvarchar(4000)
    DECLARE @v3 nvarchar(4000)
    DECLARE @v4 nvarchar(4000)
    DECLARE @v5 nvarchar(4000)

    -- expression body
    SET @v1 = @v0
    SET @v2 = ISNULL(@v1,N'')
    SET @v3 = N'abc'
    SET @v4 = ISNULL(@v3,N'')
    SET @v5 = CONCAT(@v2,@v4)
    -- end expression body

    RETURN @v5
END
";
        [Fact]
        public void CheckGuidUsedInFormula()
        {
            var xrmModel = MockModels.AllAttributeModel.ToXrm();
            var provider = new MockXrmMetadataProvider(MockModels.AllAttributeModels);
            var engine = new PowerFx2SqlEngine(xrmModel, new CdsEntityMetadataProvider(provider));
            var result = engine.Compile("Concatenate(guid,\"abc\")", new SqlCompileOptions() { UdfName = "test" });
            Assert.True(result.IsSuccess);
            Assert.Single(result.TopLevelIdentifiers);
            Assert.Equal("guid", result.TopLevelIdentifiers.ElementAt(0));
            Assert.Equal(guidTestUDF, result.SqlFunction);

            result = engine.Compile("Concatenate(lookup.tripleremoteid,\"abc\")", new SqlCompileOptions());
            Assert.True(result.IsSuccess);
            Assert.Equal("tripleremoteid", result.RelatedIdentifiers.ToArray()[0].Value.First());
        }

        public const string BaseTableNameTestUDF = @"CREATE FUNCTION test(
    @v0 uniqueidentifier -- new_tasklookup
) RETURNS decimal(23,10)
AS BEGIN
    DECLARE @v1 decimal(23,10)
    DECLARE @v2 uniqueidentifier
    DECLARE @v3 decimal(23,10)
    SELECT TOP(1) @v1 = [subject],@v2 = [activitypointerid] FROM [dbo].[ActivityPointerBase] WHERE[activitypointerid] = @v0

    -- expression body
    SET @v3 = @v1
    -- end expression body

    IF(@v3<-100000000000 OR @v3>100000000000) BEGIN RETURN NULL END
    RETURN ROUND(@v3, 10)
END
";
        [Fact]
        public void BaseTableNameTest()
        {
            var provider = new MockXrmMetadataProvider(MockModels.TestAllAttributeModels);
            var metadataProvider = new MockEntityAttributeMetadataProvider(provider);
            var engine = new PowerFx2SqlEngine(MockModels.Account.ToXrm(), new CdsEntityMetadataProvider(provider), entityAttributeMetadataProvider: new EntityAttributeMetadataProvider(metadataProvider)); 
            var result = engine.Compile("tasklookup.subject", new SqlCompileOptions() { UdfName = "test" });
            Assert.True(result.IsSuccess);
            Assert.Equal(BaseTableNameTestUDF, result.SqlFunction);
        }

        public const string InheritsFromTestUDF = @"CREATE FUNCTION test(
    @v1 uniqueidentifier -- activitypointerid
) RETURNS decimal(23,10)
AS BEGIN
    DECLARE @v0 decimal(23,10)
    DECLARE @v2 decimal(23,10)
    DECLARE @v3 decimal(23,10)
    SELECT TOP(1) @v0 = [subject] FROM [dbo].[ActivityPointerBase] WHERE[activitypointerid] = @v1

    -- expression body
    SET @v2 = 1
    SET @v3 = TRY_CAST((ISNULL(@v0,0) + ISNULL(@v2,0)) AS decimal(23,10))
    IF(@v3 IS NULL) BEGIN RETURN NULL END
    -- end expression body

    IF(@v3<-100000000000 OR @v3>100000000000) BEGIN RETURN NULL END
    RETURN ROUND(@v3, 10)
END
";
        [Fact]
        public void InheritsFromTest()
        {
            var provider = new MockXrmMetadataProvider(MockModels.Task);
            var metadataProvider = new MockEntityAttributeMetadataProvider(provider);
            var engine = new PowerFx2SqlEngine(MockModels.Task.ToXrm(), new CdsEntityMetadataProvider(provider), entityAttributeMetadataProvider: new EntityAttributeMetadataProvider(metadataProvider));

            // Current inherited entity's simple field that is stored on primary table is not passed as parameter to UDF.
            var result = engine.Compile("subject + 1", new SqlCompileOptions() { UdfName = "test" });
            Assert.True(result.IsSuccess);
            Assert.Equal(InheritsFromTestUDF, result.SqlFunction);
        }

        public const string InheritedEntityFieldNotStoredOnPrimaryTableUDF = @"CREATE FUNCTION test(
    @v0 decimal(23,10) -- fieldnotstoredonprimarytable
) RETURNS decimal(23,10)
  WITH SCHEMABINDING
AS BEGIN
    DECLARE @v1 decimal(23,10)
    DECLARE @v2 decimal(23,10)

    -- expression body
    SET @v1 = 1
    SET @v2 = TRY_CAST((ISNULL(@v0,0) + ISNULL(@v1,0)) AS decimal(23,10))
    IF(@v2 IS NULL) BEGIN RETURN NULL END
    -- end expression body

    IF(@v2<-100000000000 OR @v2>100000000000) BEGIN RETURN NULL END
    RETURN ROUND(@v2, 10)
END
";
        [Fact]
        public void InheritedEntityFieldNotStoredOnPrimaryTableTest()
        {
            var provider = new MockXrmMetadataProvider(MockModels.Task);
            var metadataProvider = new MockEntityAttributeMetadataProvider(provider);
            var engine = new PowerFx2SqlEngine(MockModels.Task.ToXrm(), new CdsEntityMetadataProvider(provider), entityAttributeMetadataProvider: new EntityAttributeMetadataProvider(metadataProvider));

            // Inherited entity's simple/rollup field that is not stored on primary table will be passed as parameter to UDF.
            var result = engine.Compile("fieldnotstoredonprimarytable + 1", new SqlCompileOptions() { UdfName = "test" });
            Assert.True(result.IsSuccess);
            Assert.Equal(InheritedEntityFieldNotStoredOnPrimaryTableUDF, result.SqlFunction);
        }

        public const string ExtensionTableTestUDF = @"CREATE FUNCTION test(
    @v0 uniqueidentifier -- new_tasklookup
) RETURNS decimal(23,10)
AS BEGIN
    DECLARE @v1 decimal(23,10)
    DECLARE @v2 decimal(23,10)
    SELECT TOP(1) @v1 = [fieldnotstoredonprimarytable] FROM [dbo].[taskExtensionTableName] WHERE[activitypointerid] = @v0

    -- expression body
    SET @v2 = @v1
    -- end expression body

    IF(@v2<-100000000000 OR @v2>100000000000) BEGIN RETURN NULL END
    RETURN ROUND(@v2, 10)
END
";
        [Fact]
        public void ExtensionTableTest()
        {
            var provider = new MockXrmMetadataProvider(MockModels.TestAllAttributeModels);
            var metadataProvider = new MockEntityAttributeMetadataProvider(provider);
            var engine = new PowerFx2SqlEngine(MockModels.Account.ToXrm(), new CdsEntityMetadataProvider(provider), entityAttributeMetadataProvider: new EntityAttributeMetadataProvider(metadataProvider));

            // Related entity field that is not stored on primary table, will be referred using extensiontablename.
            var result = engine.Compile("tasklookup.fieldnotstoredonprimarytable", new SqlCompileOptions() { UdfName = "test" });
            Assert.True(result.IsSuccess);
            Assert.Equal(ExtensionTableTestUDF, result.SqlFunction);
        }

        public const string TableColumnNameTestUDF = @"CREATE FUNCTION test(
    @v0 uniqueidentifier -- new_tasklookup
) RETURNS decimal(23,10)
AS BEGIN
    DECLARE @v1 decimal(23,10)
    DECLARE @v2 uniqueidentifier
    DECLARE @v3 decimal(23,10)
    SELECT TOP(1) @v1 = [TaskCategory],@v2 = [activitypointerid] FROM [dbo].[ActivityPointerBase] WHERE[activitypointerid] = @v0

    -- expression body
    SET @v3 = @v1
    -- end expression body

    IF(@v3<-100000000000 OR @v3>100000000000) BEGIN RETURN NULL END
    RETURN ROUND(@v3, 10)
END
";
        [Fact]
        public void TableColumnNameTest()
        {
            var provider = new MockXrmMetadataProvider(MockModels.TestAllAttributeModels);
            var metadataProvider = new MockEntityAttributeMetadataProvider(provider);
            var engine = new PowerFx2SqlEngine(MockModels.Account.ToXrm(), new CdsEntityMetadataProvider(provider), entityAttributeMetadataProvider: new EntityAttributeMetadataProvider(metadataProvider));

            // 'Category' has different TableColumnName and it is from an inherited entity. so, we use TableColumnName in UDF.
            var result = engine.Compile("tasklookup.category", new SqlCompileOptions() { UdfName = "test" });
            Assert.True(result.IsSuccess);
            Assert.Equal(TableColumnNameTestUDF, result.SqlFunction); 
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
            var provider = new MockXrmMetadataProvider(MockModels.RelationshipModels);
            var engine = new PowerFx2SqlEngine(MockModels.RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider) { NumberIsFloat = DataverseEngine.NumberIsFloat });
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
            var provider = new MockXrmMetadataProvider(MockModels.RelationshipModels);
            var engine = new PowerFx2SqlEngine(MockModels.RelationshipModels[0].ToXrm(), new CdsEntityMetadataProvider(provider));

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
            var ok = Library.TryLookup(Microsoft.PowerFx.Core.Texl.BuiltinFunctionsCore.Coalesce, out var ptr);
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

            DValue<RecordValue> result = await dataverseRecordValue.UpdateFieldsAsync(recordValue, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Null(result.Value);
            Assert.NotNull(result.Error);

            Assert.Equal($"Field {columnName} is of type ErrorValue: {errorMessage}", result.Error.Errors[0].Message);
        }

        [Theory]
        [InlineData("10000000", true, true, "")]
        [InlineData("100000000000", true, true, "")]
        [InlineData("-100000000000", true, true, "")]
        [InlineData("1000000000000000", true, false, "value is too large")]
        [InlineData("1000000000000000000000000000000000000000000000000", false, false, "value is too large")]
        [InlineData("-10000000", true, true, "")]
        [InlineData("-1000000000000000", true, false, "value is too large")]
        [InlineData("-1000000000000000000000000000000000000000000000000", false, false, "value is too large")]
        public void CheckOverflowNumericLiteral(string expr, bool pfxSuccess, bool sqlSuccess, string message = null)
        {
            var engine = new RecalcEngine();
            var sqlEngine = new PowerFx2SqlEngine();
            var pfxCheck = engine.Check(expr);

            Assert.Equal(pfxSuccess, pfxCheck.IsSuccess);

            if (!pfxCheck.IsSuccess)
            {
                Assert.True(pfxCheck.Errors.Select(err => err.Message.Contains(message)).Any());
            }

            var sqlCheck = sqlEngine.Check(expr);

            Assert.NotNull(sqlCheck);
            Assert.Equal(sqlSuccess, sqlCheck.IsSuccess);

            if (!sqlCheck.IsSuccess)
            {
                Assert.True(sqlCheck.Errors.Select(err => err.Message.Contains(message)).Any());
            }
        }

        [Fact]
        public async Task OptionSetComparisonTest()
        {
            string expr = "If(ThisRecord.Rating = 'Rating (Locals)'.Hot, true, false)";
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "Locals");
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: true, policy: policy);

            var opts = PluginExecutionTests._parserAllowSideEffects_NumberIsFloat;
            var config = new PowerFxConfig(); // Pass in per engine
            config.SymbolTable.EnableMutationFunctions();
            var engine1 = new RecalcEngine(config);
            engine1.EnableDelegation(dv.MaxRows);

            var rowscope = SymbolTableOverRecordType.NewFromRecord(dv.GetRecordType("local"), allowThisRecord: true);
            var symbols = SymbolTable.Compose(rowscope, dv.Symbols);
            var check = engine1.Check(expr, options: opts, symbolTable: symbols);
            Assert.True(check.IsSuccess);

            Assert.True(dv.MetadataCache.TryGetOptionSet(new Core.Utils.DName("Rating (Locals)"), out var optionSet));

            // Simulates IExternalOptionSetDocument.RegisterOrRefreshOptionSet
            var refreshedOptionSet = new DataverseOptionSet("rating_optionSet", "dataSet?", "local", "rating", "", "Rating", "", "", "", new Dictionary<int, string> { { 1, "Hot" }, { 2, "Warm" }, { 3, "Cold" } }, false, false);
            Assert.NotSame(refreshedOptionSet, optionSet);

            // $$$ This call will fail in DEBUG mode as 'Contracts.Assert(!_optionSets.ContainsKey(name) || optionSet.IsGlobal)' will throw
            dv.MetadataCache.RegisterOptionSet("Rating (Locals)", refreshedOptionSet);

            check = engine1.Check(expr, options: opts, symbolTable: symbols);
            Assert.True(check.IsSuccess);
        }
    }

    public class MockXrmMetadataProvider : IXrmMetadataProvider
    {
        private readonly Dictionary<string, EntityMetadata> _entitiesByName;

        public MockXrmMetadataProvider(params EntityMetadataModel[] entityModels)
        {
            _entitiesByName = entityModels.Select(model => model.ToXrm()).ToDictionary(model => model.LogicalName);
        }

        public bool TryGetEntityMetadata(string logicalOrDisplayName, out EntityMetadata entity)
        {
            var ret = _entitiesByName.TryGetValue(logicalOrDisplayName, out entity);

            if (ret && !entity.IsValid())
            {
                ret = false;
                entity = null;
            }

            return ret;
        }
    }

    public class MockEntityAttributeMetadataProvider : IEntityAttributeMetadataProvider
    {
        private readonly MockXrmMetadataProvider _xrmMetadataProvider;

        public MockEntityAttributeMetadataProvider (MockXrmMetadataProvider xrmMetadataProvider)
        {
            _xrmMetadataProvider = xrmMetadataProvider; 
        }

        public bool TryGetSecondaryEntityMetadata(string logicalName, out SecondaryEntityMetadata entity)
        {
            if (_xrmMetadataProvider.TryGetEntityMetadata(logicalName, out var xrmEntity))
            {
                entity = new SecondaryEntityMetadata()
                {
                    BaseTableName = logicalName.Equals("task") ? "ActivityPointerBase" : xrmEntity.SchemaName + "Base",
                    ExtensionTableName = xrmEntity.SchemaName + "ExtensionTableName",
                    IsInheritsFromNull = !xrmEntity.SchemaName.Equals("task")
                };
                return true;
            }

            entity = null;
            return false;
        }

        public bool TryGetSecondaryAttributeMetadata(string entityLogicalName, string attributeLogicalName, out SecondaryAttributeMetadata attribute)
        {
            attribute = new SecondaryAttributeMetadata()
            {
                IsStoredOnPrimaryTable = !attributeLogicalName.Equals("fieldnotstoredonprimarytable"),
                TableColumnName = attributeLogicalName.Equals("category") ? "TaskCategory" : attributeLogicalName
            };

            return true;
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

    internal class FakeConnectionValueContext : IConnectionValueContext
    {
        public ElasticTableAwareDVServices Services => throw new NotImplementedException();

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
