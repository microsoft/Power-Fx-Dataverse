// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.PowerFx.Interpreter;
using Microsoft.PowerFx.Types;

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

        public static new DataverseResponse<T> NewError(string error)
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
            return FormulaValue.NewError(DataverseHelpers.GetExpressionError(Error), type);
        }

        /// <summary>
        /// Throw a  <see cref="=CustomFunctionErrorException"/> on error.
        /// This exception type is specifically useful in interpreter.
        /// </summary>
        /// <exception cref="CustomFunctionErrorException">.</exception>
        public void ThrowEvalExOnError()
        {
            if (this.HasError)
            {
                throw new CustomFunctionErrorException(this.Error);
            }
        }

        public static DataverseResponse NewError(string error)
        {
            return new DataverseResponse()
            {
                Error = error
            };
        }
    }
}
