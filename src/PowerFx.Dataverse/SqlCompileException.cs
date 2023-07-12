using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.PowerFx.Core.Errors;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Dataverse.Localization;
using Microsoft.PowerFx.Syntax;

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

        private readonly SqlError _error;

        /// <summary>
        /// The error resource key for a generic not supported error
        /// </summary>
        internal static readonly ErrorResourceKey NotSupported = DataverseStringResources.New("FormulaColumns_NotSupported");

        /// <summary>
        /// The error resource key for an argument not supported error
        /// </summary>
        internal static readonly ErrorResourceKey ArgumentNotSupported = DataverseStringResources.New("FormulaColumns_ArgNotSupported");

        /// <summary>
        /// The error resource key for an argument type not supported error
        /// </summary>
        internal static readonly ErrorResourceKey ArgumentTypeNotSupported = DataverseStringResources.New("FormulaColumns_ArgumentTypeNotSupported");

        /// <summary>
        /// The error resource key for an argument requiring a literal value
        /// </summary>
        internal static readonly ErrorResourceKey LiteralArgRequired = DataverseStringResources.New("FormulaColumns_LiteralArgRequired");

        /// <summary>
        /// The error resource key for an operation requiring time zone conversion
        /// </summary>);
        internal static readonly ErrorResourceKey TimeZoneConversion = DataverseStringResources.New("FormulaColumns_TimeZoneConversion");

        /// <summary>
        /// The error resource key for an operation with incompatible date time
        /// </summary>
        internal static readonly ErrorResourceKey IncompatibleDateTimes = DataverseStringResources.New("FormulaColumns_IncompatibleDateTimes");

        /// <summary>
        /// The error resource key for an operation with an incorrect unit of time supplied to DateAdd/DateDiff functions
        /// </summary>
        internal static readonly ErrorResourceKey InvalidTimeUnit = DataverseStringResources.New("FormulaColumns_InvalidTimeUnit");

        /// <summary>
        /// The error resource key for an incorrect number of arguments for Math functions
        /// </summary>
        internal static readonly ErrorResourceKey MathFunctionBadArity = DataverseStringResources.New("FormulaColumns_MathFunctionBadArity");

        /// <summary>
        /// The error resource key for the Text function with invalid numeric format
        /// </summary>
        internal static readonly ErrorResourceKey NumericFormatNotSupported = DataverseStringResources.New("FormulaColumns_NumericFormatNotSupported");

        /// <summary>
        /// The error resource key for use of a function that is not supported, with a suggested alternate
        /// </summary>
        internal static readonly ErrorResourceKey FunctionNotSupported = DataverseStringResources.New("FormulaColumns_FunctionNotSupported");

        /// <summary>
        /// The error resource key for use of an unsupported operation
        /// </summary>
        internal static readonly ErrorResourceKey OperationNotSupported = DataverseStringResources.New("FormulaColumns_OperationNotSupported");

        /// <summary>
        /// The error resource key for an unsupported result type
        /// </summary>
        internal static readonly ErrorResourceKey ResultTypeNotSupported = DataverseStringResources.New("FormulaColumns_ResultTypeNotSupported");

        /// <summary>
        /// The error resource key for an entire record reference
        /// </summary>
        internal static readonly ErrorResourceKey RecordAccessNotSupported = DataverseStringResources.New("FormulaColumns_RecordAccessNotSupported");

        /// <summary>
        /// The error resource key for an result type that does not match the requested type
        /// </summary>
        internal static readonly ErrorResourceKey ResultTypeMustMatch = DataverseStringResources.New("FormulaColumns_ResultTypeMustMatch");

        /// <summary>
        /// The error resource key for a Dataverse column type that is not supported
        /// </summary>
        internal static readonly ErrorResourceKey ColumnTypeNotSupported = DataverseStringResources.New("FormulaColumns_ColumnTypeNotSupported");

        /// <summary>
        /// The error resource key for a Dataverse column format that is not supported
        /// </summary>
        internal static readonly ErrorResourceKey ColumnFormatNotSupported = DataverseStringResources.New("FormulaColumns_ColumnFormatNotSupported");

        /// <summary>
        /// The error resource key for a Dataverse column format that is not supported
        /// </summary>
        internal static readonly ErrorResourceKey AggregateCoercionNotSupported = DataverseStringResources.New("FormulaColumns_AggregateCoercionNotSupported");

        // <summary>
        /// The error resource key for a formula that references a virtual table
        /// </summary>
        internal static readonly ErrorResourceKey VirtualTableNotSupported = DataverseStringResources.New("FormulaColumns_VirtualTableNotSupported");

        // <summary>
        /// The error resource key for a formula that references a single-column table
        /// </summary>
        internal static readonly ErrorResourceKey SingleColumnTableNotSupported = DataverseStringResources.New("FormulaColumns_SingleColumnTableNotSupported");

        // <summary>
        /// The error resource key for a formula with Text of a number without a format string.
        /// </summary>
        internal static readonly ErrorResourceKey TextNumberMissingFormat = DataverseStringResources.New("FormulaColumns_TextNumberMissingFormat");

        // <summary>
        /// The error resource key for a formula with implicit conversion of number to text.
        /// </summary>
        internal static readonly ErrorResourceKey ImplicitNumberToText = DataverseStringResources.New("FormulaColumns_ImplicitNumberToText");

        /// <summary>
        /// The error resource key for a formula with implicit conversion of decimal to text.
        /// </summary>
        internal static readonly ErrorResourceKey ImplicitDecimalToText = DataverseStringResources.New("FormulaColumns_ImplicitDecimalToText");

        // <summary>
        /// The error resource key for a formula that references a related entity's currency field
        /// </summary>
        internal static readonly ErrorResourceKey RelatedCurrency = DataverseStringResources.New("FormulaColumns_RelatedCurrency");

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
