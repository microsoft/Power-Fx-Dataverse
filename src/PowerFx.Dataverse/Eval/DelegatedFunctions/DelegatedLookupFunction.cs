using Microsoft.PowerFx.Types;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    // Generate a lookup call for: __retrieveGUID(Table, Id=Guid)  
    internal class DelegateLookupFunction : DelegateFunction
    {
        public DelegateLookupFunction(DelegationHooks hooks, TableType tableType)
          : base(hooks, "__retrieveGUID", tableType.ToRecord(), tableType, FormulaType.Guid)
        {
        }

        public override async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            var table = (TableValue)args[0];
            var guid = ((GuidValue)args[1]).Value;

            var result = await _hooks.RetrieveAsync(table, guid, cancellationToken);
                        
            var fv = result.ToFormulaValue();

            return fv;
        }
    }

    /*
    // Generate a lookup call for: Lookup(Table, int count)  
    internal class DelegateFirstNFunction : DelegateFunction
    {
        public DelegateFirstNFunction(DelegationHooks hooks, TableType tableType)
          : base(hooks, "__firstn", tableType.ToRecord(), tableType, FormulaType.Number)
        {
        }

        public override async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            var table = (TableValue)args[0];
            var count = ((NumberValue)args[1]).Value;

            var result = await _hooks.RetrieveAsync(table, guid, cancellationToken);

            // $$$ Error? Throw?
            return result.Value;
        }
    }*/
}
