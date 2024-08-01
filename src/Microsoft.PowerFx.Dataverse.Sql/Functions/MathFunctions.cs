// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Globalization;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Types;
using static Microsoft.PowerFx.Dataverse.SqlVisitor;

namespace Microsoft.PowerFx.Dataverse.Functions
{
    internal static partial class Library
    {
        public static RetVal Mod(SqlVisitor visitor, CallNode node, Context context)
        {
            bool isFloatFlow = node.IRContext.ResultType is NumberType;

            ValidateNumericArgument(node.Args[0]);
            var number = node.Args[0].Accept(visitor, context);
            ValidateNumericArgument(node.Args[1]);
            var divisor = node.Args[1].Accept(visitor, context);
            context.DivideByZeroCheck(divisor);

            // Modulo operator always expect dividend and divisor arg to be of type other than float/real
            number = context.TryCastToDecimal($"{CoerceNullToInt(number)}");
            divisor = context.TryCastToDecimal($"{divisor}");

            var result = context.GetTempVar(isFloatFlow ? FormulaType.Number : FormulaType.Decimal);
            var initialResult = context.TryCast($"{number} % {divisor}", castToFloat: isFloatFlow);

            // SQL returns the modulo where the sign matches the number.  PowerApps and Excel match the sign of the divisor.  If the result doesn't match the sign of the divisor, add the divior
            var finalExpression = $"IIF(({initialResult} <= 0 AND {divisor} <= 0) OR ({initialResult} >= 0 AND {divisor} >= 0), {initialResult}, {initialResult} + {divisor})";
            context.TryCast(finalExpression, result, castToFloat: isFloatFlow);

            context.PerformRangeChecks(result, node);
            return result;
        }

        // Blank coercion was already handled by IR.
        public static RetVal MathNaryFunction(SqlVisitor visitor, CallNode node, Context context, string function, int arity)
        {
            bool isFloatFlow = node.IRContext.ResultType is NumberType;

            if (node.Args.Count != arity)
            {
                throw new SqlCompileException(SqlCompileException.MathFunctionBadArity, node.IRContext.SourceContext, function, node.Args.Count, arity);
            }

            var result = context.GetTempVar(isFloatFlow ? FormulaType.Number : FormulaType.Decimal);
            var args = new List<string>(arity);
            var retValList = new List<RetVal>(arity);

            for (int i = 0; i < arity; i++)
            {
                ValidateNumericArgument(node.Args[i]);
                var arg = node.Args[i].Accept(visitor, context);

                var argString = arg.ToString();
                args.Add(argString);

                retValList.Add(arg);
            }

            if (function.Equals("ROUND"))
            {
                context.AppendRoundMaxMinConditions(retValList[1]);
            }

            context.SetIntermediateVariable(result, $"TRY_CAST({function}({string.Join(",", args)}) AS {ToSqlType(result.Type, context._dataverseFeatures)})");
            return result;
        }

        public static RetVal MathScalarSetFunction(SqlVisitor visitor, CallNode node, Context context, string function, bool zeroNulls = false, bool errorOnNulls = false)
        {
            bool isFloatFlow = node.IRContext.ResultType is NumberType;
            var result = context.GetTempVar(isFloatFlow ? FormulaType.Number : FormulaType.Decimal);
            var args = new List<string>(node.Args.Count);
            for (int i = 0; i < node.Args.Count; i++)
            {
                var type = context.GetReturnType(node.Args[i]);
                ValidateNumericArgument(node.Args[i]);
                var arg = node.Args[i].Accept(visitor, context);

                // coerce to numeric or null
                // do an explicit check for a liternal null, since IIF cannot have nulls in both branches
                var coercedArg = type is BlankType || arg.ToString() == "NULL" ? "NULL" : $"IIF(ISNUMERIC({arg})=1,{arg},NULL)";

                // if there is a single parameter, this is a pass thru
                if (i == 0 && node.Args.Count == 1)
                {
                    context.SetIntermediateVariable(result, coercedArg);
                    if (zeroNulls)
                    {
                        context.SetIntermediateVariable(result, CoerceNullToInt(result));
                    }
                    else if (errorOnNulls)
                    {
                        context.NullCheck(result, postValidation: true);
                    }

                    return result;
                }
                else
                {
                    args.Add($"({context.SetIntermediateVariable(isFloatFlow ? FormulaType.Number : FormulaType.Decimal, coercedArg)})");
                }
            }

            var finalExpression = $"(SELECT {function}(X) FROM (VALUES {string.Join(",", args)}) AS TEMP_{function}(X))";
            context.TryCast(finalExpression, result, castToFloat: isFloatFlow);

            if (zeroNulls)
            {
                context.SetIntermediateVariable(result, CoerceNullToInt(result));
            }
            else if (errorOnNulls)
            {
                context.NullCheck(result, postValidation: true);
            }

            context.PerformRangeChecks(result, node);
            return result;
        }

