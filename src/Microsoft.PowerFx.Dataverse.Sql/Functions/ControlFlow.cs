// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AppMagic.Authoring.Importers.DataDescription;
using Microsoft.AppMagic.Authoring.Importers.ServiceConfig;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;
using static Microsoft.PowerFx.Dataverse.SqlVisitor;
using Span = Microsoft.PowerFx.Syntax.Span;

namespace Microsoft.PowerFx.Dataverse.Functions
{
    internal static partial class Library
    {
        public static RetVal If(SqlVisitor visitor, CallNode node, Context context)
        {
            var result = context.GetTempVar(context.GetReturnType(node));
            var resultCoerced = false;
            string optionSetName = null;

            using (var indenter = context.NewIfIndenter())
            {
                // iterate over the children in pairs
                for (int i = 0; i < node.Args.Count; i += 2)
                {
                    // a pair can either be a condition and a then result, or a single default(else) result
                    if (i + 1 < node.Args.Count)
                    {
                        // pair of condition, result
                        string conditionClause;
                        using (context.NewIfConditionContext())
                        {
                            if (i > 0)
                            {
                                indenter.EmitElseIf();
                            }

                            var condition = node.Args[i].Accept(visitor, context);

                            // SQL doesn't support boolean literals or variables in IF clauses, so add comparision to 1 (e.g. IF(@t1=1)
                            conditionClause = visitor.CoerceBooleanToOp(node.Args[i], condition, context).ToString();
                        }

                        // emit an if condition and result that can handle internal error checks or error contexts
                        using (indenter.EmitIfCondition(conditionClause, isMakerDefinedCondition: true))
                        {
                            var resultArg = node.Args[i + 1].Accept(visitor, context);
                            SetIntermediateVariableForBranchResult(result, ref resultCoerced, resultArg, node.Args[i + 1], context, ref optionSetName);
                        }
                    }
                    else
                    {
                        // or just an else result that can handle internal error checks or error contexts
                        using (indenter.EmitElse(isMakerDefinedCondition: true))
                        {
                            var resultArg = node.Args[i].Accept(visitor, context);
                            SetIntermediateVariableForBranchResult(result, ref resultCoerced, resultArg, node.Args[i], context, ref optionSetName);
                        }
                    }
                }
            }

            return result;
        }

        public static RetVal Switch(SqlVisitor visitor, CallNode node, Context context)
        {
            // SQL CASE doesn't support control flow inside comparison or result clauses, so emit as If
            // TODO: optimize for literal scenarios
            var result = context.GetTempVar(context.GetReturnType(node));
            var resultCoerced = false;
            var condition = node.Args[0].Accept(visitor, context);
            string optionSetName = null;

            using (var indenter = context.NewIfIndenter())
            {
                // iterate over the remaining children in pairs, to pre-process the conditions
                for (int i = 1; i < node.Args.Count; i += 2)
                {
                    // a pair can either be a condition value and a then result, or a single default(else) result
                    if (i + 1 < node.Args.Count)
                    {
                        RetVal val;
                        using (context.NewIfConditionContext())
                        {
                            if (i > 1)
                            {
                                indenter.EmitElseIf();
                            }

                            // pair of condition, result
                            // first, process the condition, so necessary locals can be created
                            val = node.Args[i].Accept(visitor, context);
                        }

                        // emit an if condition that can handle internal error logic
                        using (indenter.EmitIfCondition(EqualityCheckCondition(condition, val), isMakerDefinedCondition: true))
                        {
                            var resultArg = node.Args[i + 1].Accept(visitor, context);
                            SetIntermediateVariableForBranchResult(result, ref resultCoerced, resultArg, node.Args[i + 1], context, ref optionSetName);
                        }
                    }
                    else
                    {
                        // emit an else that can handle internal error logic
                        using (indenter.EmitElse(isMakerDefinedCondition: true))
                        {
                            var resultArg = node.Args[i].Accept(visitor, context);
                            SetIntermediateVariableForBranchResult(result, ref resultCoerced, resultArg, node.Args[i], context, ref optionSetName);
                        }
                    }
                }
            }

            return result;
        }

        public static RetVal Error(SqlVisitor visitor, CallNode node, Context context)
        {
            if (context.InErrorContext)
            {
                // don't actually visit record, but find the parameter for kind and convert to number
                if (node.Args[0] is RecordNode record)
                {
                    var kindNode = record.Fields[new DName("Kind")];
                    RetVal kind = kindNode.Accept(visitor, context);

                    // if the input can't be coerced to an numeric value, emit an error with the validation error kind
                    return context.SetIntermediateVariable(context.CurrentErrorContext.Code, $"IIF(ISNUMERIC({kind})=1,{kind},{Context.ValidationErrorCode})");
                }

                throw new SqlCompileException(SqlCompileException.ResultTypeNotSupported, node.Args[0].IRContext.SourceContext, node.Args[0].IRContext.ResultType._type.GetKindString());
            }
            else
            {
                // not in an error context, so just immediately return null
                context.AppendContentLine("RETURN NULL");
                return RetVal.FromSQL("NULL", context.GetReturnType(node));
            }
        }

