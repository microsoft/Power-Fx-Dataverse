using Microsoft.PowerFx.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    // Generate a lookup call for: __retrieveGUID(Table, Id=Guid)  
    internal class DelegatedRetrieveGUIDFunction : DelegateFunction
    {
        public DelegatedRetrieveGUIDFunction(DelegationHooks hooks, TableType tableType)
          : base(hooks, "__retrieveGUID", tableType.ToRecord())
        {
        }

        protected override async Task<FormulaValue> ExecuteAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            if (args[0] is not TableValue table)
            {
                throw new InvalidOperationException($"args0 should alway be of type {nameof(TableValue)} : found {args[0]}");
            }

            if (args[1] is BlankValue)
            {
                return FormulaValue.NewBlank(this.ReturnFormulaType);
            }

            var guid = ((GuidValue)args[1]).Value;

            // column names to fetch.
            IEnumerable<string> columns = null;
            if (args.Length > 2)
            {
                columns = args.Skip(2).Select(x => ((StringValue)x).Value);
            }

            var result = await _hooks.RetrieveAsync(table, guid, columns, cancellationToken).ConfigureAwait(false);

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
                var resultRecord = CompileTimeTypeWrapperRecordValue.AdjustType((RecordType)ReturnFormulaType, result.Value);
                return resultRecord;
            }
        }
    }
}
