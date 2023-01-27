//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.Xrm.Sdk;

namespace Microsoft.PowerFx.Dataverse
{
    internal static class DataverseHelpers
    {
        internal static ExpressionError GetExpressionError(string message, ErrorSeverity severity = ErrorSeverity.Critical, string messageKey = null)
        {
            return new ExpressionError() { Kind = ErrorKind.Unknown, Severity = severity, Message = message, MessageKey = messageKey };
        }

        // For some specific column types we need to extract the primitive value.
        internal static object ExtractPrimitiveValue(object value)
        {
            if (value is Money money) 
            {
                return money.Value;
            }

            return value;
        }
    }
}
