//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Types;
using System;
using static Microsoft.PowerFx.Dataverse.SqlVisitor;
using Span = Microsoft.PowerFx.Syntax.Span;

namespace Microsoft.PowerFx.Dataverse.Functions
{
    internal static partial class Library
    {
        public static RetVal NowUTCNow(SqlVisitor visitor, CallNode node, Context context, FormulaType formulaType)
        {
            context.expressionHasTimeBoundFunction = true;
            // round to the second, for parity with existing legacy CDS calculated fields
            return context.SetIntermediateVariable(formulaType, "DATEADD(ms, (0 - DATEPART(ms, GETUTCDATE())), GETUTCDATE())");
        }

        public static RetVal UTCToday(SqlVisitor visitor, CallNode node, Context context)
        {
            context.expressionHasTimeBoundFunction = true;
            return context.SetIntermediateVariable(FormulaType.DateTimeNoTimeZone, "CONVERT(date, GETUTCDATE())");
        }

        public static RetVal IsUTCToday(SqlVisitor visitor, CallNode node, Context context)
        {
            // determines if the date (stored in UTC) matches today (UTC)
            // ignores the user's time zone
            var date = node.Args[0].Accept(visitor, context);
            return context.SetIntermediateVariable(node, $"CONVERT(date, {date}) = CONVERT(date, GETUTCDATE())");
        }

        public static RetVal DateAdd(SqlVisitor visitor, CallNode node, Context context)
        {
            // first visit the units node
            string units = GetUnits(node, 2, visitor, context);

            var date = node.Args[0].Accept(visitor, context);
            var offset = node.Args[1].Accept(visitor, context);

            return AddDates(node, date, offset, units, context);
        }

        public static RetVal DateDiff(SqlVisitor visitor, CallNode node, Context context)
        {
            var date1 = node.Args[0].Accept(visitor, context);
            var date2 = node.Args[1].Accept(visitor, context);
            string units = GetUnits(node, 2, visitor, context);
            context.DateDiffOverflowCheck(date1, date2, units);

            ValidateTypeCompatibility(date1, date2, node.IRContext.SourceContext);
            return context.SetIntermediateVariable(node, $"DATEDIFF({units}, {date1}, {date2})");
        }

        public static void ValidateTypeCompatibility(RetVal val1, RetVal val2, Span sourceContext)
        {
            // if the representations of the dates are the same (local in UTC(TZI/DateOnly) vs UTC), they can be used together
            if (IsDateTimeType(val1.type) && ColumnContainsLocalDateInUTC(val1.type) != ColumnContainsLocalDateInUTC(val2.type))
            {
                throw BuildDateTimeCompatibilityError(sourceContext);
            }
        }

        private static string GetUnits(CallNode callNode, int argumentIndex, SqlVisitor visitor, Context context)
        {
            var unitsNode = callNode.Args.Count > argumentIndex ? callNode.Args[argumentIndex] : null;
            if (unitsNode == null)
            {
                return "day"; // default value
            }

            string units = null;

            if (unitsNode is UnaryOpNode u)
            {
                if (u.Child is RecordFieldAccessNode fieldNode)
                {
                    units = fieldNode.Field.ToString().ToLowerInvariant();
                }
            }
            else if (unitsNode is TextLiteralNode)
            {
                using (context.NewInlineLiteralContext())
                {
                    var unitsEnum = unitsNode.Accept(visitor, context);
                    units = unitsEnum.inlineSQL.ToLowerInvariant();
                }
            }

            if (units != null)
            {
                return units switch
                {
                    "seconds" => SqlStatementFormat.Second,
                    "minutes" => SqlStatementFormat.Minute,
                    "hours" => SqlStatementFormat.Hour,
                    "days" => SqlStatementFormat.Day,
                    "weeks" => SqlStatementFormat.Week,
                    "months" => SqlStatementFormat.Month,
                    "quarters" => SqlStatementFormat.Quarter,
                    "years" => SqlStatementFormat.Year,
                    _ => throw new SqlCompileException(SqlCompileException.InvalidTimeUnit, unitsNode.IRContext.SourceContext, GetArgumentName(callNode.Function, argumentIndex), callNode.Function.Name)
                };
            }
            
            throw BuildLiteralArgumentException(callNode.Args[argumentIndex].IRContext.SourceContext);            
        }

