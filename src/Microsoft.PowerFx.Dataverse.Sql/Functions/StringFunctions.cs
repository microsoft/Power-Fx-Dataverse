//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using BuiltinFunctionsCore = Microsoft.PowerFx.Core.Texl.BuiltinFunctionsCore;
using static Microsoft.PowerFx.Dataverse.SqlVisitor;

namespace Microsoft.PowerFx.Dataverse.Functions
{
    internal static partial class Library
    {
        public static RetVal Value(SqlVisitor visitor, CallNode node, Context context)
        {
            if (node.Args.Count > 1)
            {
                throw BuildUnsupportedArgumentException(node.Function, 1, node.Args[1].IRContext.SourceContext);
            }
            var arg0 = node.Args[0];
            RetVal arg = null;

            // Currency Fields can only be accepted through Decimal function so passing this flag valueFunctionCall to accept currency fields in this case 
            if (node.Args.Count == 1 && node.Function != BuiltinFunctionsCore.Float && arg0 is ScopeAccessNode scopeAccessNode && scopeAccessNode.Value is ScopeAccessSymbol scopeAccess)
            {
                var varDetails = context.GetVarDetails(scopeAccess, scopeAccessNode.IRContext.SourceContext, true);
                arg = RetVal.FromVar(varDetails.VarName, context.GetReturnType(scopeAccessNode, varDetails.VarType));
            }
            else
            {
                arg = arg0.Accept(visitor, context);
            }
            
            if (arg.type is StringType)
            {
                if (arg0 is TextLiteralNode literal)
                {
                    if (!int.TryParse(literal.LiteralValue, out var ignore))
                    {
                        context._unsupportedWarnings.Add("Value() only works on ints");
                    }
                }

                // TODO: evaluate SQL perf for visitor scenario, should it be a function that can be tuned?
                var result = context.GetTempVar(context.GetReturnType(node));
                var numberType = ToSqlType(result.type, context._dataverseFeatures);

                if (context._dataverseFeatures.IsFloatingPointEnabled && result.type is NumberType)
                {
                    context.SetIntermediateVariable(result, $"TRY_PARSE({CoerceNullToString(arg)} AS FLOAT)");
                }
                else
                {
                    // only allow whole numbers to be parsed
                    context.SetIntermediateVariable(result, $"TRY_PARSE({CoerceNullToString(arg)} AS decimal(23,10))");
                }

                context.ErrorCheck($"LEN({CoerceNullToString(arg)}+N'x') <> 1 AND (CHARINDEX(N'.',{arg}) > 0 OR CHARINDEX(N',',{arg}) > 0 OR {result} IS NULL)", Context.ValidationErrorCode, postValidation: true);
                context.PerformRangeChecks(result, node);
                return result;
            }
            else if (context.IsNumericType(arg))
            {
                var column = context.GetVarDetails(arg.varName).Column;
                if (column != null && (column.TypeCode == AttributeTypeCode.Money || column.LogicalName.Equals("exchangerate")))
                {
                    var result = context.GetTempVar(context.GetReturnType(node));

                    if (result.type is NumberType)
                    {
                        context.TryCastToFloat($"{CoerceNullToInt(arg)}", result);
                    }
                    else
                    {
                        // only allow whole numbers to be parsed
                        context.TryCastToDecimal($"{CoerceNullToInt(arg)}", result);
                    }
                        
                    context.PerformRangeChecks(result, node);
                    return result;
                }

                // calling Value on a number is a pass-thru
                return context.SetIntermediateVariable(node, arg.ToString());
            }
            else if (arg.type is BlankType)
            {
                return context.SetIntermediateVariable(node, "NULL");
            }
            else
            {
                throw BuildUnsupportedArgumentTypeException(arg.type._type.GetKindString(), arg0.IRContext.SourceContext);
            }
        }

