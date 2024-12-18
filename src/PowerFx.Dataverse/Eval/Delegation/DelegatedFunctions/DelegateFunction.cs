// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    // Delegation means rewriting a client-side functions into functions that make efficient server calls.
    // This means injecting new runtime helper functions into the IR.
    // As runtime helpers, they can't be referenced by binder and don't show in intellisense or source.
    // As such, the actual function name doesn't matter and is just used for diagnostics.
    internal abstract class DelegateFunction : TexlFunction, IAsyncTexlFunction5
    {
        protected readonly DelegationHooks _hooks;

        protected readonly FormulaType ReturnFormulaType;

        public DelegateFunction(DelegationHooks hooks, string name, FormulaType returnType, params FormulaType[] paramTypes)
          : this(hooks, name, returnType._type, Array.ConvertAll(paramTypes, x => x._type))
        {
            ReturnFormulaType = returnType;
        }

        private DelegateFunction(DelegationHooks hooks, string name, DType returnType, params DType[] paramTypes)
        : base(DPath.Root, name, name, SG("Custom func " + name), FunctionCategories.Table, returnType, 0, paramTypes.Length, paramTypes.Length, paramTypes)
        {
            _hooks = hooks;
        }

        public static TexlStrings.StringGetter SG(string text)
        {
            return (string locale) => text;
        }

        // Not a behavior function. Behavior functions block delegation
        public override bool IsSelfContained => true;

        public override IEnumerable<TexlStrings.StringGetter[]> GetSignatures()
        {
            yield return new TexlStrings.StringGetter[0];
        }

        public Task<FormulaValue> InvokeAsync(IServiceProvider services, FormulaType irContext, FormulaValue[] args, CancellationToken cancellationToken)
        {
            // If any of the args are errors, return the first one
            List<ErrorValue> errors = new List<ErrorValue>();
            foreach (var arg in args)
            {
                if (arg is ErrorValue error)
                {
                    errors.Add(error);
                }
            }
            
            if (errors.Any())
            {
                return Task.FromResult<FormulaValue>(ErrorValue.Combine(IRContext.NotInSource(ReturnFormulaType), errors));
            }

            return ExecuteAsync(services, args, cancellationToken);
        }

        protected abstract Task<FormulaValue> ExecuteAsync(IServiceProvider services, FormulaValue[] args, CancellationToken cancellationToken);

        internal virtual bool IsUsingColumnMap(CallNode node, out ColumnMap columnMap)
        {
            columnMap = null;
            return false;
        }
    }
}