        public static RetVal DatePart(SqlVisitor visitor, CallNode node, Context context, string part)
        {
            var date = node.Args[0].Accept(visitor, context);
            if (node.Args.Count > 1)
            {
                throw BuildUnsupportedArgumentException(node.Function, 1, node.Args[1].IRContext.SourceContext);
            }

            // return the requested date part from the raw value, validating if the time zone needs conversion
            CheckForTimeZoneConversionToLocal(date, node.Function, node.IRContext.SourceContext);
            return context.SetIntermediateVariable(node, $"DATEPART({part}, {date})");
        }

        public static void CheckForTimeZoneConversionToLocal(RetVal date, TexlFunction function, Span sourceContext)
        {
            // if the column isn't TimeZoneIndependent or DateOnly, it will need to be converted to local
            if (IsDateTimeType(date.type) && !ColumnContainsLocalDateInUTC(date.type))
            {
                throw BuildTimeZoneConversionError(function, sourceContext);
            }
        }

        public static void CheckForTimeZoneConversionToUTC(RetVal date, TexlFunction function, Span sourceContext)
        {
            // if the column is TZI or DateOnly, it will need to be converted to UTC to be evaluated
            if (IsDateTimeType(date.type) && ColumnContainsLocalDateInUTC(date.type))
            {
                throw BuildTimeZoneConversionError(function, sourceContext);
            }
        }

        public static Exception BuildTimeZoneConversionError(TexlFunction function, Span sourceContext)
        {
            throw new SqlCompileException(SqlCompileException.TimeZoneConversion, sourceContext, function.LocaleSpecificName);
        }

        public static Exception BuildDateTimeCompatibilityError(Span sourceContext)
        {
            throw new SqlCompileException(SqlCompileException.IncompatibleDateTimes, sourceContext);
        }

        public static bool ColumnContainsLocalDateInUTC(FormulaType type)
        {
            // Time Zone Independent and DateOnly fields are stored in UTC but should be interpreted in the user's locale
            return type is DateTimeNoTimeZoneType || type is DateType;
        }

        public static RetVal AddDates(IntermediateNode node, RetVal date, RetVal offset, string units, Context context)
        {
            return AddDatesInternal(node, date, offset, units, context);
        }

        public static RetVal AddDays(IntermediateNode node, RetVal date, RetVal offset, Context context)
        {
            return AddDatesInternal(node, date, offset, SqlStatementFormat.Day, context, supportFractionalDays: true);
        }

        private static RetVal AddDatesInternal(IntermediateNode node, RetVal date, RetVal offset, string units, Context context, bool supportFractionalDays = false)
        {
            // use the same return type as the input, so TZI type is retained
            var result = context.GetTempVar(date.type);
            var validatedOffset = context.SetIntermediateVariable(new SqlBigType(), $"ISNULL({offset},0)");
            context.DateAdditionOverflowCheck(validatedOffset, units, date);
            if (supportFractionalDays)
            {
                Contracts.Assert(units == SqlStatementFormat.Day);
                using (var indenter = context.NewIfIndenter())
                {
                    // if supporting fractional days, and the offset has a fractional part, do the addition at the seconds resolution
                    using (indenter.EmitIfCondition($"FLOOR({offset})<>{offset}"))
                    {
                        context.SetIntermediateVariable(result, $"DATEADD({SqlStatementFormat.Second}, {validatedOffset}*24*60*60, {date})");
                    }
                    using (indenter.EmitElse())
                    {
                        context.SetIntermediateVariable(result, $"DATEADD({units}, {validatedOffset}, {date})");
                    }
                }
                return result;
            }
            else
            {
                return context.SetIntermediateVariable(result, $"DATEADD({units}, {validatedOffset}, {date})");
            }
        }

        public static bool IsDateTimeType(FormulaType type)
        {
            return type is DateType || type is DateTimeType || type is DateTimeNoTimeZoneType;
        }
    }
}
