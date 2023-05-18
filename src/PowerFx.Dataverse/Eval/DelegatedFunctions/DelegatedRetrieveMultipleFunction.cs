using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Dataverse.Eval.DelegatedFunctions;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Executes a query against a table and returns a table.
    /// First Arg is the table to query, Second Arg is the filter to apply, Third Arg is the number of records to return.
    /// </summary>
    internal class DelegatedRetrieveMultipleFunction : DelegateFunction
    {
        public DelegatedRetrieveMultipleFunction(DelegationHooks hooks, TableType tableType)
          : base(hooks, "__retrieveMultiple", tableType, tableType, FormulaType.Number)
        {
        }

        public override async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            var table = (TableValue)args[0];
            int? topCount = null;
            FilterExpression filter;

            if (args[2] is NumberValue count)
            {
                topCount = (int)(count).Value;
            }

            if (args[1] is DelegationFormulaValue DelegationFormulaValue)
            {
                filter = DelegationFormulaValue._value;
            }
            else
            {
                throw DelegationHelper.CommonException.InvalidInputArg;
            }

            var rows = await _hooks.RetrieveMultipleAsync(table, filter, topCount, cancellationToken);
            var result = new InMemoryTableValue(IRContext.NotInSource(this.ReturnFormulaType), rows);
            return result;
        }
    }
}