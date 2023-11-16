using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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
        public DelegatedRetrieveSingleFunction(DelegationHooks hooks, RecordType returnType)
          : base(hooks, "__retrieveSingle", returnType)
        {
        }

        protected override async Task<FormulaValue> ExecuteAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            if (args[0] is not TableValue table)
            {
                throw new InvalidOperationException($"args0 should alway be of type {nameof(TableValue)} : found {args[0]}");
            }

            FilterExpression filter;

            if (args[1] is DelegationFormulaValue DelegationFormulaValue)
            {
                filter = DelegationFormulaValue._value;
            }
            else
            {
                throw new InvalidOperationException($"Input arg should alway be of type {nameof(DelegationFormulaValue)}"); ;
            }

            // column names to fetch.
            IEnumerable<string> columns = null;
            if (args.Length > 2)
            {
                columns = args.Skip(2).Select(x => ((StringValue)x).Value);
            }

            var row = await _hooks.RetrieveMultipleAsync(table, filter, 1, columns, cancellationToken).ConfigureAwait(false);

            var result = row.FirstOrDefault();
            if (result == null || result.IsBlank)
            {
                return FormulaValue.NewBlank(this.ReturnFormulaType);
            }
            else if (result.IsError)
            {
                return result.Error;
            }
            else
            {
                // Adjust type, as function like ShowColumn() can manipulate it.
                var resultRecord =  CompileTimeTypeWrapperRecordValue.AdjustType((RecordType)ReturnFormulaType, result.Value);
                return resultRecord;
            }
        }
    }
}
