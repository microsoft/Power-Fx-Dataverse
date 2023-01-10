//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Types;
using System;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DataverseHelpers;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Allows a union between Success case (T) and an error. 
    /// 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class DataverseResponse<T> : DataverseResponse
    {
        public T Response { get; }

        public DataverseResponse(T response)
        {
            Response = response;
        }

        public static DataverseResponse<T> NewError(string error)
        {
            // Error and Response are mutually exclusive.
            return new DataverseResponse<T>(default(T))
            {
                Error = error ?? throw new ArgumentNullException(nameof(error))
            };
        }

        /// <summary>
        /// Helper for invoking a dataverse operation and translating the errors. 
        /// </summary>
        /// <param name="call"></param>
        /// <param name="operationDescription"></param>
        /// <returns></returns>
        public static async Task<DataverseResponse<T>> RunAsync(Func<Task<T>> call, string operationDescription)
        {
            return DataverseExtensions.DataverseCall(() => call().Result, operationDescription);
        }        
    }

    public class DataverseResponse
    {
        public string Error { get; protected set; }

        public bool HasError => Error != null;

        internal DValue<RecordValue> DValueError(string method) => DataverseExtensions.DataverseError<RecordValue>(Error, method);

        public ErrorValue GetErrorValue(FormulaType type)
        {
            return FormulaValue.NewError(GetExpressionError(Error), type);
        }
    }

    /// <summary>
    /// Simple way to create a DataverseResponse error instance.
    /// </summary>
    internal class DataverseResponseHasError: DataverseResponse
    {
        public DataverseResponseHasError(string error)
        {
            Error = error;
        }
    }
}
