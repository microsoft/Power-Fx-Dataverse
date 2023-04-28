using Microsoft.AppMagic.Authoring.Importers.DataDescription;
using Microsoft.AppMagic.Authoring.Importers.ServiceConfig;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Dataverse.DataSource;
using Microsoft.PowerFx.Dataverse.Functions;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BuiltinFunctionsCore = Microsoft.PowerFx.Core.Texl.BuiltinFunctionsCore;
using Span = Microsoft.PowerFx.Syntax.Span;

namespace Microsoft.PowerFx.Dataverse
{
    internal class SqlVisitor : IRNodeVisitor<SqlVisitor.RetVal, SqlVisitor.Context>
    {
        public override RetVal Visit(TextLiteralNode node, Context context)
        {
            var value = node.LiteralValue;
            var returnType = context.GetReturnType(node);
            if (context.InInlineLiteralContext)
            {
                return RetVal.FromSQL(value, returnType);
            }
            else
            {
                return context.SetIntermediateVariable(node , $"N{CrmEncodeDecode.SqlLiteralEncode(value)}");
            }
        }

        public override RetVal Visit(NumberLiteralNode node, Context context)
        {
            var type = context.GetReturnType(node);
            if (context.ValidateNumericLiteral(node.LiteralValue, type))
            {
                var val = RetVal.FromSQL(node.LiteralValue.ToString(), type);

                if (context.InInlineLiteralContext)
                {
                    return val;
                }
                else
                {
                    return context.SetIntermediateVariable(node, fromRetVal: val);
                }
            }
            else
            {
                return RetVal.FromSQL("NULL", type);
            }
        }

        public override RetVal Visit(DecimalLiteralNode node, Context context)
        {
            throw new NotImplementedException();
        }

        public override RetVal Visit(BooleanLiteralNode node, Context context)
        {
            string val = node.LiteralValue ? "1" : "0";
            if (context.InInlineLiteralContext)
            {
                return RetVal.FromSQL(val, context.GetReturnType(node));
            }
            else
            {
                return context.SetIntermediateVariable(node, val);
            }
        }

        public override RetVal Visit(ColorLiteralNode node, Context context)
        {
            throw new SqlCompileException(node.IRContext.SourceContext);
        }

        public override RetVal Visit(RecordNode node, Context context)
        {
            throw new SqlCompileException(SqlCompileException.ResultTypeNotSupported, node.IRContext.SourceContext, node.IRContext.ResultType._type.GetKindString());
        }

        public override RetVal Visit(ErrorNode node, Context context)
        {
            throw new SqlCompileException(node.IRContext.SourceContext);
        }

        public override RetVal Visit(LazyEvalNode node, Context context)
        {
            // The compiler already processes lazy evaluation for If and And, just pass-thru
            return node.Child.Accept(this, context);
        }

        public override RetVal Visit(CallNode node, Context context)
        {
            if (Library.TryLookup(node.Function, out var ptr))
            {
                return ptr(this, node, context);
            }

            // Match against Coalesce(number, 0) for blank coercion            
            if (Library.TryCoalesceNum(this, node, context, out var ret))
            {
                return ret;
            }            

            throw new SqlCompileException(node.IRContext.SourceContext);
        }

        public override RetVal Visit(BinaryOpNode node, Context context)
        {
            switch (node.Op)
            {
                case BinaryOpKind.AddNumbers:
                case BinaryOpKind.DivNumbers:
                case BinaryOpKind.MulNumbers:
                    {
                        var op = node.Op switch
                        {
                            BinaryOpKind.AddNumbers => "+",
                            BinaryOpKind.DivNumbers => "/",
                            BinaryOpKind.MulNumbers => "*"
                        };

                        Library.ValidateNumericArgument(node.Left);
                        Library.ValidateNumericArgument(node.Right);
                        var left = node.Left.Accept(this, context);
                        var right = node.Right.Accept(this, context);

                        // protect from divide by zero
                        if (node.Op == BinaryOpKind.DivNumbers)
                        {
                            context.DivideByZeroCheck(right);
                        }

                        var returnType = new SqlBigType();
                        var decimalType = new SqlDecimalType();
                        // Casting to decimal to preserve 10 precision places while ensuring no overflow for max int value math
                        // Docs: https://learn.microsoft.com/en-us/sql/t-sql/data-types/precision-scale-and-length-transact-sql?view=sql-server-ver15
                        var result = context.SetIntermediateVariable(returnType, $"({Library.CoerceNullToNumberType(left, decimalType)} {op} {Library.CoerceNullToNumberType(right, decimalType)})");
                        context.PerformRangeChecks(result, node);

                        return result;
                    }

                case BinaryOpKind.AddDateAndDay:
                case BinaryOpKind.AddDateTimeAndDay:
                    {
                        var date = node.Left.Accept(this, context);
                        var offset = node.Right.Accept(this, context);
                        return Library.AddDays(node, date, offset, context);
                    }

                case BinaryOpKind.InText:
                case BinaryOpKind.ExactInText:
                    {
                        // Reverse the argument order
                        // - The in operator and SQL LIKE have opposite definitions ("x" in "xx" vs. "xx" LIKE "%x%")
                        var arg1 = node.Right.Accept(this, context);
                        var match = EncodeLikeArgument(node.Left, MatchType.Inner, context);
                        if (node.Op == BinaryOpKind.ExactInText)
                        {
                            // TODO: what collation should this be performed with?  The database collation, the user's collation, the column collation, etc.
                            // hardcode with default for now
                            match += $" {SqlStatementFormat.CollateString}";
                        }

                        var arg2 = RetVal.FromSQL(match, FormulaType.String);
                        return context.SetIntermediateVariable(node, $"({Library.CoerceNullToString(arg1)} LIKE {arg2})");
                    }

                //case BinaryOpKind.EqBlob:
                case BinaryOpKind.EqBoolean:
                //case BinaryOpKind.EqColor:
                case BinaryOpKind.EqCurrency:
                case BinaryOpKind.EqDate:
                case BinaryOpKind.EqDateTime:
                case BinaryOpKind.EqGuid:
                case BinaryOpKind.EqHyperlink:
                //case BinaryOpKind.EqImage:
                //case BinaryOpKind.EqMedia:
                case BinaryOpKind.EqNumbers:
                case BinaryOpKind.EqOptionSetValue:
                case BinaryOpKind.EqText:
                //case BinaryOpKind.EqTime:
                    return EqualityCheck(node.Left, node.Right, node.Op, context.GetReturnType(node), context, node.IRContext.SourceContext);

                //case BinaryOpKind.NeqBlob:
                case BinaryOpKind.NeqBoolean:
                //case BinaryOpKind.NeqColor:
                case BinaryOpKind.NeqCurrency:
                case BinaryOpKind.NeqDate:
                case BinaryOpKind.NeqDateTime:
                case BinaryOpKind.NeqGuid:
                case BinaryOpKind.NeqHyperlink:
                //case BinaryOpKind.NeqImage:
                //case BinaryOpKind.NeqMedia:
                case BinaryOpKind.NeqNumbers:
                case BinaryOpKind.NeqOptionSetValue:
                case BinaryOpKind.NeqText:
                //case BinaryOpKind.NeqTime:
                    return EqualityCheck(node.Left, node.Right, node.Op, context.GetReturnType(node), context, node.IRContext.SourceContext, equals: false);

                case BinaryOpKind.EqNull:
                    return BinaryOperation(node.Left, node.Right, context.GetReturnType(node), "IS", context, node.IRContext.SourceContext);
                case BinaryOpKind.NeqNull:
                    return BinaryOperation(node.Left, node.Right, context.GetReturnType(node), "IS NOT", context, node.IRContext.SourceContext);

                case BinaryOpKind.GtDate:
                case BinaryOpKind.GtDateTime:
                //case BinaryOpKind.GtTime:
                    return BinaryOperation(node.Left, node.Right, context.GetReturnType(node), ">", context, node.IRContext.SourceContext);

                case BinaryOpKind.GtNumbers:
                    return BinaryNumericOperation(node.Left, node.Right, context.GetReturnType(node), ">", context);

                case BinaryOpKind.GeqDate:
                case BinaryOpKind.GeqDateTime:
                //case BinaryOpKind.GeqTime:
                    return BinaryOperation(node.Left, node.Right, context.GetReturnType(node), ">=", context, node.IRContext.SourceContext);

                case BinaryOpKind.GeqNumbers:
                    return BinaryNumericOperation(node.Left, node.Right, context.GetReturnType(node), ">=", context);

                case BinaryOpKind.LtDate:
                case BinaryOpKind.LtDateTime:
                //case BinaryOpKind.LtTime:
                    return BinaryOperation(node.Left, node.Right, context.GetReturnType(node), "<", context, node.IRContext.SourceContext);

                case BinaryOpKind.LtNumbers:
                    return BinaryNumericOperation(node.Left, node.Right, context.GetReturnType(node), "<", context);

                case BinaryOpKind.LeqDate:
                case BinaryOpKind.LeqDateTime:
                //case BinaryOpKind.LeqTime:
                    return BinaryOperation(node.Left, node.Right, context.GetReturnType(node), "<=", context, node.IRContext.SourceContext);

                case BinaryOpKind.LeqNumbers:
                    return BinaryNumericOperation(node.Left, node.Right, context.GetReturnType(node), "<=", context);

                case BinaryOpKind.SubtractNumberAndDate:
                case BinaryOpKind.SubtractNumberAndDateTime:
                case BinaryOpKind.SubtractNumberAndTime:
                    throw new SqlCompileException(SqlCompileException.ArgumentTypeNotSupported, node.Right.IRContext.SourceContext, context.GetReturnType(node.Right)._type.GetKindString());

                default:
                    throw new SqlCompileException(SqlCompileException.OperationNotSupported, node.IRContext.SourceContext, context.GetReturnType(node.Left)._type.GetKindString());
            }
        }

