using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System;
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
        protected readonly ConditionOperator _op;

        public DelegatedOperatorFunction(DelegationHooks hooks, string name, ConditionOperator op, params FormulaType[] paramTypes)
          : base(hooks, name, FormulaType.Blank, paramTypes)
        {
            this._op = op;
        }

        public override async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            // propagate args[0] if it's not a table (e.g. Blank/Error)
            if (args[0] is not TableValue table)
            {
                return args[0];
            }

            var field = ((StringValue)args[1]).Value;
            var value = args[2];
            if (value.Type._type.IsPrimitive)
            {
                var dvValue = _hooks.RetrieveAttribute(table, field, value);
                
                var filter = new FilterExpression();
                filter.AddCondition(field, _op, dvValue);

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