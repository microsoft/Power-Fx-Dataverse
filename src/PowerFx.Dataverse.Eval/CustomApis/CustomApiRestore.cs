using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Convert from a dataverse signature to a Power Fx function.
    /// </summary>
    internal class CustomApiRestore
    {
        // Needed to resolve Entity/EntityReference to a FormulaType
        // These could be null if we just use primitives. 
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
            var marshaller = new CustomApiParameterMarshaller(metadataCache);

            // Inputs are always as a record. Enables named input parameters. 
            RecordType inRecord = CustomApiMarshaller.GetInputType(model.Inputs, marshaller);

            // If multiple return types, then use a record. 
            FormulaType outType = CustomApiMarshaller.GetOutputType(model.Outputs, marshaller);

            var apiFunc = new CustomApiFunction(
                dataverseConnection,
                model,
                outType,
                inRecord);

            return apiFunc;
        }
    }
}
