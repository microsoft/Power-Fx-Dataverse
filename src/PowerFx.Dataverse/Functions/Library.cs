//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Types;
using static Microsoft.PowerFx.Dataverse.SqlVisitor;
using BuiltinFunctionsCore = Microsoft.PowerFx.Core.Texl.BuiltinFunctionsCore;
using Span = Microsoft.PowerFx.Syntax.Span;
using TexlFunction = Microsoft.PowerFx.Core.Functions.TexlFunction;

namespace Microsoft.PowerFx.Dataverse.Functions
{
    internal static partial class Library
    {
        public delegate RetVal FunctionPtr(SqlVisitor runner, CallNode node, Context context);

        public static IEnumerable<TexlFunction> FunctionList => _funcsByName.Keys;

        // Some TexlFunctions are overloaded
        private static Dictionary<TexlFunction, FunctionPtr> _funcsByName = new Dictionary<TexlFunction, FunctionPtr>
        {
            { BuiltinFunctionsCore.Abs, (SqlVisitor runner, CallNode node, Context context) => MathNaryFunction(runner, node, context, "ABS", 1) },
            //{ BuiltinFunctionsCore.AddColumns, AddColumns },
            { BuiltinFunctionsCore.And, (SqlVisitor runner, CallNode node, Context context) => LogicalSetFunction(runner, node, context, "AND", false) },
            { BuiltinFunctionsCore.Average, (SqlVisitor runner, CallNode node, Context context) => MathScalarSetFunction(runner, node, context, "AVG", errorOnNulls:true) },
            //{ BuiltinFunctionsCore.AverageT, AverageTable },
            { BuiltinFunctionsCore.Blank, Blank },
            //{ BuiltinFunctionsCore.Concat, Concat },
            { BuiltinFunctionsCore.Concatenate, Concatenate },
            //{ BuiltinFunctionsCore.Coalesce, Coalesce },
            { BuiltinFunctionsCore.Char, Char },
            //{ BuiltinFunctionsCore.CountIf, CountIf },
            //{ BuiltinFunctionsCore.CountRows, CountRows },
            
            // Date is not supported since it returns user-local time, which isn't accurate due to time zone/ DST issues.
            // Instead, we should have a new Date function that returns TimeZoneIndependent. 
            // { BuiltinFunctionsCore.Date, Date },

            { BuiltinFunctionsCore.DateAdd, DateAdd },
            { BuiltinFunctionsCore.DateDiff, DateDiff },
            //{ BuiltinFunctionsCore.DateTimeValue, DateTimeValue },
            //{ BuiltinFunctionsCore.DateValue, DateValue },
            { BuiltinFunctionsCore.Day, (SqlVisitor runner, CallNode node, Context context) => DatePart(runner, node, context, SqlStatementFormat.Day) },
            { BuiltinFunctionsCore.EndsWith, (SqlVisitor runner, CallNode node, Context context) => StartsEndsWith(runner, node, context, MatchType.Suffix) },
            { BuiltinFunctionsCore.Error, Error },
          //  { BuiltinFunctionsCore.Exp, Exp },
            //{ BuiltinFunctionsCore.Filter, FilterTable },
            //{ BuiltinFunctionsCore.Find, Find },
            //{ BuiltinFunctionsCore.First, First },
            //{ BuiltinFunctionsCore.FirstN, FirstN },
            //{ BuiltinFunctionsCore.ForAll, ForAll },
            { BuiltinFunctionsCore.Hour, (SqlVisitor runner, CallNode node, Context context) => DatePart(runner, node, context, SqlStatementFormat.Hour) },
            { BuiltinFunctionsCore.If, If },
            { BuiltinFunctionsCore.IfError, IfError },
            { BuiltinFunctionsCore.Int, (SqlVisitor runner, CallNode node, Context context) => MathNaryFunction(runner, node, context, "FLOOR", 1) },
            { BuiltinFunctionsCore.IsBlank, IsBlank },
            { BuiltinFunctionsCore.IsBlankOptionSetValue, IsBlank },
            { BuiltinFunctionsCore.IsError, IsError },
            { BuiltinFunctionsCore.ISOWeekNum, (SqlVisitor runner, CallNode node, Context context) => DatePart(runner, node, context, "iso_week") },
            { BuiltinFunctionsCore.IsToday, (SqlVisitor runner, CallNode node, Context context) => NotSupported(runner, node, context, BuiltinFunctionsCore.IsUTCToday.LocaleSpecificName) },
            { BuiltinFunctionsCore.IsUTCToday, IsUTCToday },
            //{ BuiltinFunctionsCore.Last, Last},
            //{ BuiltinFunctionsCore.LastN, LastN},
            { BuiltinFunctionsCore.Left, (SqlVisitor runner, CallNode node, Context context) => LeftRight(runner, node, context, "LEFT") },
            { BuiltinFunctionsCore.Len, Len },
         //   { BuiltinFunctionsCore.Ln, Ln },
            { BuiltinFunctionsCore.Lower, (SqlVisitor runner, CallNode node, Context context) => UpperLower(runner, node, context, "LOWER") },
            { BuiltinFunctionsCore.Max, (SqlVisitor runner, CallNode node, Context context) => MathScalarSetFunction(runner, node, context, "MAX", zeroNulls:true) },
            { BuiltinFunctionsCore.Mid, Mid },
            { BuiltinFunctionsCore.Min, (SqlVisitor runner, CallNode node, Context context) => MathScalarSetFunction(runner, node, context, "MIN", zeroNulls:true) },
            { BuiltinFunctionsCore.Minute, (SqlVisitor runner, CallNode node, Context context) => DatePart(runner, node, context, SqlStatementFormat.Minute) },
            { BuiltinFunctionsCore.Mod, Mod },
            { BuiltinFunctionsCore.Month, (SqlVisitor runner, CallNode node, Context context) => DatePart(runner, node, context, SqlStatementFormat.Month) },
            { BuiltinFunctionsCore.Not, Not },
            { BuiltinFunctionsCore.Now, (SqlVisitor runner, CallNode node, Context context) => NowUTCNow(runner, node, context, FormulaType.DateTime) },
            { BuiltinFunctionsCore.Or, (SqlVisitor runner, CallNode node, Context context) => LogicalSetFunction(runner, node, context, "OR", true) },
           // { BuiltinFunctionsCore.Power, Power },
            { BuiltinFunctionsCore.Replace, Replace },
            { BuiltinFunctionsCore.Right, (SqlVisitor runner, CallNode node, Context context) => LeftRight(runner, node, context,"RIGHT") },
            { BuiltinFunctionsCore.Round, (SqlVisitor runner, CallNode node, Context context) => MathNaryFunction(runner, node, context, "ROUND", 2) },
            { BuiltinFunctionsCore.RoundUp, RoundUp },
            { BuiltinFunctionsCore.RoundDown, RoundDown },
            //{ BuiltinFunctionsCore.Sequence, Sequence},
            { BuiltinFunctionsCore.Second, (SqlVisitor runner, CallNode node, Context context) => DatePart(runner, node, context, SqlStatementFormat.Second) },
            { BuiltinFunctionsCore.StartsWith, (SqlVisitor runner, CallNode node, Context context) => StartsEndsWith(runner, node, context, MatchType.Prefix) },
            { BuiltinFunctionsCore.Sum, (SqlVisitor runner, CallNode node, Context context) => MathScalarSetFunction(runner, node, context, "SUM") },
            //{ BuiltinFunctionsCore.SumT, SumTable },
          //  { BuiltinFunctionsCore.Sqrt, Sqrt },
            { BuiltinFunctionsCore.Substitute, Substitute },
            { BuiltinFunctionsCore.Switch, Switch },
            //{ BuiltinFunctionsCore.Table, Table },
            { BuiltinFunctionsCore.Text, Text },
            //{ BuiltinFunctionsCore.TimeZoneOffset, TimeZoneOffset },
            { BuiltinFunctionsCore.Today, (SqlVisitor runner, CallNode node, Context context) => NotSupported(runner, node, context, BuiltinFunctionsCore.UTCToday.LocaleSpecificName) },
            { BuiltinFunctionsCore.Trim, Trim },
            { BuiltinFunctionsCore.TrimEnds, TrimEnds },
            { BuiltinFunctionsCore.Trunc, Trunc },
            { BuiltinFunctionsCore.Upper, (SqlVisitor runner, CallNode node, Context context) => UpperLower(runner, node, context, "UPPER") },
            { BuiltinFunctionsCore.UTCNow, (SqlVisitor runner, CallNode node, Context context) => NowUTCNow(runner, node, context, FormulaType.DateTimeNoTimeZone) },
            { BuiltinFunctionsCore.UTCToday, UTCToday },
            { BuiltinFunctionsCore.Value, Value },
            { BuiltinFunctionsCore.WeekNum, (SqlVisitor runner, CallNode node, Context context) => DatePart(runner, node, context, SqlStatementFormat.Week) },
            { BuiltinFunctionsCore.Weekday, (SqlVisitor runner, CallNode node, Context context) => DatePart(runner, node, context, "weekday") },
            //{ BuiltinFunctionsCore.With, With },
            { BuiltinFunctionsCore.Year, (SqlVisitor runner, CallNode node, Context context) => DatePart(runner, node, context, SqlStatementFormat.Year) }
        };

