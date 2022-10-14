//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse
{
    public class DataverseResponse<T> : DataverseResponse        
    {
        internal T Response { get; }

        public DataverseResponse(T response)
            : this(null, response)
        {
        }

        public DataverseResponse(string error, T response = default)
            : base(error)
        {
            Response = response;
        }
    }

    public class DataverseResponse
    {
        public string Error { get; }

        public DataverseResponse(string error = null)
        {
            Error = error;
        }

        public bool HasError => !string.IsNullOrEmpty(Error);

        internal DValue<RecordValue> DValueError(string method) => DataverseExtensions.DataverseError<RecordValue>(Error, method);
    }
}
