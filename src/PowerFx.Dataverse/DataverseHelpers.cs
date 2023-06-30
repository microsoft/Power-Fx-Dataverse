using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Dataverse.Localization;

namespace Microsoft.PowerFx.Dataverse
{
    public static class DataverseHelpers
    {
        public static ExpressionError GetExpressionError(string message, ErrorSeverity severity = ErrorSeverity.Critical, string messageKey = null)
        {
            return new ExpressionError() { Kind = ErrorKind.Unknown, Severity = severity, Message = message }.SetMessageKey(new ErrorResourceKey(messageKey, DataverseStringResources.LocalStringResources));
        }

        public static ExpressionError GetInvalidCastError(object[] messageArgs)
        {
            return new ExpressionError() { MessageArgs = messageArgs }.SetMessageKey(TexlStrings.InvalidCast); 
        }
    }
}