        public static RetVal Exp(SqlVisitor visitor, CallNode node, Context context)
        {
            var result = context.GetTempVar(FormulaType.Number);
            ValidateNumericArgument(node.Args[0]);
            var arg = node.Args[0].Accept(visitor, context);
            context.PowerOverflowCheck(RetVal.FromSQL("EXP(1)", FormulaType.Number), arg, isFloatFlow: true);
            context.SetIntermediateVariable(result, $"EXP({arg})");
            context.PerformRangeChecks(result, node);
            return result;
        }

        public static RetVal Power(SqlVisitor visitor, CallNode node, Context context)
        {
            // First arg should be of type float or type that can be implicitly converted to float
            // Second arg can be exact numeric or approximate data type
            var result = context.GetTempVar(FormulaType.Number);
            ValidateNumericArgument(node.Args[0]);
            var number = node.Args[0].Accept(visitor, context);
            ValidateNumericArgument(node.Args[1]);
            var exponent = node.Args[1].Accept(visitor, context);
            context.PowerOverflowCheck(number, exponent, isFloatFlow: true);
            context.SetIntermediateVariable(result, $"TRY_CAST(POWER({CoerceNumberToType(number.ToString(), result.Type, context._dataverseFeatures)},{CoerceNullToNumberType(exponent, result.Type, context._dataverseFeatures)}) AS {ToSqlType(result.Type, context._dataverseFeatures)})");
            context.PerformRangeChecks(result, node);
            return result;
        }

        public static RetVal Sqrt(SqlVisitor visitor, CallNode node, Context context)
        {
            var result = context.GetTempVar(FormulaType.Number);
            ValidateNumericArgument(node.Args[0]);
            var arg = node.Args[0].Accept(visitor, context);
            context.NegativeNumberCheck(arg);
            context.SetIntermediateVariable(result, $"SQRT({arg})");
            context.PerformRangeChecks(result, node);
            return result;
        }

        public static RetVal Ln(SqlVisitor visitor, CallNode node, Context context)
        {
            var result = context.GetTempVar(FormulaType.Number);
            ValidateNumericArgument(node.Args[0]);
            var arg = node.Args[0].Accept(visitor, context);
            context.NonPositiveNumberCheck(arg);
            context.SetIntermediateVariable(result, $"LOG({arg})");
            context.PerformRangeChecks(result, node);
            return result;
        }

        public static RetVal RoundUp(SqlVisitor visitor, CallNode node, Context context)
        {
            bool isFloatFlow = node.IRContext.ResultType is NumberType;

            var result = context.GetTempVar(isFloatFlow ? FormulaType.Number : FormulaType.Decimal);
            ValidateNumericArgument(node.Args[0]);
            var number = node.Args[0].Accept(visitor, context);
            ValidateNumericArgument(node.Args[1]);
            var rawDigits = node.Args[1].Accept(visitor, context);

            context.AppendRoundMaxMinConditions(rawDigits);

            // SQL does not implement any version of round that rounds digits less that 5 up, so use ceiling/floor instead
            // the digits should be converted to a whole number, by rounding towards zero
            var digits = context.TryCastToDecimal(RoundDownNullToInt(rawDigits));
            context.PowerOverflowCheck(RetVal.FromSQL("10", FormulaType.Decimal), digits, isFloatFlow: false);
            var factor = context.GetTempVar(FormulaType.Decimal);
            var factorExpression = $"POWER(CAST(10 as {ToSqlType(factor.Type, context._dataverseFeatures)}),{digits})";
            context.TryCastToDecimal(factorExpression, factor);
            context.DivideByZeroCheck(factor);

            // PowerApps rounds up away from zero, so use floor for negative numbers and ceiling for positive
            var finalExpression = $"IIF({CoerceNullToInt(number)}>0,CEILING({CoerceNullToInt(number)}*{factor})/{factor},FLOOR({CoerceNullToInt(number)}*{factor})/{factor})";
            context.TryCast(finalExpression, result, castToFloat: isFloatFlow);
            context.ErrorCheck($"{number} <> 0 AND {CoerceNullToInt(result)} = 0", Context.ValidationErrorCode, postValidation: true);
            context.PerformRangeChecks(result, node);
            return result;
        }

