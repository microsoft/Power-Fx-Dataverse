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
        public static async Task<CustomApiSignature> GetApiSignatureAsync(
            this IDataverseReader reader,
            string logicalName, 
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sig = await reader.GetDataverseObjectAsync<CustomApiSignature>("uniquename", logicalName, cancellationToken)
                .ConfigureAwait(false);

            // $$$ What if it's missing, not found?
            return sig;
        }
    }
}
