//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse
{
    public class DVAddPlugInFunction : ReflectionFunction
    {
        public DVAddPlugInFunction()
            : base("DVAddPlugIn", FormulaType.String, FormulaType.String, FormulaType.String)
        {
            ConfigType = typeof(IDataversePlugInContext);
        }

        public async Task<FormulaValue> Execute(IDataversePlugInContext context, StringValue @namespace, StringValue pluginName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(@namespace.Value))
            {
                return FormulaValue.NewError(new ExpressionError() { Kind = ErrorKind.InvalidArgument, Severity = ErrorSeverity.Critical, Message = @"Need a valid namespace" });
            }

            if (string.IsNullOrEmpty(pluginName.Value))
            {
                return FormulaValue.NewError(new ExpressionError() { Kind = ErrorKind.InvalidArgument, Severity = ErrorSeverity.Critical, Message = @"Need a valid plugin name" });
            }
         
            CustomApiSignature plugin = await context.GetDataverseObjectAsync<CustomApiSignature>(pluginName.Value, cancellationToken).ConfigureAwait(false);

            if (plugin == null)
            {
                return FormulaValue.NewError(new ExpressionError() { Kind = ErrorKind.InvalidArgument, Severity = ErrorSeverity.Critical, Message = @"There is no plugin with that name." });
            }

            return context.AddPlugIn(@namespace.Value, plugin);
        }
    }
}
