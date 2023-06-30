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
        internal readonly static ErrorResourceKey NotSupported = new ErrorResourceKey("DV_FormulaColumns_NotSupported", DataverseStringResources.LocalStringResources);

        /// <summary>
        /// The error resource key for an argument not supported error
        /// </summary>
        internal readonly static ErrorResourceKey ArgumentNotSupported = new ErrorResourceKey("DV_FormulaColumns_ArgNotSupported", DataverseStringResources.LocalStringResources);

        /// <summary>
        /// The error resource key for an argument type not supported error
        /// </summary>
        internal readonly static ErrorResourceKey ArgumentTypeNotSupported = new ErrorResourceKey("DV_FormulaColumns_ArgumentTypeNotSupported", DataverseStringResources.LocalStringResources);

        /// <summary>
        /// The error resource key for an argument requiring a literal value
        /// </summary>
        internal readonly static ErrorResourceKey LiteralArgRequired = new ErrorResourceKey("DV_FormulaColumns_LiteralArgRequired", DataverseStringResources.LocalStringResources);

        /// <summary>
        /// The error resource key for an operation requiring time zone conversion
        /// </summary>
        internal readonly static ErrorResourceKey TimeZoneConversion = new ErrorResourceKey("DV_FormulaColumns_TimeZoneConversion", DataverseStringResources.LocalStringResources);

        /// <summary>
        /// The error resource key for an operation with incompatible date time
        /// </summary>
        internal readonly static ErrorResourceKey IncompatibleDateTimes = new ErrorResourceKey("DV_FormulaColumns_IncompatibleDateTimes", DataverseStringResources.LocalStringResources);

        /// <summary>
        /// The error resource key for an operation with an incorrect unit of time supplied to DateAdd/DateDiff functions
        /// </summary>
        internal readonly static ErrorResourceKey InvalidTimeUnit = new ErrorResourceKey("DV_FormulaColumns_InvalidTimeUnit", DataverseStringResources.LocalStringResources);

        /// <summary>
        /// The error resource key for an incorrect number of arguments for Math functions
        /// </summary>
        internal readonly static ErrorResourceKey MathFunctionBadArity = new ErrorResourceKey("DV_FormulaColumns_MathFunctionBadArity", DataverseStringResources.LocalStringResources);

        /// <summary>
        /// The error resource key for the Text function with invalid numeric format
        /// </summary>
        internal readonly static ErrorResourceKey NumericFormatNotSupported = new ErrorResourceKey("DV_FormulaColumns_NumericFormatNotSupported", DataverseStringResources.LocalStringResources);

        /// <summary>
        /// The error resource key for use of a function that is not supported, with a suggested alternate
        /// </summary>
        internal readonly static ErrorResourceKey FunctionNotSupported = new ErrorResourceKey("DV_FormulaColumns_FunctionNotSupported", DataverseStringResources.LocalStringResources);

        /// <summary>
        /// The error resource key for use of an unsupported operation
        /// </summary>
        internal readonly static ErrorResourceKey OperationNotSupported = new ErrorResourceKey("DV_FormulaColumns_OperationNotSupported", DataverseStringResources.LocalStringResources);

        /// <summary>
        /// The error resource key for an unsupported result type
        /// </summary>
        internal readonly static ErrorResourceKey ResultTypeNotSupported = new ErrorResourceKey("DV_FormulaColumns_ResultTypeNotSupported", DataverseStringResources.LocalStringResources);

        /// <summary>
        /// The error resource key for an entire record reference
        /// </summary>
        internal readonly static ErrorResourceKey RecordAccessNotSupported = new ErrorResourceKey("DV_FormulaColumns_RecordAccessNotSupported", DataverseStringResources.LocalStringResources);

        /// <summary>
        /// The error resource key for an result type that does not match the requested type
        /// </summary>
        internal readonly static ErrorResourceKey ResultTypeMustMatch = new ErrorResourceKey("DV_FormulaColumns_ResultTypeMustMatch", DataverseStringResources.LocalStringResources);

        /// <summary>
        /// The error resource key for a Dataverse column type that is not supported
        /// </summary>
        internal readonly static ErrorResourceKey ColumnTypeNotSupported = new ErrorResourceKey("DV_FormulaColumns_ColumnTypeNotSupported", DataverseStringResources.LocalStringResources);

        /// <summary>
        /// The error resource key for a Dataverse column format that is not supported
        /// </summary>
        internal readonly static ErrorResourceKey ColumnFormatNotSupported = new ErrorResourceKey("DV_FormulaColumns_ColumnFormatNotSupported", DataverseStringResources.LocalStringResources);

        /// <summary>
        /// The error resource key for a Dataverse column format that is not supported
        /// </summary>
        internal readonly static ErrorResourceKey AggregateCoercionNotSupported = new ErrorResourceKey("DV_FormulaColumns_AggregateCoercionNotSupported", DataverseStringResources.LocalStringResources);

        // <summary>
        /// The error resource key for a formula that references a virtual table
        /// </summary>
        internal readonly static ErrorResourceKey VirtualTableNotSupported = new ErrorResourceKey("DV_FormulaColumns_VirtualTableNotSupported", DataverseStringResources.LocalStringResources);

        // <summary>
        /// The error resource key for a formula that references a single-column table
        /// </summary>
        internal readonly static ErrorResourceKey SingleColumnTableNotSupported = new ErrorResourceKey("DV_FormulaColumns_SingleColumnTableNotSupported", DataverseStringResources.LocalStringResources);

        // <summary>
        /// The error resource key for a formula with Text of a number without a format string.
        /// </summary>
        internal readonly static ErrorResourceKey TextNumberMissingFormat = new ErrorResourceKey("DV_FormulaColumns_TextNumberMissingFormat", DataverseStringResources.LocalStringResources);

        // <summary>
        /// The error resource key for a formula with implicit conversion of number to text.
        /// </summary>
        internal readonly static ErrorResourceKey ImplicitNumberToText = new ErrorResourceKey("DV_FormulaColumns_ImplicitNumberToText", DataverseStringResources.LocalStringResources);

        // <summary>
        /// The error resource key for a formula that references a related entity's currency field
        /// </summary>
        internal readonly static ErrorResourceKey RelatedCurrency = new ErrorResourceKey("DV_FormulaColumns_RelatedCurrency", DataverseStringResources.LocalStringResources);

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
