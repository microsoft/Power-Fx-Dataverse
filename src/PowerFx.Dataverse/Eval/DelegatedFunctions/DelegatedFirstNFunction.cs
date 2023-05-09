using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    // Generate a lookup call for: __FirstN(Table, count)  
    internal class DelegatedFirstNFunction : DelegateFunction
    {
        public DelegatedFirstNFunction(DelegationHooks hooks, TableType tableType)
          : base(hooks, "__top", tableType, tableType, FormulaType.Number)
        {
        }

        public override async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            var table = (TableValue)args[0];
            var topCount = ((NumberValue)args[1]).Value;

            var rows = await _hooks.RetrieveMultipleAsync(table, filter: null, (int)topCount, cancellationToken);

            var result = new InMemoryTableValue(IRContext.NotInSource(this.ReturnFormulaType), rows);

            return result;
        }
    }
}
