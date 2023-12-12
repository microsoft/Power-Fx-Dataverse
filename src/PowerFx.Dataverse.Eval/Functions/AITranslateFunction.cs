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
    public abstract class AITranslateFunctionBase : ReflectionFunction
    {
        public AITranslateFunctionBase()
                : base("AITranslate",
                      FormulaType.String,
                      FormulaType.String)
        {
            this.ConfigType = typeof(IDataverseExecute);
        }

        public AITranslateFunctionBase(FormulaType fieldType)
                : base("AITranslate", 
                      FormulaType.String, 
                      FormulaType.String, 
                      fieldType)
        {
            this.ConfigType = typeof(IDataverseExecute);
        }

        // POCO for the OrganizationRequest message.
        protected class TranslateRequest
        {
            /// <summary>
            /// The incoming text. 
            /// </summary>
            public string Text { get; set; }

            /// <summary>
            /// The target language to translate the text to. 
            /// </summary>
            public string TargetLanguage { get; set; }

            public OrganizationRequest Get()
            {
                var req = new OrganizationRequest("AITranslate");
                req[nameof(TranslateRequest.Text)] = this.Text;
                req[nameof(TranslateRequest.TargetLanguage)] = this.TargetLanguage;

                return req;
            }
        }

        // POCO for the OrganizationResponse message.
        protected class TranslateResponse
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

        protected async Task<string> TranslatedText(string myText, string myTargetLanguage, IDataverseExecute service, CancellationToken cancel)
        {
            var req = new TranslateRequest
            {
                Text = myText,
                TargetLanguage = myTargetLanguage
            }.Get();

            var result = await service.ExecuteAsync(req, cancel);
            result.ThrowEvalExOnError();

            var response = TranslateResponse.Parse(result.Response);

            return response.TranslatedText;
        }
    }

    public class AITranslateFunction : AITranslateFunctionBase
    {
        public AITranslateFunction() 
            : base()
        {
        }

        // Entry called by Power Fx interpreter. 
        public async Task<StringValue> Execute(IDataverseExecute client, StringValue value, CancellationToken cancel)
        {
            if (client == null)
            {
                // We missed a call to AddDataverseExecute in the runtime config.
                throw new CustomFunctionErrorException("Org not available");
            }

            var result = await TranslatedText(value.Value, null, client, cancel);


            return FormulaValue.New(result);
        }
    }

    public class AITranslateFunctionWithLanguage : AITranslateFunctionBase
    {
        public AITranslateFunctionWithLanguage()
            : base(FormulaType.String)
        {
        }

        // Entry called by Power Fx interpreter. 
        public async Task<StringValue> Execute(IDataverseExecute client, StringValue value, StringValue targetLanguage, CancellationToken cancel)
        {
            if (client == null)
            {
                // We missed a call to AddDataverseExecute in the runtime config.
                throw new CustomFunctionErrorException("Org not available");
            }

            var result =  await TranslatedText(value.Value, targetLanguage.Value, client, cancel);
             

            return FormulaValue.New(result);
        }
    }
}
