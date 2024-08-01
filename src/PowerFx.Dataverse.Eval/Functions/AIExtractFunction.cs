// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Interpreter;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;

namespace Microsoft.PowerFx.Dataverse
{
    // AIExtract(String, String) : string array
    // given a string, call GPT to return a single column table containing the extracted values
    public class AIExtractFunction : ReflectionFunction
    {
        public AIExtractFunction() 
            : base("AIExtract", RecordType.Empty().Add("Value", FormulaType.String).ToTable(), FormulaType.String, FormulaType.String)
        {
            this.ConfigType = typeof(IDataverseExecute);
        }

        // POCO for the OrganizationRequest message.
        private class ExtractRequest
        {
            /// <summary>
            /// The incoming text.
            /// </summary>
            public string Text { get; set; }

            /// <summary>
            /// The incoming entity that will extracted from the text.
            /// </summary>
            public string Entity { get; set; }

            public OrganizationRequest Get()
            {
                var req = new OrganizationRequest("AIExtract");
                req[nameof(ExtractRequest.Text)] = this.Text;
                req[nameof(ExtractRequest.Entity)] = this.Entity;
                return req;
            }
        }

        // POCO for the OrganizationResponse message.
        private class ExtractResponse
        {
            public string[] ExtractedData { get; set; }

            public static ExtractResponse Parse(OrganizationResponse res)
            {
                res.ValidateNameOrThrowEvalEx("AIExtract");

                var stringArray = res.Results.GetOrThrowEvalEx<string[]>(nameof(ExtractResponse.ExtractedData));

                return new ExtractResponse
                {
                    ExtractedData = stringArray
                };
            }
        }

        // Entry called by Power Fx interpreter.
        public async Task<TableValue> Execute(IDataverseExecute client, StringValue text, StringValue entity, CancellationToken cancel)
        {
            if (client == null)
            {
                // We missed a call to AddDataverseExecute in the runtime config.
                throw new CustomFunctionErrorException("Org not available");
            }

            string[] result = await ExtractedData(text.Value, entity.Value, client, cancel);

            return FormulaValue.NewSingleColumnTable(result.Select(v => FormulaValue.New(v)));
        }

        private async Task<string[]> ExtractedData(string myText, string myEntity, IDataverseExecute service, CancellationToken cancel)
        {
            var req = new ExtractRequest
            {
                Text = myText,
                Entity = myEntity
            }.Get();

            var result = await service.ExecuteAsync(req, cancel);
            result.ThrowEvalExOnError();

            var response = ExtractResponse.Parse(result.Response);

            return response.ExtractedData;
        }
    }
}
