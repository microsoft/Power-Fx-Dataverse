using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    // Delegation means rewriting a client-side functions into functions that make efficient server calls. 
    // This means injecting new runtime helper functions into the IR.
    // As runtime helpers, they can't be referenced by binder and don't show in intellisense or source. 
    // As such, the actual function name doesn't matter and is just used for diagnostics. 
    internal abstract class DelegateFunction : TexlFunction, IAsyncTexlFunction
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

        public abstract Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken);
    }
}