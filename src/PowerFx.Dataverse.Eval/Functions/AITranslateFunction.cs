using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Interpreter;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse
{
    // AITranslate(String) : string 
    // given a string, call GPT to return a English-translated version of the string. 
    public class AITranslateFunction : ReflectionFunction
    {
        public AITranslateFunction()
        {
            this.ConfigType = typeof(IDataverseExecute);
        }

        // POCO for the OrganizationRequest message.
        private class TranslateRequest
        {
            /// <summary>
            /// The incoming text. 
            /// </summary>
            public string Text { get; set; }

            public OrganizationRequest Get()
            {
                var req = new OrganizationRequest("AITranslate");
                req[nameof(TranslateRequest.Text)] = this.Text;

                return req;
            }
        }

        // POCO for the OrganizationResponse message.
        private class TranslateResponse
        {
            public string TranslatedText { get; set; }

            public static TranslateResponse Parse(OrganizationResponse res)
            {
                res.ValidateNameOrThrowEvalEx("AITranslate");

                var str = res.Results.GetOrThrowEvalEx<string>(nameof(TranslateResponse.TranslatedText));

                return new TranslateResponse
                {
                    TranslatedText = str
                };
            }
        }

        // Entry called by Power Fx interpreter. 
        public async Task<StringValue> Execute(IDataverseExecute client, StringValue value, CancellationToken cancel)
        {
            if (client == null)
            {
                // We missed a call to AddDataverseExecute in the runtime config.
                throw new CustomFunctionErrorException("Org not available");
            }

            var result = await TranslatedText(value.Value, client, cancel);

            return FormulaValue.New(result);
        }

        private async Task<string> TranslatedText(string myText, IDataverseExecute service, CancellationToken cancel)
        {
            var req = new TranslateRequest
            {
                Text = myText
            }.Get();

            var result = await service.ExecuteAsync(req, cancel);
            result.ThrowEvalExOnError();

            var response = TranslateResponse.Parse(result.Response);

            return response.TranslatedText;
        }
    }
}
