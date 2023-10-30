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

            // $$$ "uniquename" or "name"? 
            var sig = await reader.GetDataverseObjectAsync<CustomApiSignature>("name", logicalName, cancellationToken)
                .ConfigureAwait(false);

            // $$$ What if it's missing, not found?
            return sig;
        }
    }


    // Default implementation of ICustomApiRestore
    public class CustomApiRestore 
    {
        // Needed to resolve Entity/EntityReference to a FormulaType
        // These could be null if we ust use primitives. 
        // $$$ Can we get _metadataCache from the DVC?
        private readonly CdsEntityMetadataProvider _metadataCache;

        public CustomApiRestore(CdsEntityMetadataProvider metadataCache)
        {
            _metadataCache = metadataCache;
        }

        public CustomApiFunction ToFunction(CustomApiSignature model, DataverseConnection dataverseConnection)
        {
            return ToFunction(model, _metadataCache, dataverseConnection);
        }

        public static CustomApiFunction ToFunction(CustomApiSignature model, CdsEntityMetadataProvider metadataCache, DataverseConnection dataverseConnection)
        {
            // Inputs are always as a record. Enables named input parameters. 
            RecordType inRecord = CustomApiMarshaller.GetInputType(model.Inputs, metadataCache);

            // If multiple return types, then use a record. 
            FormulaType outType = CustomApiMarshaller.GetOutputType(model.Outputs, metadataCache);

            var apiFunc = new CustomApiFunction(
                dataverseConnection,
                model,
                outType,
                inRecord);

            return apiFunc;
        }
    }
}
