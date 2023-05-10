using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    // Generate a lookup call for: __Filter(Table, predicate)
    internal class DelegatedFilterFunction : DelegateFunction
    {
        public DelegatedFilterFunction(DelegationHooks hooks, TableType tableType)
          : base(hooks, "__filter", tableType, tableType, FormulaType.Number)
        {
        }

        public override async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            var table = (TableValue)args[0];

            var predicate = args[1];

            if (predicate is DelegationInfoValue delegationInfoValue)
            {
                var filter = (FilterExpression)delegationInfoValue._value;
                var rows = await _hooks.RetrieveMultipleAsync(table, filter, null, cancellationToken);
                var result = new InMemoryTableValue(IRContext.NotInSource(this.ReturnFormulaType), rows);
                return result;
            }

            if (predicate is BooleanValue booleanPredicate)
            {
                if (booleanPredicate.Value)
                {
                    var rows = await _hooks.RetrieveMultipleAsync(table, filter: null, null, cancellationToken);
                    var result = new InMemoryTableValue(IRContext.NotInSource(this.ReturnFormulaType), rows);
                    return result;
                }
                else
                {
                    var result = FormulaValue.NewBlank(ReturnFormulaType);
                    return result;
                }
            }

            throw new System.NotSupportedException(); // $$$ Error? Throw?
        }
    }
}