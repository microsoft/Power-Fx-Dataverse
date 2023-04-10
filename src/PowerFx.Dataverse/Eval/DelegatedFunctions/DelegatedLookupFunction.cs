using Microsoft.PowerFx.Types;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    // Generate a lookup call for: Lookup(Table, Id=Guid)  
    internal class DelegateLookupFunction : DelegateFunction
    {
        public DelegateLookupFunction(DelegationHooks hooks, TableType tableType)
          : base(hooks, "__lookup", tableType.ToRecord(), tableType, FormulaType.Guid)
        {
        }

        public override async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            var table = (TableValue)args[0];
            var guid = ((GuidValue)args[1]).Value;

            var result = await _hooks.RetrieveAsync(table, guid, cancellationToken);

            // $$$ Error? Throw?
            return result.Value;
        }
    }
}