        public static RetVal IsError(SqlVisitor visitor, CallNode node, Context context)
        {
            RetVal errorCode;
            using (var error = context.NewErrorContext())
            {
                errorCode = error.Code;
                node.Args[0].Accept(visitor, context);
            }

            return context.SetIntermediateVariable(node, $"{errorCode} <> 0");
        }

        public static RetVal IfError(SqlVisitor visitor, CallNode node, Context context)
        {
            RetVal result = context.GetTempVar(context.GetReturnType(node));
            var resultCoerced = false;
            string optionSetName = null;

            using (var indenter = context.NewIfIndenter())
            {
                var elseEmitted = false;
                RetVal lastSuccess = null;
                Span lastSuccessContext = default;
                IntermediateNode lastSuccessNode = null;

                // iterate over the children in pairs, to process the error and replacement
                for (int i = 0; i < node.Args.Count; i += 2)
                {
                    // a pair can either be a potential error and a then replacement, or a single default(else) replacement
                    if (i + 1 < node.Args.Count)
                    {
                        if (i > 1)
                        {
                            indenter.EmitElseIf();
                        }

                        RetVal errorCode;
                        using (var error = context.NewErrorContext())
                        {
                            lastSuccess = node.Args[i].Accept(visitor, context);
                            lastSuccessContext = node.Args[i].IRContext.SourceContext;
                            errorCode = error.Code;
                            lastSuccessNode = node.Args[i];
                        }

                        using (indenter.EmitIfCondition($"{errorCode} <> 0", isMakerDefinedCondition: true))
                        {
                            var resultArg = node.Args[i + 1].Accept(visitor, context);
                            SetIntermediateVariableForBranchResult(result, ref resultCoerced, resultArg, node.Args[i + 1], context, ref optionSetName);
                        }
                    }
                    else
                    {
                        elseEmitted = true;
                        using (indenter.EmitElse(isMakerDefinedCondition: true))
                        {
                            var resultArg = node.Args[i].Accept(visitor, context);
                            SetIntermediateVariableForBranchResult(result, ref resultCoerced, resultArg, node.Args[i], context, ref optionSetName);
                        }
                    }
                }

                if (!elseEmitted)
                {
                    using (indenter.EmitElse(isMakerDefinedCondition: true))
                    {
                        SetIntermediateVariableForBranchResult(result, ref resultCoerced, lastSuccess, lastSuccessNode, context, ref optionSetName);
                    }
                }
            }

            return result;
        }

        private static RetVal SetIntermediateVariableForBranchResult(RetVal result, ref bool resultCoerced, RetVal retVal, IntermediateNode node, Context context, ref string optionSetName)
        {
            var sourceContext = node.IRContext.SourceContext;

            // if the branch type is more specific than the overall binding type, update it
            if (retVal.Type != result.Type && IsDateTimeType(retVal.Type))
            {
                if (!resultCoerced)
                {
                    result.Type = retVal.Type;
                }
                else
                {
                    // if the result type doesn't match the previous specific date type, and would require a time zone conversion, which is not supported, it is an error
                    ValidateTypeCompatibility(retVal, result, sourceContext);
                }
            }

            if (context._dataverseFeatures.IsOptionSetEnabled && !(retVal.Type is BlankType) && retVal?.VarName != null)
            {
                var columnDefinition = context.GetVarDetails(retVal.VarName)?.Column;
                ValidateOptionSetResultArgument(node, columnDefinition, ref optionSetName);
            }

            resultCoerced = true;
            return context.SetIntermediateVariable(result, fromRetVal: retVal);
        }

        /// <summary>
        /// Validates if all optionsetvalue result arguments are using same optionset.
        /// e.g., If(1>2, 'localPicklist1'.A, 'localPicklist2'.A) - here localPicklist1, localPicklist2 are two different optionsets and
        /// two different optionsets cannot be used as result type, hence should throw error.
        /// </summary>
        private static void ValidateOptionSetResultArgument(IntermediateNode node, CdsColumnDefinition columnDefinition, ref string optionSetName)
        {
            string currentResultArgOptionSetName = null;

            if (node is LazyEvalNode lazyEvalNode && lazyEvalNode.Child is RecordFieldAccessNode fieldNode &&
                fieldNode.From is ResolvedObjectNode resolvedNode && resolvedNode.Value is DataverseOptionSet optionSet)
            {
                currentResultArgOptionSetName = optionSet.InvariantName;
            }
            else if (columnDefinition != null && columnDefinition.IsOptionSet && columnDefinition.TypeDefinition is CdsOptionSetTypeDefinition optionSetTypeDefinition)
            {
                currentResultArgOptionSetName = optionSetTypeDefinition.Title;
            }

            if (optionSetName == null)
            {
                optionSetName = currentResultArgOptionSetName;
            }
            else if (!optionSetName.Equals(currentResultArgOptionSetName))
            {
                throw new SqlCompileException(SqlCompileException.ResultFromMultipleOptionSetsNotSupported, node.IRContext.SourceContext);
            }
        }
    }
}
