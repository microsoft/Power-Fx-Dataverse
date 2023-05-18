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
    /// Executes a query against a table and returns a record.
    /// First Arg is the table to query, Second Arg is the filter to apply.
    /// </summary>
    internal class DelegatedRetrieveSingleFunction : DelegateFunction
    {
        public DelegatedRetrieveSingleFunction(DelegationHooks hooks, TableType tableType)
          : base(hooks, "__retrieveSingle", tableType, tableType, FormulaType.Number)
        {
        }

        public override async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            var table = (TableValue)args[0];
            FilterExpression filter;

            if (args[1] is DelegationFormulaValue DelegationFormulaValue)
            {
                filter = DelegationFormulaValue._value;
            }
            else
            {
                throw DelegationHelper.CommonException.InvalidInputArg;
            }

            var row = await _hooks.RetrieveAsync(table, filter, cancellationToken);
            return row.ToFormulaValue();
        }
    }
}