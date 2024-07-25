using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Types;
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

        protected override async Task<FormulaValue> ExecuteAsync(IServiceProvider services, FormulaValue[] args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (args[0] is not TableValue table)
            {
                throw new InvalidOperationException($"args0 should always be of type {nameof(TableValue)} : found {args[0]}");
            }

            if (args[1] is BlankValue)
            {
                return FormulaValue.NewBlank(this.ReturnFormulaType);
            }

            var guid = ((GuidValue)args[1]).Value;

            string partitionId;
            if (args[2] is BlankValue)
            {
                partitionId = null;
            }
            else
            {
                partitionId = ((StringValue)args[2]).Value;
            }

            ColumnMap columnMap = null;

            if (args.Length > 3)
            {
                columnMap = args[3] is RecordValue rv 
                    ? new ColumnMap(rv)
                    : throw new InvalidOperationException($"Expecting args3 to be a {nameof(RecordValue)} : found {args[4].GetType().Name}");                
            }

            var result = await _hooks.RetrieveAsync(table, guid, partitionId, columnMap, cancellationToken).ConfigureAwait(false);

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
