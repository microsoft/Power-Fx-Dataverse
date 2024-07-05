using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation.DelegatedFunctions
{
    internal class DelegatedSort : DelegateFunction
    {
        public DelegatedSort(DelegationHooks hooks)
         : base(hooks, "__orderBy", FormulaType.Blank)
        {
        }
       
        protected override async Task<FormulaValue> ExecuteAsync(IServiceProvider services, FormulaValue[] args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (args[0] is not IDelegatableTableValue table)
            {
                throw new InvalidOperationException($"args0 should always be of type {nameof(TableValue)} : found {args[0]}");
            }

            List<OrderExpression> orderExpressions = new List<OrderExpression>();

            int columns = (args.Length - 1) / 2;
            for (int i = 0; i < columns; i++)
            {
                string column = args[1 + i * 2] is StringValue sv ? sv.Value : throw new ArgumentException($"arg{1 + i + 2} should be of string type");
                bool ordering = args[2 + i * 2] is BooleanValue bv ? bv.Value : throw new ArgumentException($"arg{2 + i + 2} should be of boolean type");

                orderExpressions.Add(new OrderExpression(column, ordering ? OrderType.Ascending : OrderType.Descending));

            }

            return new DelegationFormulaValue(null, null, orderExpressions);
        }
    }
}