        private RetVal EqualityCheck(IntermediateNode left, IntermediateNode right, BinaryOpKind op, FormulaType type, Context context, Span sourceContext = default, bool equals = true)
        {
            // Don't do any null coercions for not equals checks, but check for date to number coercion
            if (op == BinaryOpKind.EqNumbers || op == BinaryOpKind.NeqNumbers)
            {
                Library.ValidateNumericArgument(left);
                Library.ValidateNumericArgument(right);
            }

            var leftVal = left.Accept(this, context);
            var rightVal = right.Accept(this, context);
            Library.ValidateTypeCompatibility(leftVal, rightVal, sourceContext);

            // SQL does not allow simple equality checks for null (equals and not equals with a null both return false)
            if (equals)
            {
                return context.SetIntermediateVariable(type, $"(({leftVal} IS NULL AND {rightVal} IS NULL) OR ({leftVal} = {rightVal}))");
            }
            else
            {
                return context.SetIntermediateVariable(type, $"((({leftVal} IS NULL AND {rightVal} IS NOT NULL) OR ({leftVal} IS NOT NULL AND {rightVal} IS NULL)) OR ({leftVal} <> {rightVal}))");
            }
        }

        /// <summary>
        /// Helper function to generate a binary operation
        /// </summary>
        /// <param name="left">The left node</param>
        /// <param name="right">The right node</param>
        /// <param name="type">The return type</param>
        /// <param name="op">The SQL operation string</param>
        /// <param name="context">The context</param>
        /// <returns></returns>
        private RetVal BinaryOperation(IntermediateNode left, IntermediateNode right, FormulaType type, string op, Context context, Span sourceContext = default)
        {
            var leftVal = left.Accept(this, context);
            var rightVal = right.Accept(this, context);
            Library.ValidateTypeCompatibility(leftVal, rightVal, sourceContext);
            return context.SetIntermediateVariable(type, $"({leftVal} {op} {rightVal})");
        }

        /// <summary>
        /// Helper function to generate a numeric binary operation, that includes coorcing nulls to zero
        /// </summary>
        /// <param name="left">The left node</param>
        /// <param name="right">The right node</param>
        /// <param name="type">The return type</param>
        /// <param name="op">The SQL operation string</param>
        /// <param name="context">The context</param>
        /// <returns></returns>
        private RetVal BinaryNumericOperation(IntermediateNode left, IntermediateNode right, FormulaType type, string op, Context context)
        {
            Library.ValidateNumericArgument(left);
            Library.ValidateNumericArgument(right);
            var leftVal = left.Accept(this, context);
            var rightVal = right.Accept(this, context);
            return context.SetIntermediateVariable(type, $"({Library.CoerceNullToInt(leftVal)} {op} {Library.CoerceNullToInt(rightVal)})");
        }