        public static RetVal Text(SqlVisitor visitor, CallNode node, Context context)
        {
            Contracts.Assert(node.Args.Count >= 1);

            var val = node.Args[0].Accept(visitor, context);

            if (node.Args.Count > 2)
            {
                throw BuildUnsupportedArgumentException(node.Function, 2, node.Args[2].IRContext.SourceContext);
            }
            else if (node.Args.Count == 1)
            {
                if (val.type is StringType)
                {
                    // String passes through Text()
                    // Blank values should pass through too and not be converted to an empty string
                    return context.SetIntermediateVariable(node, $"{val}");
                }
                else if (val.type is BlankType)
                {
                    // Blank() passes through Text()
                    return context.SetIntermediateVariable(node, $"NULL");
                }
                else if (context.IsNumericType(val))
                {
                    throw new SqlCompileException(SqlCompileException.TextNumberMissingFormat, node.IRContext.SourceContext);
                }
            }
            // two arguments is only supported for numbers, datetimes, and typed/untyped blanks
            else if (node.Args.Count == 2 && (context.IsNumericType(val) || val.type is BlankType))
            {
                string format = null;

                if (node.Args[1] is TextLiteralNode)
                {
                    using (context.NewInlineLiteralContext())
                    {
                        var formatArg = node.Args[1].Accept(visitor, context);
                        format = formatArg.ToString();
                    }
                }
                else if (node.Args[1].IRContext.ResultType is BlankType)
                {
                    // if the format is blank, emit an empty string
                    return context.SetIntermediateVariable(node, $"N''");
                }
                else
                {
                    throw BuildLiteralArgumentException(node.Args[1].IRContext.SourceContext);
                }

                var result = context.GetTempVar(context.GetReturnType(node));
                if (format == null)
                {
                    var defaultFormatted = visitor.CoerceNumberToString(val, context);
                    context.SetIntermediateVariable(result, fromRetVal: defaultFormatted);
                }
                else
                {
                    // The numeric formating placeholders for Text: https://docs.microsoft.com/en-us/powerapps/maker/canvas-apps/functions/function-text#number-placeholders
                    // generally match the .NET placeholders used by SQL: https://docs.microsoft.com/en-us/dotnet/standard/base-types/custom-numeric-format-strings

                    // Do not allow , . or locale tag or datetime format
                    if (!TextFormatUtils.IsValidFormatArg(format, formatCulture: null, defaultLanguage: null, out var textFormatArgs) || Regex.IsMatch(textFormatArgs.FormatArg, @"(,|\.)") 
                        || (textFormatArgs.DateTimeFmt != DateTimeFmtType.NoDateTimeFormat) || !string.IsNullOrEmpty(textFormatArgs.FormatCultureName))
                    {
                        context._unsupportedWarnings.Add("Unsupported numeric format");
                        throw new SqlCompileException(SqlCompileException.NumericFormatNotSupported, node.Args[1].IRContext.SourceContext);
                    }

                    format = textFormatArgs.FormatArg;

                    // For Excel compat, an empty format string should return an empty result string
                    // .NET format will treat an empty format string as a general format
                    if (format == "")
                    {
                        context.SetIntermediateVariable(result, "N''");
                    }
                    else
                    {
                        // Double quoted escaping appears to have a bug in SQL/CLR wtih SQL Server 2019.
                        //
                        // For example:
                        //    Text( 567, "0 ""a"" 0" ) incorrectly returns "56 a 0"
                        //    Text( 567, "0 \a 0" ) correctly returns "56 a 7"
                        //
                        // The equivalent SQL for the above is:
                        //    SELECT FORMAT( 567.0, N'0 "a" 0') incorrectly returns "56 a 0"
                        //    SELECT FORMAT( 567.0, N'0 \a 0' correctly returns "56 a 7"
                        //
                        // To avoid this problem, double quoted string escaping is converted to per character backslash escaping
                        //
                        StringBuilder backslashEscaped = new StringBuilder();
                        int strLength = format.Length;
                        bool inDblQuotes = false;
                        for (int i = 0; i < strLength; i++)
                        {
                            if (format[i] == '\"')
                            {
                                inDblQuotes = !inDblQuotes;
                            }
                            else if (format[i] == '\\' && !inDblQuotes)
                            {
                                backslashEscaped.Append('\\');
                                if (++i < strLength)
                                {
                                    backslashEscaped.Append(format[i]);
                                }
                            }
                            else
                            {
                                if (inDblQuotes)
                                {
                                    backslashEscaped.Append('\\');
                                }
                                backslashEscaped.Append(format[i]);
                            }
                        }

                        format = backslashEscaped.ToString();

                        // Single ticks need to be doubled in order to escape within the single tick delimited format string used in the FORMAT call below.
                        // This needs to be done after all other adjustments above
                        format = format.Replace("'", "''");

                        // Format function throws error if null arg is passed - e.g, FORMAT(NULL, N'0')
                        // use 0 if numeric arg is NULL which result in # placeholders not being filled (correctly)
                        context.SetIntermediateVariable(result, $"FORMAT({CoerceNullToInt(val)}, N'{format}')");
                    }
                }
                return result;
            }

            throw BuildUnsupportedArgumentTypeException(val.type._type.GetKindString(), node.Args[0].IRContext.SourceContext);
        }

        public static RetVal UpperLower(SqlVisitor visitor, CallNode node, Context context, string function)
        {
            var val = node.Args[0].Accept(visitor, context);
            // Null values are coerced to empty string
            return context.SetIntermediateVariable(node, $"{function}({CoerceNullToString(val)})");
        }

