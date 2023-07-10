using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Dataverse.Localization;

namespace Microsoft.PowerFx.Dataverse
{
    public static class DataverseHelpers
    {
        public static ExpressionError GetExpressionError(string message, ErrorSeverity severity = ErrorSeverity.Critical, string messageKey = null)
        {
            if (messageKey == null)
            {
                // ErrorResourceKey has assert that messageKey can't be null.
                return new ExpressionError()
                {
                    Kind = ErrorKind.Unknown,
                    Severity = severity,
                    Message = message
                };
            }

            return new ExpressionError() 
            { 
                Kind = ErrorKind.Unknown, 
                Severity = severity, 
                Message = message,
                ResourceKey = new ErrorResourceKey(messageKey, DataverseStringResources.LocalStringResources)
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
