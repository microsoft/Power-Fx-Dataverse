using Microsoft.PowerFx.Types;
using System;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    // Generate a lookup call for: __retrieveGUID(Table, Id=Guid)  
    internal class DelegatedRetrieveGUIDFunction : DelegateFunction
    {
        public DelegatedRetrieveGUIDFunction(DelegationHooks hooks, TableType tableType)
          : base(hooks, "__retrieveGUID", tableType.ToRecord(), tableType, FormulaType.Guid)
        {
        }

        protected override async Task<FormulaValue> ExecuteAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            if (args[0] is not TableValue table)
            {
                throw new InvalidOperationException($"args0 should alway be of type {nameof(TableValue)} : found {args[0]}");
            }

            var guid = ((GuidValue)args[1]).Value;

            var result = await _hooks.RetrieveAsync(table, guid, cancellationToken);
                        
            var fv = result.ToFormulaValue();

            return fv;
        }
    }
}
