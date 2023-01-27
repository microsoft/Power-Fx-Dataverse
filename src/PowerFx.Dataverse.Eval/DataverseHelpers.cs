//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Microsoft.PowerFx.Dataverse
{
    internal static class DataverseHelpers
    {
        internal static ExpressionError GetExpressionError(string message, ErrorSeverity severity = ErrorSeverity.Critical, string messageKey = null)
        {
            return new ExpressionError() { Kind = ErrorKind.Unknown, Severity = severity, Message = message, MessageKey = messageKey };
        }
    }
}