        public override RetVal Visit(UnaryOpNode node, Context context)
        {
            RetVal arg;
            switch (node.Op)
            {
                case UnaryOpKind.Negate:
                    Library.ValidateNumericArgument(node.Child);
                    arg = node.Child.Accept(this, context);
                    return context.SetIntermediateVariable(new SqlBigType(), $"(-{Library.CoerceNullToInt(arg)})");

                case UnaryOpKind.Percent:
                    arg = node.Child.Accept(this, context);
                    var result = context.SetIntermediateVariable(new SqlBigType(), $"({Library.CoerceNullToInt(arg)}/100)");
                    context.PerformRangeChecks(result, node);
                    return result;

                // Coercions

                case UnaryOpKind.BooleanToText:
                case UnaryOpKind.BooleanToNumber:
                    arg = node.Child.Accept(this, context);
                    var boolResult = CoerceBooleanToOp(node, arg, context);

                    return node.Op switch
                    {
                        UnaryOpKind.BooleanToText => RetVal.FromSQL(context.WrapInlineBoolean(boolResult.ToString(), "N'true'", "N'false'"), context.GetReturnType(node)),
                        _ => RetVal.FromSQL(context.WrapInlineBoolean(boolResult.ToString(), "1", "0"), context.GetReturnType(node))
                    };

                case UnaryOpKind.NumberToBoolean:
                    arg = node.Child.Accept(this, context);
                    return context.SetIntermediateVariable(node, $"{Library.CoerceNullToInt(arg)}<>0");

                case UnaryOpKind.NumberToText:
                    arg = node.Child.Accept(this, context);
                    {
                        // Can only convert whole numbers.
                        // If it's a literal, provide a compile-time error (rather than runtime).

                        var node2 = node.Child;
                        if (node2 is UnaryOpNode unary && unary.Op == UnaryOpKind.Negate)
                        {
                            // -X is Unary(negate,X). 
                            node2 = unary.Child;
                        }

                        if (node2 is NumberLiteralNode number)
                        {
                            // Non integer 
                            if (number.LiteralValue != (int)number.LiteralValue)
                            {
                                context._unsupportedWarnings.Add("Can't convert non-integer to text");
                            }
                        }                        
                    }                    
                    var str = CoerceNumberToString(arg, context);
                    return context.SetIntermediateVariable(node, fromRetVal: str);

                case UnaryOpKind.TextToBoolean:
                    arg = node.Child.Accept(this, context);
                    var coercedArg = RetVal.FromSQL($"IIF({arg} IS NULL OR {arg} = N'', N'false', {arg})", FormulaType.String);
                    context.ErrorCheck($"{coercedArg} <> N'true' AND {coercedArg} <> N'false'", Context.ValidationErrorCode);
                    return context.SetIntermediateVariable(node, $"{coercedArg} = N'true'");

                case UnaryOpKind.OptionSetToBoolean:
                    // converting a boolean option set to a boolean is a noop
                    return node.Child.Accept(this, context);

                case UnaryOpKind.BlankToEmptyString:
                    arg = node.Child.Accept(this, context);
                    return context.SetIntermediateVariable(node, $"IIF({arg} IS NULL, N'', {arg})");

                case UnaryOpKind.DateTimeToNumber:
                case UnaryOpKind.DateTimeToTime:
                case UnaryOpKind.DateTimeToDate:
                case UnaryOpKind.DateToDateTime:
                case UnaryOpKind.DateToNumber:
                case UnaryOpKind.DateToTime:
                case UnaryOpKind.NumberToDate:
                case UnaryOpKind.NumberToDateTime:
                case UnaryOpKind.NumberToTime:
                case UnaryOpKind.TimeToDate:
                case UnaryOpKind.TimeToDateTime:
                case UnaryOpKind.TimeToNumber:
                    throw Library.BuildUnsupportedArgumentTypeException(node.IRContext.ResultType._type.GetKindString(), node.Child.IRContext.SourceContext);

                case UnaryOpKind.OptionSetToText:
                    if (node.Child is RecordFieldAccessNode fieldNode)
                    {
                        string name = fieldNode.Field.ToString();
                        return context.SetIntermediateVariable(node, $"N{CrmEncodeDecode.SqlLiteralEncode(name)}");
                    }
                    goto default;

                // TODO: other coorcions as new type support is added
                default:
                    throw new SqlCompileException(node.IRContext.SourceContext);
            }
        }

        public override RetVal Visit(ScopeAccessNode node, Context context)
        {
            if (node.Value is ScopeAccessSymbol scopeAccess)
            {
                var varDetails = context.GetVarDetails(scopeAccess, node.IRContext.SourceContext);
                return RetVal.FromVar(varDetails.VarName, context.GetReturnType(node, varDetails.VarType));
            }
            // TODO: handle direct scope access, like entities for roll-ups
            throw new SqlCompileException(SqlCompileException.RecordAccessNotSupported, node.IRContext.SourceContext);
        }

        public override RetVal Visit(RecordFieldAccessNode node, Context context)
        {
            if (node.From is ScopeAccessNode scopeNode && scopeNode.Value is ScopeAccessSymbol scopeSymbol)
            {
                // This is a lookup
                // Don't actually visit the From node, just extract the lookup details
                var lookup = context.GetScopeColumn(scopeSymbol);
                if (lookup.IsNavigation && lookup.TypeDefinition is CdsNavigationTypeDefinition scopeNavType)
                {
                    // add the referencing attribute to the list of used fields, a well as for chains of lookups
                    var scope = context.GetScope(scopeSymbol.Parent);
                    context.GetVarName(scopeNavType.ReferencingFieldName, scope, node.IRContext.SourceContext);

                    // get the referenced field
                    var path = new DPath().Append(new DName(scopeNavType.PropertyName)).Append(node.Field);
                    var column = scope.GetColumn(path, navigation: scopeNavType);

                    // virtual tables can not be referenced from SQL
                    var table = scope.GetTable(scopeNavType.TargetTableNames[0]);
                    if (table.IsVirtual)
                    {
                        throw new SqlCompileException(SqlCompileException.VirtualTableNotSupported, node.IRContext.SourceContext, table.DisplayName);
                    }

                    // if the referenced field is a navigation, build a path to refer to the referencing attribute
                    if (column.IsNavigation && column.TypeDefinition is CdsNavigationTypeDefinition fieldNavType)
                    {
                        path = path.Parent.Append(new DName(fieldNavType.ReferencingFieldName));
                    }

                    var varName = context.GetVarName(path, scope, node.IRContext.SourceContext, scopeNavType);
                    return RetVal.FromVar(varName, context.GetReturnType(node));
                }
            }
            else if (node.From is RecordFieldAccessNode)
            {
                // This is a chain of lookups, first visit the From to unroll the chain
                var parent = node.From.Accept(this, context);
                var parentVar = context.GetVarDetails(parent.varName);

                var lookup = parentVar.Column;
                if (lookup.IsNavigation && lookup.TypeDefinition is CdsNavigationTypeDefinition scopeNavType)
                {
                    // the parent var will be the actual referencing Guid.  Create a path with the actual nav prop instead
                    var path = parentVar.Path.Parent.Append(new DName(scopeNavType.PropertyName)).Append(node.Field);
                    var column = parentVar.Scope.GetColumn(path, navigation: scopeNavType);

                    // if this is another lookup in the chain, return the referencing Guid.
                    if (column.IsNavigation && column.TypeDefinition is CdsNavigationTypeDefinition innerNavType)
                    {
                        path = path.Parent.Append(new DName(innerNavType.ReferencingFieldName));
                    }

                    string varName = context.GetVarName(path, parentVar.Scope, node.IRContext.SourceContext, scopeNavType);
                    return RetVal.FromVar(varName, context.GetReturnType(node));
                }
            }
            else if (node.From is ResolvedObjectNode resolvedObjectNode1 && resolvedObjectNode1.Value is DataverseOptionSet)
            {
                // This is an option set, which is treated as a record access
                // the node Field will be a DName of the option set value
                return context.SetIntermediateVariable(node, node.Field.Value);
            }

            // TODO: handle other types of records
            throw new SqlCompileException(SqlCompileException.ResultTypeNotSupported, node.IRContext.SourceContext, node.From.IRContext.ResultType._type.GetKindString());
        }

