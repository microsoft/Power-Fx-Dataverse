using Microsoft.PowerFx.Core.Errors;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Dataverse.Localization;
using Microsoft.PowerFx.Syntax;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Thrown by <see cref="SqlVisitor"/> for unsupported scenarios. 
    /// </summary>
    internal class SqlCompileException: Exception
    {
        internal class SqlError: BaseError
        {
            internal SqlError(ErrorResourceKey key, Span context, DocumentErrorSeverity severity = DocumentErrorSeverity.Critical, params object[] args)
                : base(null, null, DocumentErrorKind.AXL, severity, key, context, null, args)
            {                
            }

            public override Span TextSpan => OverrideSpan ?? base.TextSpan;

            internal Span OverrideSpan { get; set; }

            internal SqlError EnsureErrorContext(Span defaultSpan)
            {
                if (TextSpan == default)
                {
                    OverrideSpan = defaultSpan;
                }

                return this;
            }
        }

        private SqlError _error;

        /// <summary>
        /// The error resource key for a generic not supported error
        /// </summary>
        internal readonly static ErrorResourceKey NotSupported = DataverseStringResources.New("FormulaColumns_NotSupported");

        /// <summary>
        /// The error resource key for an argument not supported error
        /// </summary>
        internal readonly static ErrorResourceKey ArgumentNotSupported = DataverseStringResources.New("FormulaColumns_ArgNotSupported");

        /// <summary>
        /// The error resource key for an argument type not supported error
        /// </summary>
        internal readonly static ErrorResourceKey ArgumentTypeNotSupported = DataverseStringResources.New("FormulaColumns_ArgumentTypeNotSupported");

        /// <summary>
        /// The error resource key for an argument requiring a literal value
        /// </summary>
        internal readonly static ErrorResourceKey LiteralArgRequired = DataverseStringResources.New("FormulaColumns_LiteralArgRequired");

        /// <summary>
        /// The error resource key for an operation requiring time zone conversion
        /// </summary>);
        internal readonly static ErrorResourceKey TimeZoneConversion = DataverseStringResources.New("FormulaColumns_TimeZoneConversion");

        /// <summary>
        /// The error resource key for an operation with incompatible date time
        /// </summary>
        internal readonly static ErrorResourceKey IncompatibleDateTimes = DataverseStringResources.New("FormulaColumns_IncompatibleDateTimes");

        /// <summary>
        /// The error resource key for an operation with an incorrect unit of time supplied to DateAdd/DateDiff functions
        /// </summary>
        internal readonly static ErrorResourceKey InvalidTimeUnit = DataverseStringResources.New("FormulaColumns_InvalidTimeUnit");

        /// <summary>
        /// The error resource key for an incorrect number of arguments for Math functions
        /// </summary>
        internal readonly static ErrorResourceKey MathFunctionBadArity = DataverseStringResources.New("FormulaColumns_MathFunctionBadArity");

        /// <summary>
        /// The error resource key for the Text function with invalid numeric format
        /// </summary>
        internal readonly static ErrorResourceKey NumericFormatNotSupported = DataverseStringResources.New("FormulaColumns_NumericFormatNotSupported");

        /// <summary>
        /// The error resource key for use of a function that is not supported, with a suggested alternate
        /// </summary>
        internal readonly static ErrorResourceKey FunctionNotSupported = DataverseStringResources.New("FormulaColumns_FunctionNotSupported");

        /// <summary>
        /// The error resource key for use of an unsupported operation
        /// </summary>
        internal readonly static ErrorResourceKey OperationNotSupported = DataverseStringResources.New("FormulaColumns_OperationNotSupported");

        /// <summary>
        /// The error resource key for an unsupported result type
        /// </summary>
        internal readonly static ErrorResourceKey ResultTypeNotSupported = DataverseStringResources.New("FormulaColumns_ResultTypeNotSupported");

        /// <summary>
        /// The error resource key for an entire record reference
        /// </summary>
        internal readonly static ErrorResourceKey RecordAccessNotSupported = DataverseStringResources.New("FormulaColumns_RecordAccessNotSupported");

        /// <summary>
        /// The error resource key for an result type that does not match the requested type
        /// </summary>
        internal readonly static ErrorResourceKey ResultTypeMustMatch = DataverseStringResources.New("FormulaColumns_ResultTypeMustMatch");

        /// <summary>
        /// The error resource key for a Dataverse column type that is not supported
        /// </summary>
        internal readonly static ErrorResourceKey ColumnTypeNotSupported = DataverseStringResources.New("FormulaColumns_ColumnTypeNotSupported");

        /// <summary>
        /// The error resource key for a Dataverse column format that is not supported
        /// </summary>
        internal readonly static ErrorResourceKey ColumnFormatNotSupported = DataverseStringResources.New("FormulaColumns_ColumnFormatNotSupported");

        /// <summary>
        /// The error resource key for a Dataverse column format that is not supported
        /// </summary>
        internal readonly static ErrorResourceKey AggregateCoercionNotSupported = DataverseStringResources.New("FormulaColumns_AggregateCoercionNotSupported");

        // <summary>
        /// The error resource key for a formula that references a virtual table
        /// </summary>
        internal readonly static ErrorResourceKey VirtualTableNotSupported = DataverseStringResources.New("FormulaColumns_VirtualTableNotSupported");

        // <summary>
        /// The error resource key for a formula that references a single-column table
        /// </summary>
        internal readonly static ErrorResourceKey SingleColumnTableNotSupported = DataverseStringResources.New("FormulaColumns_SingleColumnTableNotSupported");

        // <summary>
        /// The error resource key for a formula with Text of a number without a format string.
        /// </summary>
        internal readonly static ErrorResourceKey TextNumberMissingFormat = DataverseStringResources.New("FormulaColumns_TextNumberMissingFormat");

        // <summary>
        /// The error resource key for a formula with implicit conversion of number to text.
        /// </summary>
        internal readonly static ErrorResourceKey ImplicitNumberToText = DataverseStringResources.New("FormulaColumns_ImplicitNumberToText");

        // <summary>
        /// The error resource key for a formula that references a related entity's currency field
        /// </summary>
        internal readonly static ErrorResourceKey RelatedCurrency = DataverseStringResources.New("FormulaColumns_RelatedCurrency");

        /// <summary>
        /// The error resource key for overflow numeric literal
        /// </summary>
        internal readonly static ErrorResourceKey OverflowNumericLiteral = DataverseStringResources.New("FormulaColumns_OverflowNumericLiteral");
        
        /// <summary>
        /// The error resource key for overflow decimal literal
        /// </summary>
        internal readonly static ErrorResourceKey OverflowDecimalLiteral = DataverseStringResources.New("FormulaColumns_OverflowDecimalLiteral");

        internal SqlCompileException(ErrorResourceKey key, Span context, params object[] args) : base()
        {
            _error = new SqlError(key, context, DocumentErrorSeverity.Critical, args);
        }

        internal SqlCompileException(Span context)
        {
            _error = new SqlError(NotSupported, context);
        }

        internal IEnumerable<IDocumentError> GetErrors(Span defaultSpan)
        {
            _error.EnsureErrorContext(defaultSpan);

            yield return _error;
        }

        // Return true if this error key is a SQL Not Supported case. 
        internal static bool IsError(string errorKey)
        {
            var fields = typeof(SqlCompileException).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            foreach (var field in fields)
            {
                if (field.FieldType == typeof(ErrorResourceKey))
                {
                    var key2 = (ErrorResourceKey)field.GetValue(null);
                    if (key2.Key == errorKey)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
