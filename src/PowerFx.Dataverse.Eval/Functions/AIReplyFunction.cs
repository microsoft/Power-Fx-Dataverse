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
    // AIReply(String) : string 
    // given a string, call GPT to return a response to the string. 
    public class AIReplyFunction : ReflectionFunction
    {
        public AIReplyFunction()
        {
            this.ConfigType = typeof(IDataverseExecute);
        }

        // POCO for the OrganizationRequest message.
        private class ReplyRequest
        {
            /// <summary>
            /// The incoming text/question. 
            /// </summary>
            public string Text { get; set; }

            public OrganizationRequest Get()
            {
                var req = new OrganizationRequest("AIReply");
                req[nameof(ReplyRequest.Text)] = this.Text;

                return req;
            }
        }

        // POCO for the OrganizationResponse message.
        private class ReplyResponse
        {
            public string PreparedResponse { get; set; }

            public static ReplyResponse Parse(OrganizationResponse res)
            {
                res.ValidateNameOrThrowEvalEx("AIReply");

                var str = res.Results.GetOrThrowEvalEx<string>(nameof(ReplyResponse.PreparedResponse));

                return new ReplyResponse
                {
                    PreparedResponse = str
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

            var result = await RepliedText(value.Value, client, cancel);

            return FormulaValue.New(result);
        }

        private async Task<string> RepliedText(string myText, IDataverseExecute service, CancellationToken cancel)
        {
            var req = new ReplyRequest
            {
                Text = myText
            }.Get();

            var result = await service.ExecuteAsync(req, cancel);
            result.ThrowEvalExOnError();

            var response = ReplyResponse.Parse(result.Response);

            return response.PreparedResponse;
        }
    }
}
