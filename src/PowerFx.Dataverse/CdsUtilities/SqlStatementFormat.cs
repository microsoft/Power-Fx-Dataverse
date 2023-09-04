using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.PowerFx.Dataverse.CdsUtilities
{
    internal sealed class SqlStatementFormat
    {
        /// <summary>
        /// Syntax of the UDF declaration
        /// </summary>
        public const string FunctionSignatureFormat = "CREATE FUNCTION {0}";
        public const string WithSchemaBindingFormat = "WITH SCHEMABINDING";
        public const string AsFormat = "AS";
        public const string ReturnsSqlTypeFormat = "RETURNS {0}";

        /// <summary>
        /// If/Else/ElseIf keyword
        /// </summary>
        public const string SqlIf = "IF";
        public const string SqlElse = "ELSE";
        public const string SqlElseIf = "ELSE IF";

        /// <summary>
        /// Always True Condition
        /// </summary>
        public const string AlwaysTrueCondition = "1=1";

        /// <summary>
        /// Syntax of the variable declaration
        /// </summary>
        public const string DeclareVariableFormat = "DECLARE {0}";

        /// <summary>
        /// Syntax of the variable declaration
        /// </summary>
        public const string ParameterDeclarationFormat = "{0} {1}";

        /// <summary>
        /// Syntax of Set variable default value
        /// </summary>
        public const string SetValueFormat = "SET {0} = {1}";

        /// <summary>
        /// Syntax of RETURN variable
        /// </summary>
        public const string ReturnVariableFormat = "RETURN {0}";

        /// <summary>
        /// Error check
        /// </summary>
        public const string ErrorCheck = "IF({0}) BEGIN RETURN NULL END";

        /// <summary>
        /// Divide By Zero condition
        /// </summary>
        public const string DivideByZeroCondition = "{0} = 0";

        /// <summary>
        /// Divide By Zero condition where null is coerced to zero
        /// </summary>
        public const string DivideByZeroCoerceCondition = "ISNULL({0}, 0) = 0";

        /// <summary>
        /// Negative number condition
        /// </summary>
        public const string NegativeNumberCondition = "{0} < 0";


        /// <summary>
        /// Non-positive number condition
        /// </summary>
        public const string NonPositiveNumberCondition = "{0} <= 0";

        /// <summary>
        /// Less than one number condition
        /// </summary>
        public const string LessThanOneNumberCondition = "{0} < 1";

        /// <summary>
        /// Overflow condition
        /// </summary>
        public const string OverflowCondition = "{0}<{1} OR {0}>{2}";

        /// <summary>
        /// Power Overflow condition - big type is decimal(38,10), which overflows after 10^28
        /// </summary>
        public const string PowerOverflowCondition = "{0} > 28";


        /// <summary>
        /// The max supported year
        /// </summary>
        public const double DateMaxYearValue = 9999;

        /// <summary>
        /// The min supported year
        /// </summary>
        public const double DateMinYearValue = 1753;

        /// <summary>
        /// Date Overflow check
        /// </summary>
        public const string DateTimeOverflowCheck = "IF({0} < 1753 OR {0} > 9999) BEGIN RETURN NULL END";

        /// <summary>
        /// Date Overflow condition
        /// </summary>
        public const string DateOverflowCondition = "{0} IS NOT NULL AND {1} IS NOT NULL AND {2} IS NOT NULL AND (ISDATE(CONCAT(CAST({0} AS int), N'-', CAST({1} AS int), N'-', CAST({2} AS int))) = 0 OR {0} < 1753 OR {0} > 9999)";

        /// <summary>
        /// Date Addition Overflow condition
        /// </summary>
        public const string DateAdditionOverflowCondition = "{0} IS NULL OR DATEDIFF({1},CONVERT(datetime,'1753-01-01 00:00:00',120),{2}) < -{0} OR DATEDIFF({1},{2},CONVERT(datetime,'9999-12-30 23:59:59',120)) < {0}";

        /// <summary>
        /// Null value check for DateDiff functions.
        /// </summary>
        public const string NullValueCheck = "IF({0} IS NULL OR {2} IS NULL) BEGIN RETURN NULL END";

        /// <summary>
        /// DateTime Overflow check for Diff In Minutes or Seconds
        /// Compare the parameters to find the min datetime parameter and Add MaxInteger(2147483647) to min datetime parameter in terms of minutes or seconds (@maxAllowedDate)
        /// Now, compare max datetime parameter(@maxInputDate) with the MaxInteger added Datetime parameter(@maxAllowedDate), 
        /// If @maxAllowedDate is less than the @maxInputDate then the scenario is overflow and return NULL.
        /// </summary>
        public const string PrepareDateTimeOverflowConditionForDateDiff = @"IF({0} < {1}) 
    BEGIN 
        SET @maxInputDate={1} 
        SET @maxAllowedDate=DATEADD({2},2147483647,{0})
    END 
ELSE 
    BEGIN 
        SET @maxInputDate={0} 
        SET @maxAllowedDate=DATEADD({2},2147483647,{1})
    END";

        public const string DateTimeOverflowConditionForDateDiff = "@maxAllowedDate<@maxInputDate OR {0} IS NULL OR {1} IS NULL";

        /// <summary>
        /// Variable to check DateTime overflow
        /// </summary>
        public const string VariableDeclarationForDateTimeOverflowChecks = "DECLARE @maxInputDate datetime,@maxAllowedDate datetime";

        /// <summary>
        /// Null condition
        /// </summary>
        public const string NullCondition = "{0} IS NULL";

        /// <summary>
        /// Set Empty string for Null
        /// </summary>
        public const string SetEmptyValueForNull = "IF({0} IS NULL) BEGIN SET {0} = N'' END";

        /// <summary>
        /// Sql data type
        /// </summary>
        public const string SqlNvarcharType = "nvarchar(4000)";
        public const string SqlIntegerType = "int";
        public const string SqlBitType = "bit";
        public const string SqlDecimalType = "decimal(23,10)";
        public const string SqlBigType = "decimal(38,10)";

        public const string SqlMoneyType = "money";
        public const string SqlDateTimeType = "datetime";
        public const string SqlUniqueIdentifierType = "uniqueidentifier";
        public const string SqlExchangeRateType = "decimal(28,12)";

        public const string SqlFloatType = "float";

        /// <summary>
        /// Declare intermediate variable
        /// </summary>
        public const string DeclareIntermediateVariableFormat = "DECLARE {0} {1}";
        public const string CastToLargeDataType = "(CAST({0} as {1}))";
        public const string BigMoneyType = "decimal(38,8)";
        public const string ExchangeRateType = "decimal(28,12)";

        /// <summary>
        /// Max/Min value for Int/Decimal/Money
        /// </summary>
        public const string IntTypeMin = "-2147483648";
        public const string IntTypeMax = "2147483647";
        public const double IntTypeMinValue = -2147483648;
        public const double IntTypeMaxValue = 2147483647;

        // Changing the min and max for decimals to match with
        // CRM supported min and max for decimals. 
        public const string DecimalTypeMin = "-100000000000";
        public const string DecimalTypeMax = "100000000000";
        public const string DecimalTypeMinForIntermediateOperations = "-9999999999999.9999999999";
        public const string DecimalTypeMaxForIntermediateOperations = "9999999999999.9999999999";
        public const double DecimalTypeMinValue = -100000000000;
        public const double DecimalTypeMaxValue = 100000000000;

        // decimal constants for comparision with decimal literal node
        public const decimal DDecimalTypeMinValue = -100000000000;
        public const decimal DDecimalTypeMaxValue = 100000000000;

        public const string MoneyTypeMin = "-922337203685477.5808";
        public const string MoneyTypeMax = "922337203685477.5807";
        public const double MoneyTypeMinValue = -922337203685477.5808;
        public const double MoneyTypeMaxValue = 922337203685477.5807;

        /// <summary>
        /// DateTime part
        /// </summary>
        public const string Second = "second";
        public const string Minute = "minute";
        public const string Hour = "hour";
        public const string Day = "day";
        public const string Week = "week";
        public const string Month = "month";
        public const string Quarter = "quarter";
        public const string Year = "year";

        /// <summary>
        /// Boundary Condition for Trims
        /// </summary>
        public const string TrimLenNullOrNegative = "IF({0} IS NULL OR {0} <= 0) BEGIN SET {1} = {2} END";
        public const string TrimLenHigher = "ELSE IF({0} >= {1}) BEGIN SET {2} = N'' END";
        public const string TrimLenOkay = "ELSE BEGIN SET {0} = {1} END";
        public const string SetLengthValue = "LEN(({0} + N'1') {1}) - 1";

        /// <summary>
        /// Round for casting decimal/money to integer
        /// </summary>
        public const string Round = "ROUND({0}, 0)";

        /// <summary>
        /// Round for casting decimal/money to correct precision.
        /// </summary>
        public const string RoundDecimal = "ROUND({0}, {1})";

        /// <summary>
        /// Cast non-string data types to nvarchar types before concat(). The default cast to nvarchar is of length 30 but in case of decimal(38,0) we would have overflow that's way we are using 80 for safe.
        /// </summary>
        public const string CastToVarchar = "(ISNULL(CAST({0} AS NVARCHAR(80)),''))";

        /// <summary>
        /// SQL Expression format for Trim Functions without using CLR
        /// </summary>
        public const string CollateString = "collate Latin1_General_100_CS_AS_KS_WS_SC";
        public const string CollateNone = "";

        /// <summary>
        /// Selecting related attribute from related entity view.
        /// {0} = Local variable in set format.        
        /// {1} = Related entity view name.
        /// {2} = Primary key attribute name of the related entity table.
        /// {3} = Lookup field of primary entity.
        /// </summary>
        public const string SelectAttributeBasedOnSingleWhere = "SELECT TOP(1) {0} FROM [dbo].[{1}] WHERE [{2}] = {3}";

        /// <summary>
        /// Set inside select
        /// {0} = sqlVariable i.e. @v0
        /// {1} = Field Name i.e. [Name]
        /// </summary>
        public const string SetInsideSelect = "{0} = [{1}] ";
    }
}
