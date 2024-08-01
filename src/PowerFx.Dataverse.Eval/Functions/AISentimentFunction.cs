// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Interpreter;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;

namespace Microsoft.PowerFx.Dataverse
{
    // AISentiment(String) : string
    // given a string, call GPT to return the analyzed sentiment of the string.
    public class AISentimentFunction : ReflectionFunction
    {
        public AISentimentFunction()
        {
            this.ConfigType = typeof(IDataverseExecute);
        }

        // POCO for the OrganizationRequest message.
        private class SentimentRequest
        {
            /// <summary>
            /// The incoming text.
            /// </summary>
            public string Text { get; set; }

            public OrganizationRequest Get()
            {
                var req = new OrganizationRequest("AISentiment");
                req[nameof(SentimentRequest.Text)] = this.Text;

                return req;
            }
        }

        // POCO for the OrganizationResponse message.
        private class SentimentResponse
        {
            public string AnalyzedSentiment { get; set; }

            public static SentimentResponse Parse(OrganizationResponse res)
            {
                res.ValidateNameOrThrowEvalEx("AISentiment");

                var str = res.Results.GetOrThrowEvalEx<string>(nameof(SentimentResponse.AnalyzedSentiment));

                return new SentimentResponse
                {
                    AnalyzedSentiment = str
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

            var result = await AnalyzedSentimentText(value.Value, client, cancel);

            return FormulaValue.New(result);
        }

        private async Task<string> AnalyzedSentimentText(string myText, IDataverseExecute service, CancellationToken cancel)
        {
            var req = new SentimentRequest
            {
                Text = myText
            }.Get();

            var result = await service.ExecuteAsync(req, cancel);
            result.ThrowEvalExOnError();

            var response = SentimentResponse.Parse(result.Response);

            return response.AnalyzedSentiment;
        }
    }
}
