using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Executes a qury against a table and return a single record.
    /// </summary>
    internal class DelegatedSingleQueryFunction : DelegateFunction
    {
        public DelegatedSingleQueryFunction(DelegationHooks hooks, TableType tableType)
          : base(hooks, "__sQuery", tableType, tableType, FormulaType.Number)
        {
        }

        public override async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            var table = (TableValue)args[0];
            FilterExpression filter = null;

            if (args[1] is DelegationInfoValue delegationInfoValue)
            {
                filter = (FilterExpression)delegationInfoValue._value;
            }

            var row = await _hooks.RetrieveAsync(table, filter, cancellationToken);
            return row.ToFormulaValue();
        }
    }
}