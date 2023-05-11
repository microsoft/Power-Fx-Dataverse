//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Connectors;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.ServiceModel;
using System.ServiceModel.Security;
using static Microsoft.PowerFx.Dataverse.DataverseHelpers;

namespace Microsoft.PowerFx.Dataverse
{
    internal static class DataverseExtensions
    {
        public static bool Execute<Req, Res, Out>(this IOrganizationService svcClient, Req request, Func<Res, Out> transform, out Out result)
            where Req : OrganizationRequest
            where Res : OrganizationResponse
        {
            var resp = DataverseCall<Res>(() => (Res)svcClient.Execute(request), request.RequestName);
            if (resp.HasError)
            {
                result = default;
                return false;
            }

            result = transform(resp.Response);
            return true;
        }
       
        // Record should already be logical names. 
        public static Entity ConvertRecordToEntity(this RecordValue record, EntityMetadata metadata, out DValue<RecordValue> error, [CallerMemberName] string methodName = null)
        {
            // Contains only the modified fields.
            var leanEntity = new Entity(metadata.LogicalName);

            error = null;

            foreach (var field in record.Fields)
            {
                if (!metadata.TryGetAttribute(field.Name, out var amd))
                {
                    if (metadata.TryGetRelationship(field.Name, out var realAttributeName))
                    {
                        // Get primary key, set as guid. 
                        var dvr = field.Value as DataverseRecordValue;
                        if (dvr == null)
                        {
                            // Binder should have stopped this. 
                            error = DataverseExtensions.DataverseError<RecordValue>($"{field.Name} should be a Dataverse Record", methodName);
                            return null;
                        }
                        var entityRef = dvr.Entity.ToEntityReference();

                        leanEntity.Attributes.Add(realAttributeName, entityRef);
                        continue;
                    }
                }

                try
                {
                    object fieldValue = amd.ToAttributeObject(field.Value);

                    string fieldName = field.Name;

                    leanEntity.Attributes.Add(fieldName, fieldValue);
                }
                catch (NotImplementedException)
                {
                    error = DataverseExtensions.DataverseError<RecordValue>($"Key {field.Name} with type {amd.AttributeType.Value}/{field.Value.Type} is not supported yet.", methodName);
                    return null;
                }
            }

            return leanEntity;
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
            return DValue<T>.Of(FormulaValue.NewError(GetExpressionError(message, messageKey: method)));
        }

        // Call IOrganizationService and translate responses. 
        // This should be the one place we translate from IOrganizationClient failures.
        public static DataverseResponse<T> DataverseCall<T>(Func<T> call, string operationDescription)
        {
            string message;
            try
            {
                // Will throw on error 
                try
                {
                    T result = call();

                    // Success. 
                    return new DataverseResponse<T>(result);
                }
                catch (AggregateException ae)
                {
                    // Unwrap aggregates
                    throw ae.InnerException;
                }
            }
            catch (FaultException<OrganizationServiceFault> e)
            {
                // thrown by server- server received command and it failed. Like missing record, etc.
                // Details contain things like:
                // - http status code 

                message = e.Message;                
            }
            catch (MessageSecurityException e)
            {
                // thrown if we can't auth to server.                 
                message = e.Message;
            }
            catch (InvalidOperationException e)
            {
                // Retrieve on missing entity will throw System.Net.WebException, which derives from System.InvalidOperationException
                message = e.Message;
            }
            catch (IOException e)
            {
                // Network is bad - such as network is offline. 
                message = e.Message;
            }
            catch (Exception e)
            {
                // Need to handle other Dataverse exceptions
                // https://github.com/microsoft/Power-Fx-Dataverse/issues/51
                if (e.GetType().FullName == "Microsoft.PowerPlatform.Dataverse.Client.Utils.DataverseOperationException")
                {
                    message = e.Message;
                }
                else
                {
                    // Any other exception is a "hard failure" that should propagate up. 
                    // this will terminate the eval. 
                    throw;
                }
            }

            var fullMessage = $"Error attempting {operationDescription}. {message}";
            return DataverseResponse<T>.NewError(fullMessage);
        }
    }
}