        public override RetVal Visit(ResolvedObjectNode node, Context context)
        {
            throw new SqlCompileException(node.IRContext.SourceContext);
        }

        public override RetVal Visit(SingleColumnTableAccessNode node, Context context)
        {
            throw new SqlCompileException(SqlCompileException.SingleColumnTableNotSupported, node.IRContext.SourceContext);
        }

        public override RetVal Visit(ChainingNode node, Context context)
        {
            throw new SqlCompileException(node.IRContext.SourceContext);
        }

        public override RetVal Visit(AggregateCoercionNode node, Context context)
        {
            throw new SqlCompileException(SqlCompileException.AggregateCoercionNotSupported, node.IRContext.SourceContext);
        }

        internal enum MatchType
        {
            Prefix,
            Suffix,
            Inner
        }

        internal string EncodeLikeArgument(IntermediateNode node, MatchType matchType, Context context)
        {
            if (node is TextLiteralNode)
            {
                // use an inline literal context to get the like argument as a raw string to properly format the SQL parameter
                using (context.NewInlineLiteralContext())
                {
                    var arg = node.Accept(this, context);
                    var match = arg.ToString();

                    // encode SQL placeholders and quotes
                    match = match.Replace("_", "[_]");
                    match = match.Replace("[", "[[]");
                    match = match.Replace("%", "[%]");
                    match = match.Replace("'", "''");

                    // append start and end wildcards, as needed
                    var startWildcard = matchType != MatchType.Prefix ? "%" : "";
                    var endWildcard = matchType != MatchType.Suffix ? "%" : "";
                    return $"N'{startWildcard}{match}{endWildcard}'";
                }
            }
            else if (node is CallNode callNode && callNode.Function == BuiltinFunctionsCore.Blank)
            {
                return "N'%'";
            }
            else
            {
                // TODO: allow runtime encoding of string inputs
                context._unsupportedWarnings.Add("Only string literals allowed");
                throw Library.BuildLiteralArgumentException(node.IRContext.SourceContext);
            }
        }

        internal RetVal CoerceBooleanToOp(IntermediateNode node, RetVal result, Context context)
        {
            // SQL does not allow boolean literals or boolean variables as a logical operation
            if (node is BooleanLiteralNode || (node as LazyEvalNode)?.Child is BooleanLiteralNode)
            {
                return RetVal.FromSQL($"({result}=1)", FormulaType.Boolean);
            }
            else if (result.type is BlankType)
            {
                return RetVal.FromSQL("(0=1)", FormulaType.Boolean);
            }
            else if (result.varName != null && result.type is BooleanType)
            {
                return RetVal.FromSQL($"({Library.CoerceNullToInt(result)}=1)", FormulaType.Boolean);
            }
            else
            {
                return result;
            }
        }

        internal RetVal CoerceNumberToString(RetVal result, Context context)
        {
            context.FloatingPointErrorCheck(result);
            return RetVal.FromSQL($"(ISNULL(FORMAT({result},N'0'),''))", FormulaType.String);
        }

        public static string ToSqlType(FormulaType t)
        {
            // handle all numeric subtypes first
            if (t is SqlNumberBase sqlNumber)
            {
                return sqlNumber.ToSqlType();
            }
            else if (t is NumberType)
            {
                // use big as the numeric type if not already resolved (e.g. local variables)
                return new SqlBigType().ToSqlType();
            }
            else if (t is SqlMoneyType)
            {
                return SqlStatementFormat.SqlMoneyType;
            }
            else if (t is StringType || t is HyperlinkType)
            {
                return SqlStatementFormat.SqlNvarcharType;
            }
            else if (t is BooleanType)
            {
                return SqlStatementFormat.SqlBitType;
            }
            else if (t is GuidType || t is RecordType)
            {
                return SqlStatementFormat.SqlUniqueIdentifierType;
            }
            else if (Library.IsDateTimeType(t))
            {
                return SqlStatementFormat.SqlDateTimeType;
            }
            else if (t is OptionSetValueType)
            {
                return SqlStatementFormat.SqlIntegerType;
            }
            else if (t is BlankType)
            {
                return "nvarchar"; // TODO: what type should be used to a null variable?
            }
            throw new SqlCompileException(default);
        }

        internal class RetVal
        {
            public string varName;
            public string inlineSQL;
            public FormulaType type;

            private RetVal(string varName, string inlineSQL, FormulaType type)
            {
                this.varName = varName;
                this.inlineSQL = inlineSQL;
                this.type = type;
            }

            public static RetVal FromVar(string varName, FormulaType type)
            {
                return new RetVal(varName, null, type);
            }

            public static RetVal FromSQL(string SQL, FormulaType type)
            {
                return new RetVal(null, SQL, type);
            }

            public override string ToString()
            {
                return inlineSQL ?? varName;
            }
        }

        internal class Context
        {
            internal class VarDetails
            {
                /// <summary>
                /// The index of the var
                /// </summary>
                public int Index;

                /// <summary>
                /// The name of the variable
                /// </summary>
                public string VarName;

                /// <summary>
                /// The type of the variable
                /// </summary>
                public FormulaType VarType;

                /// <summary>
                /// The backing field attribute metadata, will be null for temp variables
                /// </summary>
                public CdsColumnDefinition Column;

                /// <summary>
                /// The navigation type, for lookup fields
                /// </summary>
                public CdsNavigationTypeDefinition Navigation;

                /// <summary>
                /// The logical name of the table for the field
                /// </summary>
                public string Table;

                /// <summary>
                /// The field's scope
                /// </summary>
                public Scope Scope;

                /// <summary>
                /// The path to the field
                /// </summary>
                public DPath Path;
            }

            // Mapping of field names to details
            private Dictionary<string, VarDetails> _fields = new Dictionary<string, VarDetails>();

            // Mapping of var names to details
            private Dictionary<string, VarDetails> _vars = new Dictionary<string, VarDetails>();

            internal class Scope : DataverseType
            {
                /// <summary>
                /// The ScopeSymbol for the scope
                /// </summary>
                public ScopeSymbol Symbol;
            }

            internal readonly List<string> _unsupportedWarnings = new List<string>();

            // the known scope root records, indexed by scope id
            private readonly Dictionary<int, Scope> _scopes = new Dictionary<int, Scope>();

            internal readonly IntermediateNode RootNode;

            internal readonly Scope RootScope;

            /// <summary>
            /// A flag to indicate that the compliation is just validate SQL functionality, and shouldn't generate the full SQL function
            /// </summary>
            private bool _checkOnly;

            public Context(IntermediateNode rootNode, ScopeSymbol rootScope, DType rootType, bool checkOnly = false)
            {
                RootNode = rootNode;
                _checkOnly = checkOnly;

                RootScope = new Scope
                {
                    Symbol = rootScope,
                    Type = rootType
                };

                _scopes[rootScope.Id] = RootScope;

                DoesDateDiffOverflowCheck = false;
            }

