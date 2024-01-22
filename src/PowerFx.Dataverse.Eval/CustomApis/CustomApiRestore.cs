using System;
using System.Collections.Generic;
using System.Linq;
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

        public CustomApiFunctionBase ToFunction(CustomApiSignature model, DataverseConnection dataverseConnection)
        {
            return ToFunction(model, _metadataCache, dataverseConnection);
        }

        public static CustomApiFunctionBase ToFunction(CustomApiSignature model, CdsEntityMetadataProvider metadataCache, DataverseConnection dataverseConnection)
        {
            var marshaller = new CustomApiParameterMarshaller(metadataCache);

            // Inputs are always as a record. Enables named input parameters. 
            RecordType inRecord = CustomApiMarshaller.GetInputType(model.Inputs, marshaller);

            // If multiple return types, then use a record. 
            FormulaType outType = CustomApiMarshaller.GetOutputType(model.Outputs, marshaller);

            if (inRecord.FieldNames.Any())
            {
                var apiFunc = new CustomApi1ArgFunction(
                    dataverseConnection,
                    model,
                    outType,
                    inRecord);

                return apiFunc;
            }
            else
            {
                var apiFunc = new CustomApi0ArgFunction(
                    dataverseConnection,
                    model,
                    outType,
                    inRecord);

                return apiFunc;
            }
        }
    }
}
