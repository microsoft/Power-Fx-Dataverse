using Microsoft.PowerFx;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse
{
    // Represent a single Custom API. 
    // Input: single Record, each field is a parameter
    // Output: a scalar if API has 1 output, else a record of outputs. 
    // cr378_TestApi2({ cr378_Param1 : 1})
    internal class CustomApiFunction : ReflectionFunction
    {
        private readonly string _name;

        private readonly DataverseConnection _dataverseConnection;

        private readonly CustomApiSignature _signature;

        public RecordType InputParameters { get; private set; }
        public FormulaType OutputParameters { get; private set; }

        // Custom APIs by default appear in "Environment" namespace. 
        public static DPath EnvironmentNamespace = DPath.Root.Append(new DName("Environment"));

        // For 0-param cases, use signature Foo() instead of Foo({}).
        private static FormulaType[] Norm(RecordType inputType)
        {
            if (!inputType.FieldNames.Any())
            {
                return new FormulaType[0];
            }
            return new FormulaType[1] { inputType };
        }

        public CustomApiFunction(
            DataverseConnection dataverseConnection,
            CustomApiSignature signature,
            FormulaType returnType,
            RecordType inputType) : base(EnvironmentNamespace, signature.Api.uniquename, returnType, Norm(inputType))
        {
            // To invoke, use uniqueName
            // Also support display names: https://github.com/microsoft/Power-Fx-Dataverse/issues/389
            _name = signature.Api.uniquename;
            _signature = signature;
            _dataverseConnection = dataverseConnection;

            this.InputParameters = inputType;
            this.OutputParameters = returnType;

            // May need other services, like tracing:
            // https://github.com/microsoft/Power-Fx/issues/2001
            this.ConfigType = typeof(IDataverseExecute);
        }

        // Callback invoked by Power Fx engine during expression execution. 
        public async Task<FormulaValue> Execute(IDataverseExecute invoker, RecordValue namedArgs, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();

            // Don't invoke if there are any incoming errors. 
            foreach (var fields in namedArgs.Fields)
            {
                if (fields.Value is ErrorValue error)
                {
                    return error;
                }
            }

            ParameterCollection inputs = new ParameterCollection();
            CustomApiMarshaller.Fx2Inputs(inputs, namedArgs, this._signature.Inputs);

            var request = new OrganizationRequest(_name)
            {
                Parameters = inputs
            };

            var resp2 = await invoker.ExecuteAsync(request, cancel).ConfigureAwait(false);

            resp2.ThrowEvalExOnError();
            OrganizationResponse resp = resp2.Response;

            var result = CustomApiMarshaller.Outputs2Fx(resp.Results, this._signature.Outputs, _dataverseConnection);

            return result;
        }
    }
}
