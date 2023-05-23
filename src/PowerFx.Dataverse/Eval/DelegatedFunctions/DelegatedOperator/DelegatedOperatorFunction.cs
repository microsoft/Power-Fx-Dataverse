using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Dataverse.Eval.DelegatedFunctions;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    // Delegation means rewriting a client-side functions into functions that make efficient server calls. 
    // This means injecting new runtime helper functions into the IR.
    // As runtime helpers, they can't be referenced by binder and don't show in intellisense or source. 
    // As such, the actual function name doesn't matter and is just used for diagnostics. 
    internal abstract class DelegatedOperatorFunction : DelegateFunction
    {
        protected readonly ConditionOperator op;

        public DelegatedOperatorFunction(DelegationHooks hooks, string name, ConditionOperator op, FormulaType returnType, params FormulaType[] paramTypes)
          : base(hooks, name, returnType, paramTypes)
        {
            this.op = op;
        }

        public override async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            var field = ((StringValue)args[0]).Value;
            if (args[1].Type._type.IsPrimitive)
            {
                var value = args[1].ToObject();

                var filter = new FilterExpression();
                filter.AddCondition(field, op, value);

                var result = new DelegationFormulaValue(filter);
                return result;
            }
            else
            {
                throw new InvalidOperationException("Unsupported type");
            }
        }
    }
}