            public bool DoesDateDiffOverflowCheck { get; internal set; }

            /// <summary>
            /// Get temp variable details
            /// </summary>
            /// <returns>List of tuples of variable name and type</returns>
            public IEnumerable<Tuple<string, FormulaType>> GetTemps()
            {
                foreach (var pair in _vars)
                {
                    // a variable is temporary if it has no backing attribute
                    if (pair.Value.Column == null)
                    {
                        yield return Tuple.Create(pair.Key, pair.Value.VarType);
                    }
                }
            }

            /// <summary>
            /// Get calculated field variable details
            /// </summary>
            /// <returns>List of tuples of calculated field variable name, field name, and type</returns>
            public IEnumerable<VarDetails> GetReferenceFields()
            {
                foreach (var pair in _vars)
                {
                    if (IsReferenceField(pair.Value))
                    {
                        yield return pair.Value;
                    }
                }
            }

            public bool IsReferenceField(VarDetails field)
            {
                return field.Column != null && (field.Column.RequiresReference() || field.Navigation != null);
            }

            /// <summary>
            /// Get parameter fields
            /// </summary>
            /// <returns>List of tuples of parameter field name and type</returns>
            public IEnumerable<Tuple<CdsColumnDefinition, FormulaType>> GetParameters()
            {
                foreach (var pair in _fields)
                {
                    if (!IsReferenceField(pair.Value))
                    {
                        yield return Tuple.Create(pair.Value.Column, pair.Value.VarType);
                    }
                }
            }

            public Dictionary<string, HashSet<string>> GetDependentFields()
            {
                var dependentFields = new Dictionary<string, HashSet<string>>();
                foreach (var pair in _fields)
                {
                    if (!(pair.Value.VarType is GuidType))
                    {
                        if (!dependentFields.ContainsKey(pair.Value.Table))
                        {
                            dependentFields[pair.Value.Table] = new HashSet<string>();
                        }
                        // Add by logical name
                        dependentFields[pair.Value.Table].Add(pair.Value.Column.LogicalName);
                    }
                }
                return dependentFields;
            }

            public Dictionary<string, HashSet<string>> GetDependentRelationships()
            {
                var dependentRelationships = new Dictionary<string, HashSet<string>>();
                foreach (var pair in _fields)
                {
                    if (pair.Value.Navigation != null)
                    {
                        if (!dependentRelationships.TryGetValue(pair.Value.Navigation.ReferencingTableName, out var rels))
                        {
                            rels = new HashSet<string>();
                            dependentRelationships.Add(pair.Value.Navigation.ReferencingTableName, rels);
                        }
                        rels.Add(pair.Value.Navigation.SchemaName);
                    }
                }
                return dependentRelationships;
            }

            public RetVal GetTempVar(FormulaType type)
            {
                // TODO: do we want to use the "t" prefix for temp variables?
                var idx = _vars.Count;
                var varName = "@v" + idx;
                _vars.Add(varName, new VarDetails { Index = idx, VarName = varName, VarType = type });
                return RetVal.FromVar(varName, type);
            }

            public VarDetails GetVarDetails(ScopeAccessSymbol scopeAccess, Span sourceContext)
            {
                return GetVarDetails(new DPath().Append(scopeAccess.Name), GetScope(scopeAccess.Parent), sourceContext);
            }

            // "new_Field" --> "@v0"
            public string GetVarName(string fieldName, Scope scope, Span sourceContext, CdsNavigationTypeDefinition navigation = null, bool create = true)
            {
                return GetVarDetails(new DPath().Append(new DName(fieldName)), scope, sourceContext, navigation, create).VarName;
            }

            public string GetVarName(DPath path, Scope scope, Span sourceContext, CdsNavigationTypeDefinition navigation = null, bool create = true)
            {
                return GetVarDetails(path, scope, sourceContext, navigation, create).VarName;
            }

            public VarDetails GetVarDetails(string varName)
            {
                return _vars[varName];
            }

            private VarDetails GetVarDetails(DPath path, Scope scope, Span sourceContext, CdsNavigationTypeDefinition navigation = null, bool create = true)
            {
                var key = $"{scope.Symbol.Id}.{path.ToDottedSyntax()}";

                if (_fields.TryGetValue(key, out var details))
                {
                }
                else
                {
                    if (!create)
                    {
                        throw new Exception($"Variable not found for field '{path.ToDottedSyntax()}'");
                    }
                    var idx = _vars.Count;
                    var varName = "@v" + idx;
                    // resolve the column against the navigation target, or the current entity
                    var column = scope.GetColumn(path, navigation: navigation);

                    var table = navigation == null ? scope.Type.AssociatedDataSources.First().Name : navigation.TargetTableNames[0];

                    details = new VarDetails { Index = idx, VarName = varName, Column = column, VarType = GetFormulaType(column, sourceContext), Navigation = navigation, Table = table, Scope = scope, Path = path };
                    _vars.Add(varName, details);
                    _fields.Add(key, details);

                    if (column.RequiresReference())
                    {
                        // the first time a calculated or logical field is referenced, add a var for the primary id for the table
                        var parentPath = path.Parent;
                        var primaryKey = scope.Type.GetType(parentPath).CdsTableDefinition().PrimaryKeyColumn;
                        var primaryKeyPath = parentPath.Append(new DName(primaryKey));
                        GetVarName(primaryKeyPath, scope, sourceContext, navigation);
                    }

                    // initialize a string variable to the empty string
                    // if (details.VarType is StringType)
                    // {
                    // AppendContentLine($"IF({varName} IS NULL) BEGIN SET {varName} = N'' END");
                    // }
                }
                return details;
            }