        public static RetVal RoundDown(SqlVisitor visitor, CallNode node, Context context)
        {
            bool isFloatFlow = node.IRContext.ResultType is NumberType;

            ValidateNumericArgument(node.Args[0]);
            var number = node.Args[0].Accept(visitor, context);
            ValidateNumericArgument(node.Args[1]);
            var digits = node.Args[1].Accept(visitor, context);

            context.AppendRoundMaxMinConditions(digits);

            var result = context.GetTempVar(isFloatFlow ? FormulaType.Number : FormulaType.Decimal);

            var expression = $"ROUND({CoerceNullToInt(number)}, {CoerceNullToInt(digits)}, 1)";
            context.TryCast(expression, result, castToFloat: isFloatFlow);

            context.PerformRangeChecks(result, node);
            return result;
        }

        public static RetVal Trunc(SqlVisitor visitor, CallNode node, Context context)
        {
            var result = context.GetTempVar(context.GetReturnType(node));
            ValidateNumericArgument(node.Args[0]);
            var number = node.Args[0].Accept(visitor, context);
            if (node.Args.Count > 1)
            {
                ValidateNumericArgument(node.Args[1]);
                var digits = node.Args[1].Accept(visitor, context);

                context.AppendRoundMaxMinConditions(digits);

                context.SetIntermediateVariable(result, $"ROUND({CoerceNullToInt(number)}, {CoerceNullToInt(digits)}, 1)");
                return result;
            }
            else
            {
                context.SetIntermediateVariable(result, $"ROUND({CoerceNullToInt(number)}, 0, 1)");
                return result;
            }
        }

        /// <summary>
        /// PowerApps Rounds Down towards zero, so 1.4 becomes 1 and -1.4 becomes -1.
        /// </summary>s>
        public static string RoundDownNullToInt(RetVal retVal)
        {
            return RoundDownToInt(RetVal.FromSQL(CoerceNullToInt(retVal), retVal.Type));
        }

        /// <summary>
        /// PowerApps Rounds Down towards zero, so 1.4 becomes 1 and -1.4 becomes -1.
        /// </summary>s>
        public static string RoundDownToInt(RetVal retVal)
        {
            return $"IIF({retVal}<0,CEILING({retVal}),FLOOR({retVal}))";
        }

        /// <summary>
        /// PowerApps Rounds Up away from zero, so 1.4 becomes 2 and -1.4 becomes -2.
        /// </summary>
        /// <param name="retVal"></param>
        /// <returns></returns>
        public static string RoundUpNullToInt(RetVal retVal)
        {
            return RoundUpToInt(RetVal.FromSQL(CoerceNullToInt(retVal), retVal.Type));
        }

        public static string RoundUpToInt(RetVal retVal)
        {
            return $"IIF({retVal}>0,CEILING({retVal}),FLOOR({retVal}))";
        }

        public static string CoerceNullToInt(RetVal retVal)
        {
            return $"ISNULL({retVal},0)";
        }

        public static string CoerceNullToNumberType(RetVal retVal, FormulaType type, DataverseFeatures dataverseFeatures)
        {
            Contracts.Assert(type is DecimalType || type is NumberType);
            return CoerceNumberToType(CoerceNullToInt(retVal), type, dataverseFeatures);
        }

        public static string CoerceNumberToType(string value, FormulaType type, DataverseFeatures dataverseFeatures)
        {
            Contracts.Assert(type is DecimalType || type is NumberType);
            return $"CAST({value} AS {ToSqlType(type, dataverseFeatures)})";
        }

        public static void ValidateNumericArgument(IntermediateNode node)
        {
            // The language allows dates to be treated as numbers (e.g. ticks in JavaScript)
            // Fail if this is a coercion we don't support
            if (!(node.IRContext.ResultType is BlankType || Context.IsNumericType(node.IRContext.ResultType)))
            {
                throw BuildUnsupportedArgumentTypeException(node.IRContext.ResultType._type.GetKindString(), node.IRContext.SourceContext);
            }
        }
    }
}
