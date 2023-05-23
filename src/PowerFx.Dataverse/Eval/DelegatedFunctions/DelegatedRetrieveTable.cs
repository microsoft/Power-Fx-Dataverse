using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Generates a delegation filter expression with blank filter to retrieve entire table.
    /// </summary>
    internal class DelegatedBlankFilter : DelegateFunction
    {
        public DelegatedBlankFilter(DelegationHooks hooks, TableType tableType)
          : base(hooks, "__noFilter", tableType, tableType, FormulaType.Number)
        {
        }

        public override async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            var filter = new FilterExpression();
            return new DelegationFormulaValue(filter);
        }
    }
}