            public FormulaType GetFormulaType(CdsColumnDefinition column, Span sourceContext)
            {
                var dkind = column.TypeDefinition.DKind;
                switch (dkind)
                {
                    case DKind.Number:
                        // formatted integer types are not supported
                        if (column.TypeCode == AttributeTypeCode.Integer && column.FormatName != null && column.FormatName != IntegerFormat.None.ToString())
                        {
                            throw new SqlCompileException(SqlCompileException.ColumnFormatNotSupported, sourceContext, column.TypeCode, column.FormatName);
                        }

                        // don't allow floating point types
                        if (column.TypeCode != AttributeTypeCode.Double && column.TypeCode.Value.TryGetFormulaType(out var numberType))
                        {
                            return numberType;
                        }
                        else
                        {
                            throw new SqlCompileException(SqlCompileException.ColumnTypeNotSupported, sourceContext, column.TypeCode);
                        }

                    case DKind.DataEntity:
                        // TODO: in the long run, we will need to convert the data entity into a Record type with proper data source backing
                        return FormulaType.Build(DType.EmptyRecord);

                    case DKind.Guid:
                        return new GuidType();

                    case DKind.OptionSet:
                        return column.TypeCode == AttributeTypeCode.Boolean ? FormulaType.Boolean : FormulaType.OptionSetValue;

                    case DKind.DateTimeNoTimeZone:
                    case DKind.DateTime:
                        if (column.TypeDefinition is JsonDateTimeStringTypeDefinition dateTimeType)
                        {
                            if (dateTimeType.Behavior == AppMagic.Authoring.Importers.ServiceConfig.DateTimeBehavior.NoTimeZone)
                            {
                                return FormulaType.DateTimeNoTimeZone;
                            }
                            else
                            {
                                return FormulaType.DateTime;
                            }
                        }
                        throw new SqlCompileException(sourceContext);

                    case DKind.Date:
                        return FormulaType.Date;

                    // These column types aren't support  in SQL compilation.                    
                    case DKind.Image: 
                        throw new SqlCompileException(SqlCompileException.ColumnTypeNotSupported, sourceContext, column.TypeCode);

                    default:
                        if (!column.DType.HasValue)
                        {
                            throw new SqlCompileException(SqlCompileException.ColumnTypeNotSupported, sourceContext, column.TypeCode);
                        }

                        var type = PowerFx2SqlEngine.BuildReturnType(column.DType.Value.ToDType());

                        // formatted string types are not supported
                        if ((type == FormulaType.String && column.TypeCode == AttributeTypeCode.String && column.FormatName != StringFormat.Text.ToString()) ||
                            type == FormulaType.Hyperlink)
                        {
                            throw new SqlCompileException(SqlCompileException.ColumnFormatNotSupported, sourceContext, column.TypeCode, column.FormatName);
                        }
                        return type;
                }
            }

            public FormulaType GetReturnType(IntermediateNode node, FormulaType sourceType = null)
            {
                // if we have a node source type, use it
                if (sourceType != null)
                {
                    return sourceType;
                }

                // otherwise, translate existing binding node type to SQL
                return PowerFx2SqlEngine.BuildReturnType(node.IRContext.ResultType);
            }

            public Scope GetScope(ScopeSymbol symbol)
            {
                if (_scopes.TryGetValue(symbol.Id, out var scope))
                {
                    return scope;
                }
                throw new SqlCompileException(default);
            }

            public CdsColumnDefinition GetScopeColumn(ScopeAccessSymbol scopeAccess)
            {
                var scope = GetScope(scopeAccess.Parent);
                var path = new DPath().Append(scopeAccess.Name);
                return scope.GetColumn(path);
            }

            #region SQL Building

            internal static string GetErrorCode(ErrorKind kind)
            {
                return ((int)kind).ToString();
            }
            internal static string ValidationErrorCode => GetErrorCode(ErrorKind.Validation);
            internal static string Div0ErrorCode => GetErrorCode(ErrorKind.Div0);
            internal static string InvalidArgumentErrorCode => GetErrorCode(ErrorKind.InvalidArgument);

            internal StringBuilder _sbContent = new StringBuilder();
            int _indentLevel = 1;

            // TODO: make this private so it is only called from other higher level functions
            internal void AppendContentLine(string content, bool skipEmittingElse = false)
            {
                if (_checkOnly) return;

                // if there is a pending else on the error context from a post-set validation, emit it before adding any other context
                if (InErrorContext && CurrentErrorContext?.PostValidationElse == true)
                {
                    CurrentErrorContext.PostValidationElse = false;
                    // skip it if we are closing an if result
                    if (!skipEmittingElse)
                    {
                        AppendContentLine("END ELSE BEGIN");
                    }
                }
                var indent = "    ";
                for (int i = 0; i < _indentLevel; i++)
                {
                    _sbContent.Append(indent);
                }
                _sbContent.AppendLine(content);
            }

            internal string WrapInlineBoolean(string conditionClause, string trueValue = "1", string falseValue = "0")
            {
                return $"IIF({conditionClause}, {trueValue}, {falseValue})";
            }

            internal RetVal SetIntermediateVariable(RetVal retVal, string value = null, RetVal fromRetVal = null)
            {
                Contracts.AssertNonEmptyOrNull(retVal.varName);
                // assert only one of value and fromRetVal should be non-null
                Contracts.Assert(value == null || fromRetVal == null);
                Contracts.Assert(value != null || fromRetVal != null);

                if (!_checkOnly)
                {
                    // otherwise, if assigning a boolean from inline SQL, but not assigning from a bit literal, convert from expression to boolean value
                    var inlineSql = value ?? fromRetVal.inlineSQL;
                    if (retVal.type is BooleanType && inlineSql != null && value != "1" && inlineSql != "0" && inlineSql != "NULL")
                    {
                        inlineSql = WrapInlineBoolean(inlineSql);
                    }

                    AppendContentLine(string.Format(CultureInfo.InvariantCulture, SqlStatementFormat.SetValueFormat, retVal.varName, inlineSql ?? fromRetVal.varName));
                }
                return retVal;
            }


            internal RetVal SetIntermediateVariable(IntermediateNode node, string value = null, RetVal fromRetVal = null)
            {
                return SetIntermediateVariable(fromRetVal?.type ?? GetReturnType(node), value, fromRetVal);
            }

            internal RetVal SetIntermediateVariable(FormulaType type, string value = null, RetVal fromRetVal = null)
            {
                return SetIntermediateVariable(GetTempVar(type), value, fromRetVal);
            }

            internal RetVal SelectIntermediateVariable(RetVal retVal, string value)
            {
                Contracts.AssertNonEmptyOrNull(retVal.varName);
                if (!_checkOnly)
                {
                    AppendContentLine(string.Format(CultureInfo.InvariantCulture, "SELECT {0}={1}", retVal.varName, value));
                }
                return retVal;
            }

            internal void DivideByZeroCheck(RetVal retVal, bool coerce = true)
            {
                if (_checkOnly) return;

                var condition = 
                    coerce
                    ? string.Format(CultureInfo.InvariantCulture, SqlStatementFormat.DivideByZeroCoerceCondition, retVal)
                    : string.Format(CultureInfo.InvariantCulture, SqlStatementFormat.DivideByZeroCondition, retVal);

                ErrorCheck(condition, Div0ErrorCode);
            }

            internal void NegativeNumberCheck(RetVal retVal)
            {
                if (_checkOnly) return;

                var condition = string.Format(CultureInfo.InvariantCulture, SqlStatementFormat.NegativeNumberCondition, retVal);
                ErrorCheck(condition, ValidationErrorCode);
            }

            internal void NonPositiveNumberCheck(RetVal retVal)
            {
                if (_checkOnly) return;

                var condition = string.Format(CultureInfo.InvariantCulture, SqlStatementFormat.NonPositiveNumberCondition, retVal);
                ErrorCheck(condition, ValidationErrorCode);
            }

            internal void LessThanOneNumberCheck(RetVal retVal)
            {
                if (_checkOnly) return;

                var condition = string.Format(CultureInfo.InvariantCulture, SqlStatementFormat.LessThanOneNumberCondition, retVal);
                ErrorCheck(condition, ValidationErrorCode);
            }

