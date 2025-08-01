﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.ServiceModel;
using System.ServiceModel.Security;
using Microsoft.PowerFx.Interpreter;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

namespace Microsoft.PowerFx.Dataverse
{
    internal static class DataverseExtensions
    {
        public static bool Execute<TReq, TRes, TOut>(this IOrganizationService svcClient, TReq request, Func<TRes, TOut> transform, out TOut result)
            where TReq : OrganizationRequest
            where TRes : OrganizationResponse
        {
            var resp = DataverseCall<TRes>(() => (TRes)svcClient.Execute(request), request.RequestName);
            if (resp.HasError)
            {
                result = default;
                return false;
            }

            result = transform(resp.Response);
            return true;
        }

        public static void ValidateNameOrThrowEvalEx(this OrganizationResponse response, string name)
        {
            if (response.ResponseName != name)
            {
                throw new CustomFunctionErrorException($"Expected response: {name}. Got {response.ResponseName}");
            }
        }

        /// <summary>
        /// Get result as given type, or throw a <see cref="CustomFunctionErrorException"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameters"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        /// <exception cref="CustomFunctionErrorException">.</exception>
        public static T GetOrThrowEvalEx<T>(this ParameterCollection parameters, string name)
        {
            if (parameters.TryGetValue(name, out var value))
            {
                if (value is T str)
                {
                    return str;
                }
                else
                {
                    throw new CustomFunctionErrorException($"Response should be {typeof(T).FullName}. Actual: {value.GetType().FullName}");
                }
            }
            else
            {
                var keys = string.Join(",", parameters.Keys.ToArray());
                throw new CustomFunctionErrorException(
                    $"Response missing {name}. Keys: {keys}");
            }
        }

        // Record should already be logical names.
        public static Entity ConvertRecordToEntity(this RecordValue record, EntityMetadata metadata, out DValue<RecordValue> error, [CallerMemberName] string methodName = null)
        {
            // Contains only the modified fields.
            Entity leanEntity = new Entity(metadata.LogicalName);

            error = null;

            foreach (NamedValue field in record.OriginalFields)
            {
                if (field.Value is ErrorValue ev)
                {
                    error = DataverseExtensions.DataverseError<RecordValue>($"Field {field.Name} is of type ErrorValue: {string.Join("\r\n", ev.Errors.Select(er => er.Message))}", methodName);
                    return null;
                }

                if (!metadata.TryGetAttribute(field.Name, out var amd))
                {
                    if (metadata.TryGetRelationship(field.Name, out var realAttributeName))
                    {
                        // Get primary key, set as guid.

                        if (field.Value is DataverseRecordValue drv)
                        {
                            leanEntity.Attributes.Add(realAttributeName, drv.Entity.ToEntityReference());
                        }
                        else
                        {
                            // Binder should have stopped this.
                            error = DataverseExtensions.DataverseError<RecordValue>($"{field.Name} should be a Dataverse Record", methodName);

                            return null;
                        }

                        continue;
                    }
                    else if (metadata.TryGetOneToManyRelationship(field.Name, out var relationship))
                    {
                        error = DataverseExtensions.DataverseError<RecordValue>($"One to Many Relations is not supported yet: {field.Name}  {metadata.LogicalName}", methodName);
                        return null;
                    }
                    else
                    {
                        error = DataverseExtensions.DataverseError<RecordValue>($"Key {field.Name} not found in {metadata.LogicalName}", methodName);
                        return null;
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
                catch (InvalidOperationException invalidOperationException)
                {
                    error = DataverseExtensions.DataverseError<RecordValue>(invalidOperationException.Message, methodName);
                    return null;
                }
            }

            return leanEntity;
        }

        private static string DisplayName(this Type t)
        {
            if (!t.IsGenericType)
            {
                return t.Name;
            }

            string genericTypeName = t.GetGenericTypeDefinition().Name;
            genericTypeName = genericTypeName.Substring(0, genericTypeName.IndexOf('`'));

            string genericArgs = string.Join(",", t.GetGenericArguments().Select(ta => DisplayName(ta)).ToArray());

            return genericTypeName + "<" + genericArgs + ">";
        }

        public static DValue<T> DataverseError<T>(string message, string method)
            where T : ValidFormulaValue
        {
            return DValue<T>.Of(FormulaValue.NewError(DataverseHelpers.GetExpressionError(message, messageKey: method)));
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
