// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

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

        // arg0 (, arg2 ...) : column name
        // arg1 (, arg3 ...) : ascending (true)/descending
        protected override async Task<FormulaValue> ExecuteAsync(IServiceProvider services, FormulaValue[] args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<OrderExpression> orderExpressions = new List<OrderExpression>();

            int columns = args.Length / 2;
            for (int i = 0; i < columns; i++)
            {
                var columnNamePosition = i * 2;
                string column = args[columnNamePosition] is StringValue sv ? sv.Value : throw new ArgumentException($"arg{1 + i + 2} should be of string type");
                bool ordering = args[columnNamePosition + 1] is BooleanValue bv ? bv.Value : throw new ArgumentException($"arg{2 + i + 2} should be of boolean type");

                orderExpressions.Add(new OrderExpression(column, ordering ? OrderType.Ascending : OrderType.Descending));
            }

            return new DelegationFormulaValue(null, orderExpressions);
        }
    }
}
