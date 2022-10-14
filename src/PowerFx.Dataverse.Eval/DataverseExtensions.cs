//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Connectors;
using Microsoft.PowerFx.Preview;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse
{
    internal static class DataverseExtensions
    {
        public static bool Execute<Req, Res, Out>(this IOrganizationService svcClient, Req request, Func<Res, Out> transform, Action<ExecutionResult, string> logMessage, out Out result)
            where Req : OrganizationRequest
            where Res : OrganizationResponse
        {
            try
            {
                var response = (Res)svcClient.Execute(request);
                if (logMessage != null) logMessage(ExecutionResult.Success, null);
                result = transform(response);
                return true;
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                if (logMessage != null) logMessage(ExecutionResult.Error, ex.Message);
                result = default;
                return false;
            }
        }

        public enum ExecutionResult
        {
            Success = 0,
            Error = 1
        }

        public static Entity ToEntity(this RecordValue record, EntityMetadata entityMetadata)
        {
            Entity entity = new(entityMetadata.LogicalName);

            foreach (NamedValue field in record.Fields)
            {
                entity.Attributes.Add(field.Name, field.Value.ToObject());
            }

            return entity;
        }

        public static string GetExceptionMessage(this Exception ex)
        {
            StringBuilder sb = new(1024);
            GetExceptionMessageInternal(ex, 0, sb);

            return sb.ToString().Trim();
        }

        private static void GetExceptionMessageInternal(Exception ex, int level, StringBuilder stringBuilder)
        {
            if (level > 10 || ex == null || string.IsNullOrEmpty(ex.Message))
                return;

            stringBuilder.AppendLine($"[{ex.GetType().DisplayName()}] {ex.Message}");
            GetExceptionMessageInternal(ex.InnerException, level + 1, stringBuilder);
        }

        private static string DisplayName(this Type t)
        {
            if (!t.IsGenericType)
                return t.Name;

            string genericTypeName = t.GetGenericTypeDefinition().Name;
            genericTypeName = genericTypeName.Substring(0, genericTypeName.IndexOf('`'));

            string genericArgs = string.Join(",", t.GetGenericArguments().Select(ta => DisplayName(ta)).ToArray());

            return genericTypeName + "<" + genericArgs + ">";
        }

        public static bool IsFatal(this Exception ex)
        {
            return ex is StackOverflowException ||
                   ex is AccessViolationException ||
                   ex is OutOfMemoryException ||
                   ex is TaskCanceledException;
        }

        public static bool IsSupported(this ODataParameters odataParameters)
        {
            if (odataParameters.IsDefault())
                return true;

            if (odataParameters.Count || !string.IsNullOrEmpty(odataParameters.OrderBy) || !string.IsNullOrEmpty(odataParameters.Filter))
                return false;

            // For now, only Top is supported
            return odataParameters.Top > 0;
        }

        public static bool IsDefault(this ODataParameters odataParameters)
        {
            return !odataParameters.Count && string.IsNullOrEmpty(odataParameters.Filter) && string.IsNullOrEmpty(odataParameters.OrderBy) && odataParameters.Top == 0;
        }

        public static DValue<T> DataverseError<T>(string message, string method)
            where T : ValidFormulaValue
        {
            return DValue<T>.Of(ErrorValue.NewError(new ExpressionError() { Kind = ErrorKind.Unknown, Message = message, Severity = ErrorSeverity.Critical, MessageKey = method }));
        }

        public static DataverseResponse<T> DataverseCall<T>(Func<T> call, string noResultMessage = null, [CallerMemberName] string memberName = null)
        {
            string GetMessage(string message) => $"Error in {memberName}: {message ?? "<null>"}";

            try
            {
                T result = call();

                return result.Equals(default(T))
                    ? new DataverseResponse<T>(GetMessage(noResultMessage))
                    : new DataverseResponse<T>(result);
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                return new DataverseResponse<T>(GetMessage(ex.GetExceptionMessage()));
            }
        }

        public static DataverseResponse DataverseCall(Action call, [CallerMemberName] string memberName = null)
        {
            string GetMessage(string message) => $"Error in {memberName}: {message ?? "<null>"}";

            try
            {
                call();

                return new DataverseResponse();
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                return new DataverseResponse(GetMessage(ex.GetExceptionMessage()));
            }
        }
    }
}
