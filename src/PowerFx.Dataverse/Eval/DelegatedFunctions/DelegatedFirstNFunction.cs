using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    // Generate a lookup call for: __top(Table, count)  
    internal class DelegatedFirstNFunction : DelegateFunction
    {
        public DelegatedFirstNFunction(DelegationHooks hooks)
          : base(hooks, "__top", FormulaType.Blank, FormulaType.Number)
        {
        }

        public override async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            var topCount = ((NumberValue)args[0]).Value; // FirstN alway has NumberType as first arg and not decimal.
            var filter = new FilterExpression();
            var result = new DelegationFormulaValue(filter, (int)topCount);

            return result;
        }
    }
}
