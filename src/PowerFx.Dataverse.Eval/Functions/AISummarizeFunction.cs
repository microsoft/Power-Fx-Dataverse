﻿// Copyright (c) Microsoft Corporation.
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
    // AISummarize(String) : string
    // given a string, call GPT to return a summarized version of the string.
    public class AISummarizeFunction : ReflectionFunction
    {
        public AISummarizeFunction()
        {
            this.ConfigType = typeof(IDataverseExecute);
        }

        // POCO for the OrganizationRequest message.
        private class SummarizeRequest
        {
            /// <summary>
            /// The incoming text.
            /// </summary>
            public string Text { get; set; }

            public OrganizationRequest Get()
            {
                var req = new OrganizationRequest("AISummarize");
                req[nameof(SummarizeRequest.Text)] = this.Text;

                return req;
            }
        }

        // POCO for the OrganizationResponse message.
        private class SummarizeResponse
        {
            public string SummarizedText { get; set; }

            public static SummarizeResponse Parse(OrganizationResponse res)
            {
                res.ValidateNameOrThrowEvalEx("AISummarize");

                var str = res.Results.GetOrThrowEvalEx<string>(nameof(SummarizeResponse.SummarizedText));

                return new SummarizeResponse
                {
                    SummarizedText = str
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

            var result = await SummarizedText(value.Value, client, cancel);

            return FormulaValue.New(result);
        }

        private async Task<string> SummarizedText(string myText, IDataverseExecute service, CancellationToken cancel)
        {
            var req = new SummarizeRequest
            {
                Text = myText
            }.Get();

            var result = await service.ExecuteAsync(req, cancel);
            result.ThrowEvalExOnError();

            var response = SummarizeResponse.Parse(result.Response);

            return response.SummarizedText;
        }
    }
}
