// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Types;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Generates a delegation filter expression with blank filter to retrieve entire table.
    /// </summary>
    internal class DelegatedBlank : DelegateFunction
    {
        public DelegatedBlank(DelegationHooks hooks)
          : base(hooks, "__noop", FormulaType.Blank)
        {
        }

        protected override async Task<FormulaValue> ExecuteAsync(IServiceProvider services, FormulaValue[] args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new DelegationFormulaValue(null, null);
        }
    }
}