        public static bool TryLookup(TexlFunction func, out FunctionPtr ptr)
        {
            return _funcsByName.TryGetValue(func, out ptr);
        }

        public static RetVal NotSupported(SqlVisitor runner, CallNode node, Context context, string suggestedFunction)
        {
            throw new SqlCompileException(SqlCompileException.FunctionNotSupported, node.IRContext.SourceContext, node.Function.LocaleSpecificName, suggestedFunction);
        }

        public static SqlCompileException BuildUnsupportedArgumentException(TexlFunction func, int argumentIndex, Span sourceContext = default)
        {
            return new SqlCompileException(SqlCompileException.ArgumentNotSupported, sourceContext, GetArgumentName(func, argumentIndex), func.LocaleSpecificName);
        }

        public static SqlCompileException BuildUnsupportedArgumentTypeException(string type, Span sourceContext = default)
        {
            return new SqlCompileException(SqlCompileException.ArgumentTypeNotSupported, sourceContext, type);
        }

        public static SqlCompileException BuildLiteralArgumentException(Span sourceContext = default)
        {
            return new SqlCompileException(SqlCompileException.LiteralArgRequired, sourceContext);
        }

        private static string GetArgumentName(TexlFunction func, int argumentIndex)
        {
            var args = func.GetParamNames().ToArray();
            Contracts.Assert(argumentIndex < args.Length);
            return args[argumentIndex];
        }

