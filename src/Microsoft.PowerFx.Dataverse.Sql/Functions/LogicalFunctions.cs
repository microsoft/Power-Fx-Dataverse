//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Core.IR;
using System;
using System.Collections.Generic;
using Microsoft.PowerFx.Core.IR.Nodes;
using static Microsoft.PowerFx.Dataverse.SqlVisitor;

namespace Microsoft.PowerFx.Dataverse.Functions
{
    internal static partial class Library
    {
        public static RetVal Not(SqlVisitor visitor, CallNode node, Context context)
        {
            var returnType = context.GetReturnType(node);
            var arg = node.Args[0].Accept(visitor, context);
            var op = visitor.CoerceBooleanToOp(node.Args[0], arg, context);
            return context.SetIntermediateVariable(returnType, $"(NOT {op})");
        }

        public static RetVal LogicalSetFunction(SqlVisitor visitor, CallNode node, Context context, string function, bool shortCircuitTest)
        {
            using (var indenter = context.NewIfIndenter())
            {
                // the default result is the negation of the short circuit test
                var defaultResult = shortCircuitTest ? "0" : "1";
                RetVal result = null;
                var args = new List<string>(node.Args.Count);
                for (int i = 0; i < node.Args.Count; i++)
                {
                    if (i > 0)
                    {
                        indenter.EmitElseIf();
                    }

                    var arg = node.Args[i].Accept(visitor, context);
                    var coercedArg = visitor.CoerceBooleanToOp(node.Args[i], arg, context);

                    // if there is a single parameter, this is a pass thru
                    if (i == 0 && node.Args.Count == 1)
                    {
                        return context.SetIntermediateVariable(node, fromRetVal: coercedArg);
                    }
                    else if (result == null)
                    {
                        result = context.GetTempVar(context.GetReturnType(node));
                        context.SetIntermediateVariable(result, defaultResult);
                    }

                    var condition = shortCircuitTest ? coercedArg.ToString() : $"(NOT {coercedArg})";
                    using (indenter.EmitIfCondition(condition))
                    {
                        result = context.SetIntermediateVariable(result, fromRetVal:coercedArg);
                    }
                }
                return result;
            }
        }
    }
}