            internal void NullCheck(RetVal retVal, bool postValidation = false)
            {
                if (_checkOnly) return;

                var condition = string.Format(CultureInfo.InvariantCulture, SqlStatementFormat.NullCondition, retVal);
                ErrorCheck(condition, ValidationErrorCode, postValidation);
            }

            internal void PowerOverflowCheck(RetVal num, RetVal exponent, bool postValidation = false)
            {
                if (_checkOnly) return;

                // compute approximate power to determine if there will be an overflow
                var power = SetIntermediateVariable(new SqlBigType(), $"IIF(ISNULL({num},0)<>0,LOG(ABS({num}),10)*{exponent},0)");
                var condition = string.Format(CultureInfo.InvariantCulture, SqlStatementFormat.PowerOverflowCondition, power);
                ErrorCheck(condition, ValidationErrorCode, postValidation);

                // fractional exponents of negative numbers are invalid
                ErrorCheck($"ROUND({exponent},0) <> {exponent} AND {num} < 0", ValidationErrorCode, postValidation);

                // zero number with negative exponent is a divide by zero
                ErrorCheck($"{num} = 0 AND {exponent} < 0", Div0ErrorCode, postValidation);
            }

            internal void ErrorCheck(string condition, string errorCode, bool postValidation = false)
            {
                if (_checkOnly) return;

                if (InErrorContext)
                {
                    using (CurrentErrorContext.EmitIfCondition(condition))
                    {
                        SetIntermediateVariable(CurrentErrorContext.Code, errorCode);
                    }
                    // if this is post validation, emit any necessary else later
                    if (postValidation)
                    {
                        CurrentErrorContext.PostValidationElse = true;
                    }
                    else
                    {
                        CurrentErrorContext.EmitElse();
                    }
                }
                else
                {
                    PerformErrorCheck(condition);
                }
            }

            internal void PerformRangeChecks(RetVal result, IntermediateNode node, bool postCheck = true)
            {
                if (_checkOnly) return;

                // if this is the root node, omit the final range check
                if (node != RootNode)
                {
                    if (result.type is SqlIntType)
                    {
                        PerformOverflowCheck(result, SqlStatementFormat.IntTypeMin, SqlStatementFormat.IntTypeMax, postCheck);
                    }
                    else if (result.type is SqlMoneyType)
                    {
                        PerformOverflowCheck(result, SqlStatementFormat.MoneyTypeMin, SqlStatementFormat.MoneyTypeMax, postCheck);
                    }
                    else if (result.type is NumberType)
                    {
                        PerformOverflowCheck(result, SqlStatementFormat.DecimalTypeMin, SqlStatementFormat.DecimalTypeMax, postCheck);
                    }
                    // TODO: other range checks?
                }
            }

            internal bool ValidateNumericLiteral(double literal, FormulaType type)
            {
                if (type is SqlIntType && literal > SqlStatementFormat.IntTypeMinValue && literal < SqlStatementFormat.IntTypeMaxValue)
                {
                    return true;
                }
                else if ((type is SqlBigType || type is SqlMoneyType) && literal > SqlStatementFormat.MoneyTypeMinValue && literal < SqlStatementFormat.MoneyTypeMaxValue)
                {
                    return true;
                }
                else if (type is NumberType && literal > SqlStatementFormat.DecimalTypeMinValue && literal < SqlStatementFormat.DecimalTypeMaxValue)
                {
                    return true;
                }
                else if (!(type is NumberType))
                {
                    throw new NotSupportedException($"Unsupported type for numeric literal check: {type}");
                }

                // there was an overflow
                if (InErrorContext)
                {
                    SetIntermediateVariable(CurrentErrorContext.Code, ValidationErrorCode);
                }
                else
                {
                    _unsupportedWarnings.Add("Overflow numeric literal");
                    AppendContentLine("RETURN NULL");
                }

                return false;
            }

            private void PerformOverflowCheck(RetVal result, string min, string max, bool postValidation = true)
            {
                if (_checkOnly) return;

                var condition = string.Format(CultureInfo.InvariantCulture, SqlStatementFormat.OverflowCondition, result, min, max);
                ErrorCheck(condition, ValidationErrorCode, postValidation);
            }

            private void PerformErrorCheck(string condition)
            {
                if (_checkOnly) return;

                AppendContentLine(string.Format(CultureInfo.InvariantCulture, SqlStatementFormat.ErrorCheck, condition));
            }

            internal void DateOverflowCheck(RetVal year, RetVal month, RetVal day, bool postValidation = false)
            {
                if (_checkOnly) return;

                var condition = string.Format(CultureInfo.InvariantCulture, SqlStatementFormat.DateOverflowCondition, year, month, day);
                ErrorCheck(condition, ValidationErrorCode);
            }

            internal void DateAdditionOverflowCheck(RetVal offset, string part, RetVal date)
            {
                if (_checkOnly) return;

                // do overflow checks at the minimum resolution of an hour
                if (part == SqlStatementFormat.Minute)
                {
                    part = SqlStatementFormat.Hour;
                    date = RetVal.FromSQL($"{offset}*60", offset.type);
                }
                else if (part == SqlStatementFormat.Second)
                {
                    part = SqlStatementFormat.Hour;
                    date = RetVal.FromSQL($"{offset}*3600", offset.type);
                }
                var condition = string.Format(CultureInfo.InvariantCulture, SqlStatementFormat.DateAdditionOverflowCondition, offset, part, date);
                ErrorCheck(condition, ValidationErrorCode);
            }

            internal void DateDiffOverflowCheck(RetVal date1, RetVal date2, string part)
            {
                if (_checkOnly) return;

                switch (part)
                {
                    case SqlStatementFormat.Minute:
                    case SqlStatementFormat.Second:
                        DoesDateDiffOverflowCheck = true;
                        AppendContentLine(string.Format(CultureInfo.InvariantCulture, SqlStatementFormat.PrepareDateTimeOverflowConditionForDateDiff, date1, date2, part));
                        var condition = string.Format(CultureInfo.InvariantCulture, SqlStatementFormat.DateTimeOverflowConditionForDateDiff, date1, date2);
                        ErrorCheck(condition, ValidationErrorCode);
                        break;
                }
            }

            internal void FloatingPointErrorCheck(RetVal number)
            {
                if (_checkOnly) return;
                var condition = $"FLOOR({number}) <> {number}";
                ErrorCheck(condition, ValidationErrorCode);
            }

            /// <summary>
            /// Create a context to manage indenting and structuring if statements and ensuring blocks are closed
            /// </summary>
            internal class IfIndenter : IDisposable
            {
                private Context _context;
                private int _indentations = 0;
                private int _ifsEmitted = 0;

                internal IfIndenter(Context context, ErrorContext errorContext = null)
                {
                    _context = context;
                    // if this indenter is not associated with an error context, link to the current one
                    if (_context.InErrorContext && errorContext == null)
                    {
                        _context.CurrentErrorContext.IndenterStack.Push(this);
                    }
                }

