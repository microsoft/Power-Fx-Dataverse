using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.PowerFx.Core.IR;

namespace Microsoft.PowerFx.Dataverse.Eval.DelegatedFunctions
{
    internal class DelegationHelper
    {
        public static DelegationInfoValue OperatorFilter(FormulaValue[] args, ConditionOperator op, FormulaType returnFormulaType)
        {
            var field = ((StringValue)args[0]).Value;
            var value = args[1].ToObject();

            var filter = new FilterExpression();
            filter.AddCondition(field, op, value);

            var result = new DelegationInfoValue(IRContext.NotInSource(returnFormulaType), filter);
            return result;
        }
    }
}
