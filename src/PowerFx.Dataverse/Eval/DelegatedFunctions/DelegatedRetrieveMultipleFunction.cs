using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
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
          : base(hooks, "__retrieveMultiple", tableType)
        {
        }

        protected override async Task<FormulaValue> ExecuteAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            // propagate args[0] if it's not a table (e.g. Blank/Error)
            if (args[0] is not TableValue table)
            {
                return args[0];
            }

            int? topCount = null;
            FilterExpression filter;

            if (args[2] is NumberValue count)
            {
                topCount = (int)(count).Value;
            }
            else if (args[2] is BlankValue)
            {
                // If Count is Blank(), return empty table.
                var emptyList = new List<DValue<RecordValue>>();
                return new InMemoryTableValue(IRContext.NotInSource(this.ReturnFormulaType), emptyList);
            }
            else
            {
                throw new InvalidOperationException($"args2 should alway be of type {nameof(NumberValue)} or {nameof(BlankValue)} : found {args[2]}");
            }

            if (args[1] is DelegationFormulaValue DelegationFormulaValue)
            {
                filter = DelegationFormulaValue._value;
            }
            else
            {
                throw new InvalidOperationException($"args1 should alway be of type {nameof(DelegationFormulaValue)} : found {args[1]}");
            }

            var rows = await _hooks.RetrieveMultipleAsync(table, filter, topCount, cancellationToken);
            var result = new InMemoryTableValue(IRContext.NotInSource(this.ReturnFormulaType), rows);
            return result;
        }
    }
}