        // Try Coalesce(number,0) pattern.
        // IR emits this specifically for Number-->Blank coercions. 
        // This is only a partial implementation for Coalesce. 
        // Once Coalesce is in the dictionary, then we can remove this. 
        public static bool TryCoalesceNum(SqlVisitor runner, CallNode node, Context context, out RetVal ret)
        {
            if (node.Function == BuiltinFunctionsCore.Coalesce)
            {
                if (node.Args.Count == 2)
                {
                    if (node.Args[1] is NumberLiteralNode num && num.LiteralValue == 0)
                    {
                        var arg0 = node.Args[0].IRContext.ResultType;
                        if (arg0 == FormulaType.Number || arg0 == FormulaType.Blank)
                        {
                            Library.ValidateNumericArgument(node.Args[0]);
                            var arg = node.Args[0].Accept(runner, context);

                            var argString = Library.CoerceNullToInt(arg);

                            var result = context.GetTempVar(new SqlBigType());
                            context.SetIntermediateVariable(result, argString);

                            ret = result;
                            return true;
                        }
                    }
                    else if (node.Args[1] is DecimalLiteralNode dec && dec.LiteralValue == 0)
                    {
                        var arg0 = node.Args[0].IRContext.ResultType;
                        if (arg0 == FormulaType.Decimal || arg0 == FormulaType.Blank)
                        {
                            Library.ValidateNumericArgument(node.Args[0]);
                            var arg = node.Args[0].Accept(runner, context);

                            var argString = Library.CoerceNullToInt(arg);

                            var result = context.GetTempVar(new SqlVeryBigType());
                            context.SetIntermediateVariable(result, argString);

                            ret = result;
                            return true;
                        }
                    }
                }
            }

            ret = null;
            return false;
        }
    }
}
