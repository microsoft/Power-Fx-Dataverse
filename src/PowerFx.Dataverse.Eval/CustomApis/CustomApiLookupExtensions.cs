using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Lookup a Custom API signature from dataverse
    /// </summary>
    internal static class CustomApiLookupExtensions
    {
        /// <summary>
        /// Lookup an API signature given its logical name (aka uniqueName). 
        /// </summary>
        /// <param name="reader">reader to access dataverse metadata, which is stored in tables like 
        /// customapi, customapirequestparameter, customapiresponseproperty.</param>
        /// <param name="logicalName">logical name of the API. this will include a prefix.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>the signature object. </returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <remarks>
        /// See description of https://learn.microsoft.com/en-us/power-apps/developer/data-platform/custom-api-tables?tabs=webapi.
        /// </remarks>
        public static async Task<CustomApiSignature> GetApiSignatureAsync(
            this IDataverseReader reader,
            string logicalName, 
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string filterName = "uniquename";
            var sig = await reader.GetDataverseObjectAsync<CustomApiSignature>(filterName, logicalName, cancellationToken)
                .ConfigureAwait(false);

            if (sig == null)
            {
                throw new InvalidOperationException($"No signature found where '{filterName}' is '{logicalName}'");
            }

            return sig;
        }
    }
}
