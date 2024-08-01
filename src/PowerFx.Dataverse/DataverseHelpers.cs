// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Dataverse.Localization;

namespace Microsoft.PowerFx.Dataverse
{
    public static class DataverseHelpers
    {
        public static ExpressionError GetExpressionError(string message, ErrorSeverity severity = ErrorSeverity.Critical, string messageKey = null)
        {
            var resourceKey = (messageKey == null) ? default : new ErrorResourceKey(messageKey, DataverseStringResources.LocalStringResources);

            return new ExpressionError()
            {
                Kind = ErrorKind.Unknown,
                Severity = severity,
                Message = message,
                ResourceKey = resourceKey
            };
        }

        public static ExpressionError GetInvalidCastError(string[] messageArgs)
        {
            return new ExpressionError()
            {
                MessageArgs = messageArgs,
                ResourceKey = TexlStrings.InvalidCast
            };
        }
    }
}