                /// <summary>
                /// Emit an else block and return a result context
                /// </summary>
                /// <param name="isMakerDefinedCondition">A flag that indicates the conditional logic has been created by the maker, and can contain additional error checks or error contexts</param>
                /// <returns>The result context</returns>
                public IfResultContext EmitElse(bool isMakerDefinedCondition = false)
                {
                    // do not indent, as the result handles it
                    EmitElseBlock(false);

                    return _context.NewIfResultContext(isMakerDefinedCondition);
                }

                public void EmitElseIf()
                {
                    // indent before emitting the condition, to support short-circuiting
                    EmitElseBlock(true);
                }

                /// <summary>
                /// Emit an if condition and return a result context
                /// </summary>
                /// <param name="condition">A string containing the SQL condition</param>
                /// <param name="isMakerDefinedCondition">A flag that indicates the conditional logic has been created by the maker, and can contain additional error checks or error contexts</param>
                /// <returns>The result context</returns>
                public IfResultContext EmitIfCondition(string condition, bool isMakerDefinedCondition = false)
                {
                    _ifsEmitted++;
                    _context.AppendContentLine(string.Format(CultureInfo.InvariantCulture, "IF ({0}) BEGIN", condition));
                    return _context.NewIfResultContext(isMakerDefinedCondition);
                }

                private void EmitElseBlock(bool indent)
                {
                    _context.AppendContentLine("END ELSE BEGIN");
                    if (indent)
                    {
                        _indentations++;
                        _context._indentLevel++;
                    }
                }

                public void Dispose()
                {
                    // if this indenter is registered on the current error context, remove it
                    if (_context.InErrorContext && _context.CurrentErrorContext.IndenterStack.Peek() == this)
                    {
                        _context.CurrentErrorContext.IndenterStack.Pop();
                    }

                    // emit ends and reduce indentation level, as needed, interleaved
                    for (int i = 0; i < _indentations || i < _ifsEmitted; i++)
                    {
                        if (i < _ifsEmitted)
                        {
                            _context.AppendContentLine("END", true);
                        }

                        if (i < _indentations)
                        {
                            _context._indentLevel--;
                        }
                    }
                }

            }

            internal IfIndenter NewIfIndenter()
            {
                return new IfIndenter(this);
            }

            #endregion

            internal enum ContextState
            {
                None,

                /// <summary>
                /// If Condition - bare logical operations are allowed only in this context
                /// </summary>
                IfCondition,

                /// <summary>
                /// If Result - embedded else if checks are not allowed in this context and need to be refactored
                /// </summary>
                IfResult,

                /// <summary>
                /// Inline Literal - do not emit literals as variables
                /// </summary>
                InlineLiteral,

                /// <summary>
                /// Error - do not create return null for errors
                /// </summary>
                Error,

                /// <summary>
                /// UTCConversion - we are creating date time and forcing the maker to convert it to UTC
                /// </summary>
                UTCConversion
            }

            // TODO: will we need to maintain more detailed information about the context
            readonly Stack<ContextState> _stack = new Stack<ContextState>();

            private bool checkCondition(ContextState state)
            {
                return _stack.Contains(state);
            }

            private void setCondition(ContextState state, bool value)
            {
                if (value)
                {
                    _stack.Push(state);
                }
                else
                {
                    _stack.Pop();
                }
            }

            internal class ContextStateContainer : IDisposable
            {
                protected Context _context;
                protected ContextState _state;

                public ContextStateContainer(Context context, ContextState state)
                {
                    _context = context;
                    _state = state;
                    context.setCondition(state, true);
                }

                public virtual void Dispose()
                {
                    _context.setCondition(_state, false);
                }
            }

            #region If Condition Context
            public IfConditionContext NewIfConditionContext()
            {
                return new IfConditionContext(this);
            }

            internal class IfConditionContext : ContextStateContainer
            {
                internal IfConditionContext(Context context) : base(context, ContextState.IfCondition) {}
            }

            #endregion

            #region If Result Context

            public IfResultContext NewIfResultContext(bool isMakerDefinedCondition = false)
            {
                return new IfResultContext(this, isMakerDefinedCondition);
            }

            internal class IfResultContext : ContextStateContainer
            {
                private IfIndenter _nestedIndenter;
                internal IfResultContext(Context context, bool isMakerDefinedCondition) : base(context, ContextState.IfResult)
                {
                    context._indentLevel++;
                    // if we are creating a new result context in an error context, create a new indenter on the error context so nested results get rolled up
                    if (context.InErrorContext && isMakerDefinedCondition)
                    {
                        _nestedIndenter = context.NewIfIndenter();
                    }
                }

                public override void Dispose()
                {
                    if (_nestedIndenter != null)
                    {
                        _nestedIndenter.Dispose();
                    }
                    _context._indentLevel--;
                    base.Dispose();
                }
            }
            #endregion

            #region Inline Literal Context
            public bool InInlineLiteralContext
            {
                get
                {
                    return checkCondition(ContextState.InlineLiteral);
                }
            }

            public InlineLiteralContext NewInlineLiteralContext()
            {
                return new InlineLiteralContext(this);
            }

            internal class InlineLiteralContext : ContextStateContainer
            {
                internal InlineLiteralContext(Context context) : base(context, ContextState.InlineLiteral) { }
            }
            #endregion

            #region Error Context
            public bool InErrorContext
            {
                get
                {
                    return checkCondition(ContextState.Error);
                }
                internal set
                {
                    setCondition(ContextState.Error, value);
                }
            }

            public ErrorContext NewErrorContext()
            {
                var error = new ErrorContext(this);
                ErrorContext.ErrorStack.Push(error);
                InErrorContext = true;

                return error;
            }

            public ErrorContext CurrentErrorContext => ErrorContext.ErrorStack.Peek();

            public class ErrorContext : IDisposable
            {
                internal static Stack<ErrorContext> ErrorStack = new Stack<ErrorContext>();

                private Context _context;
                private List<IfResultContext> _resultContexts = new List<IfResultContext>();

                public RetVal Code { get; }

                public bool PostValidationElse { get; set; }

                public Stack<IfIndenter> IndenterStack { get; }

                internal ErrorContext(Context context)
                {
                    _context = context;
                    Code = context.GetTempVar(new SqlIntType());
                    context.SetIntermediateVariable(Code, "0");
                    IndenterStack = new Stack<IfIndenter>();
                    IndenterStack.Push(new IfIndenter(context, this));
                }

                private IfIndenter CurrentIndenter => IndenterStack.Peek();

                public IfResultContext EmitIfCondition(string condition)
                {
                    return CurrentIndenter.EmitIfCondition(condition);
                }

                public void EmitElse()
                {
                    _resultContexts.Add(CurrentIndenter.EmitElse());
                }

                public void Dispose()
                {
                    _context.InErrorContext = false;
                    _resultContexts.ForEach(resultContext => resultContext.Dispose());
                    IndenterStack.ToList().ForEach(indenter => indenter.Dispose());
                    ErrorStack.Pop();
                }
            }
            #endregion
        }
    }
}
