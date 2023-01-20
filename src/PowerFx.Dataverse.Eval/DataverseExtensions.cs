//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Connectors;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Security;
using static Microsoft.PowerFx.Dataverse.DataverseHelpers;

namespace Microsoft.PowerFx.Dataverse
{
    internal static class DataverseExtensions
    {
        /// <summary>
        /// Helper to get all Logical 2 Display name map for the entire org. 
        /// This efficiently fetches the table names, but not the metadata. 
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        public static AllTablesDisplayNameProvider GetDisplayNames(this IOrganizationService client)
        {
            RetrieveAllEntitiesRequest req = new RetrieveAllEntitiesRequest
            {
                EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters.Entity
            };
            var resp = (RetrieveAllEntitiesResponse)client.Execute(req);

            var map = new AllTablesDisplayNameProvider();
            foreach (var entity in resp.EntityMetadata)
            {
                var displayName = GetDisplayName(entity);
                map.Add(entity.LogicalName, displayName);
            }

            return map;
        }

        private static string GetDisplayName(EntityMetadata entity)
        {
            return entity.DisplayCollectionName?.UserLocalizedLabel?.Label ?? entity.LogicalName;
        }

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

        public static Entity ToEntity(this RecordValue record, EntityMetadata entityMetadata)
        {
            Entity entity = new(entityMetadata.LogicalName);

            foreach (NamedValue field in record.Fields)
            {
                entity.Attributes.Add(field.Name, field.Value.ToObject());
            }

            return entity;
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

        // Return 0 if not found. 
        private static int GetHttpStatusCode(FaultException<OrganizationServiceFault> e)
        {
            return GetHttpStatusCode(e.Detail);
        }

        private static int GetHttpStatusCode(OrganizationServiceFault e)
        {
            var props = e?.ErrorDetails;
            if (props != null)
            {
                if (props.TryGetValue("ApiExceptionHttpStatusCode", out var data))
                { 
                    if (data is int code)
                    {
                        return code;
                    }                    
                }
            }
            return 0;
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
            catch (IOException e)
            {
                // Network is bad - such as network is offline. 
                message = e.Message;
            }
            catch
            {
                // Any other exception is a "hard failure" that should propagate up. 
                // this will terminate the eval. 
                throw;
            }

            var fullMessage = $"Error attempting {operationDescription}. {message}";
            return DataverseResponse<T>.NewError(fullMessage);
        }
    }
}
