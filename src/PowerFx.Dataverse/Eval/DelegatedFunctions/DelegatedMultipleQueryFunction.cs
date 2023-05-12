using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Executes a qury against a table.
    /// </summary>
    internal class DelegatedMultipleQueryFunction : DelegateFunction
    {
        public DelegatedMultipleQueryFunction(DelegationHooks hooks, TableType tableType)
          : base(hooks, "__mQuery", tableType, tableType, FormulaType.Number)
        {
        }

        public override async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            var table = (TableValue)args[0];
            int? topCount = null;
            FilterExpression filter = null;

            if (args[2] is NumberValue count)
            {
                topCount = (int)(count).Value;
            }

            if (args[1] is DelegationInfoValue delegationInfoValue)
            {
                filter = (FilterExpression)delegationInfoValue._value;
            }

            var rows = await _hooks.RetrieveMultipleAsync(table, filter, topCount, cancellationToken);
            var result = new InMemoryTableValue(IRContext.NotInSource(this.ReturnFormulaType), rows);
            return result;
        }
    }
}