        public static RetVal StringUnaryFunction(SqlVisitor visitor, CallNode node, Context context, string function)
        {
            var val = node.Args[0].Accept(visitor, context);
            return context.SetIntermediateVariable(node, $"{function}({CoerceNullToString(val)})");
        }

        public static RetVal Char(SqlVisitor visitor, CallNode node, Context context)
        {
            var val = node.Args[0].Accept(visitor, context);
            var expression = RoundDownToInt(val);
            var roundedVal = context.TryCastToDecimal(expression);
            context.ErrorCheck($"{roundedVal} < 1 OR {roundedVal} > 255", Context.ValidationErrorCode, postValidation:true);
            return context.SetIntermediateVariable(node, $"CHAR({roundedVal})");
        }

        public static RetVal Concatenate(SqlVisitor visitor, CallNode node, Context context)
        {
            var args = new List<string>(node.Args.Count);
            for (int i = 0; i < node.Args.Count; i++)
            {
                var arg = node.Args[i].Accept(visitor, context);

                var coercedArg = context.SetIntermediateVariable(FormulaType.String, CoerceNullToString(arg));
                // if there is a single parameter, this is a pass thru
                if (i == 0 && node.Args.Count == 1)
                {
                    return context.SetIntermediateVariable(node, $"{coercedArg}");
                }
                else
                {
                    args.Add(coercedArg.ToString());
                }
            }

            return context.SetIntermediateVariable(node, args.Any() ? $"CONCAT({string.Join(",", args)})" : "''");
        }

        public static RetVal Blank(SqlVisitor visitor, CallNode node, Context context)
        {
            return RetVal.FromSQL("NULL", context.GetReturnType(node));
        }

        public static RetVal IsBlank(SqlVisitor visitor, CallNode node, Context context)
        {
            var arg = node.Args[0].Accept(visitor, context);
            if (arg.type is StringType)
            {
                return context.SetIntermediateVariable(node, $"({arg} IS NULL OR LEN({arg}+N'x') = 1)");
            }
            else
            {
                return context.SetIntermediateVariable(node, $"({arg} IS NULL)");
            }
        }

        public static RetVal LeftRight(SqlVisitor visitor, CallNode node, Context context, string function)
        {
            var strArg = node.Args[0].Accept(visitor, context);
            var offset = node.Args[1].Accept(visitor, context);
            
            context.NegativeNumberCheck(offset);
            //Left Right functions doesn't support offset more than int range
            context.ErrorCheck($"{offset} > {SqlStatementFormat.IntTypeMaxValue}", Context.ValidationErrorCode, postValidation: true);

            // zero offsets are not considered errors, and return empty string
            return context.SetIntermediateVariable(node, CoerceNullToString(RetVal.FromSQL($"{function}({strArg},{offset})", FormulaType.String)));
        }

        public static RetVal Mid(SqlVisitor visitor, CallNode node, Context context)
        {
            var strArg = node.Args[0].Accept(visitor, context);

            ValidateNumericArgument(node.Args[1]);
            RetVal start = context.SetIntermediateVariable(FormulaType.Decimal, $"TRY_CAST({node.Args[1].Accept(visitor, context)} AS INT)");
            context.NullCheck(start, postValidation: true);
            context.NonPositiveNumberCheck(start);

            RetVal length;
            if (node.Args.Count > 2)
            {
                ValidateNumericArgument(node.Args[2]);
                length = context.SetIntermediateVariable(FormulaType.Decimal, $"TRY_CAST({node.Args[2].Accept(visitor, context)} AS INT)");
                context.NullCheck(length, postValidation: true);
                context.NegativeNumberCheck(length);
            }
            else
            {
                // SQL ignores trailing spaces when counting the length
                length = RetVal.FromSQL($"LEN({CoerceNullToString(strArg)}+N'x')-1", FormulaType.Decimal);
            }

            return context.SetIntermediateVariable(node, $" SUBSTRING({CoerceNullToString(strArg)},{start},{length})");
        }

        public static RetVal Len(SqlVisitor visitor, CallNode node, Context context)
        {
            var arg = node.Args[0].Accept(visitor, context);
            if (arg.type is StringType)
            {
                // SQL ignores trailing spaces when counting the length
                return context.SetIntermediateVariable(node, $"LEN({CoerceNullToString(arg)} + N'x')-1");
            }
            else
            {
                throw BuildUnsupportedArgumentTypeException(arg.type._type.GetKindString(), node.Args[0].IRContext.SourceContext);
            }
        }

