using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Generates a delegation filter expression for the And operator
    /// </summary>
    internal class DelegatedBlankFilter : DelegateFunction
    {
        public DelegatedBlankFilter(DelegationHooks hooks, TableType tableType)
          : base(hooks, "__blankFilter", tableType, tableType, FormulaType.Number)
        {
        }

        public override async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            var filter = new FilterExpression();
            return new DelegationInfoValue(IRContext.NotInSource(ReturnFormulaType), filter);
        }
    }
}