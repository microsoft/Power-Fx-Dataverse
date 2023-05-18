using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using System.Data;
using System;

namespace Microsoft.PowerFx.Dataverse.Eval.DelegatedFunctions
{
    internal static class DelegationHelper
    {
        public static class CommonException
        {
            public static Exception InvalidInputArg = new InvalidOperationException($"Input arg should alway be of type {nameof(DelegationInfoValue)}");
        }

        public static DelegationFormulaValue OperatorFilter(FormulaValue[] args, ConditionOperator op, FormulaType returnFormulaType)
        {
            var field = ((StringValue)args[0]).Value;
            var value = args[1].ToObject(); // $$$ Primitive?

            var filter = new FilterExpression();
            filter.AddCondition(field, op, value);

            var result = new DelegationFormulaValue(IRContext.NotInSource(returnFormulaType), filter);
            return result;
        }
    }
}