        public static RetVal StartsEndsWith(SqlVisitor visitor, CallNode node, Context context, MatchType matchType)
        {
            var str = node.Args[0].Accept(visitor, context);
            var match = visitor.EncodeLikeArgument(node.Args[1], matchType, context);
            return context.SetIntermediateVariable(node, $"{CoerceNullToString(str)} LIKE {match}");
        }

        public static RetVal TrimEnds(SqlVisitor visitor, CallNode node, Context context)
        {
            var result = context.GetTempVar(context.GetReturnType(node));
            var str = node.Args[0].Accept(visitor, context);
            context.SetIntermediateVariable(result, $"RTRIM(LTRIM({CoerceNullToString(str)}))");
            return result;
        }

        public static RetVal Trim(SqlVisitor visitor, CallNode node, Context context)
        {
            var result = context.GetTempVar(context.GetReturnType(node));
            var str = node.Args[0].Accept(visitor, context);
            context.SetIntermediateVariable(result, $"RTRIM(LTRIM({CoerceNullToString(str)}))");
            context.AppendContentLine($"WHILE (CHARINDEX(N'  ',{result}) <> 0) BEGIN set {result}=REPLACE({result}, N'  ', N' ') END");
            return result;
        }

        public static RetVal Substitute(SqlVisitor visitor, CallNode node, Context context)
        {
            var result = context.GetTempVar(context.GetReturnType(node));
            var str = node.Args[0].Accept(visitor, context);
            var oldStr = node.Args[1].Accept(visitor, context);
            var newStr = node.Args[2].Accept(visitor, context);
            if (node.Args.Count == 4)
            {
                // TODO: this should converted to a UDF
                ValidateNumericArgument(node.Args[3]);
                var instance = node.Args[3].Accept(visitor, context);
                var expression = RoundDownNullToInt(instance);
                var coercedInstance = context.TryCastToDecimal(expression);

                context.LessThanOneNumberCheck(coercedInstance);

                var idx = context.GetTempVar(FormulaType.Decimal);
                var matchCount = context.GetTempVar(FormulaType.Decimal);
                context.SetIntermediateVariable(idx, $"1");
                context.SetIntermediateVariable(matchCount, $"1");
                // SQL ignores trailing whitespace when counting string length, so add an additional character and and remove it from the count
                var oldLen = context.SetIntermediateVariable(FormulaType.Decimal, $"LEN({CoerceNullToString(oldStr)}+N'x')-1");
                // find the appropriate instance (case sensitive) in the original string
                context.AppendContentLine($"WHILE({matchCount} <= {coercedInstance}) BEGIN set {idx}=CHARINDEX({CoerceNullToString(oldStr)} {SqlStatementFormat.CollateString}, {CoerceNullToString(str)}, {idx}); IF ({idx}=0 OR {matchCount}={coercedInstance}) BREAK; set {matchCount}+=1; set {idx}+={oldLen} END");

                context.SetIntermediateVariable(result, $"IIF({idx} <> 0, STUFF({CoerceNullToString(str)}, {idx}, {oldLen}, {CoerceNullToString(newStr)}), {CoerceNullToString(str)})");
            }
            else
            {
                // if no instance is indicated, replace all
                context.SetIntermediateVariable(result, $"REPLACE({CoerceNullToString(str)}, {CoerceNullToString(oldStr)} {SqlStatementFormat.CollateString}, {CoerceNullToString(newStr)})");
            }
            return result;
        }

        public static RetVal Replace(SqlVisitor visitor, CallNode node, Context context)
        {
            var result = context.GetTempVar(context.GetReturnType(node));
            var str = node.Args[0].Accept(visitor, context);
            var coercedStr = context.SetIntermediateVariable(FormulaType.String, CoerceNullToString(str));
            ValidateNumericArgument(node.Args[1]);
            var start = node.Args[1].Accept(visitor, context);
            // the start value must be 1 or larger
            context.NonPositiveNumberCheck(start);
            start = context.TryCastToInteger($"{start}", applyNullCheck : true);
            ValidateNumericArgument(node.Args[2]);
            var count = node.Args[2].Accept(visitor, context);
            // the count value must be 0 or larger
            context.NegativeNumberCheck(count);
            count = context.TryCastToInteger($"{count}", applyNullCheck: true);
            var newStr = node.Args[3].Accept(visitor, context);
            var coercedNewStr = context.SetIntermediateVariable(FormulaType.String, CoerceNullToString(newStr));
            // STUFF will return null if the start index is larger than the string, so concatenate the strings in that case
            return context.SetIntermediateVariable(result, $"ISNULL(STUFF({coercedStr}, {start}, {count}, {coercedNewStr}), {coercedStr} + {coercedNewStr})");
        }

        public static string CoerceNullToString(RetVal retVal)
        {
            return $"ISNULL({retVal},N'')";
        }
    }
}
