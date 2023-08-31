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
    // AIClassify(String, Table) : string 
    // given a string and categories call GPT to return the best-fit category. 
    public class AIClassifyFunction : ReflectionFunction
    {
        public AIClassifyFunction()
            : base("AIClassify",
                  FormulaType.String,
                  FormulaType.String,
                  RecordType.Empty().Add("Value", FormulaType.String).ToTable())
        {
            this.ConfigType = typeof(IDataverseExecute);
        }

        // POCO for the OrganizationRequest message.
        private class ClassifyRequest
        {
            /// <summary>
            /// The incoming text. 
            /// </summary>
            public string Text { get; set; }
            public TableValue Categories { get; set; }

            public OrganizationRequest Get()
            {
                var req = new OrganizationRequest("AIClassify");
                req[nameof(ClassifyRequest.Text)] = this.Text;
                req[nameof(ClassifyRequest.Categories)] = this.Categories;

                return req;
            }
        }

        // POCO for the OrganizationResponse message.
        private class ClassifyResponse
        {
            public string Classification { get; set; }

            public static ClassifyResponse Parse(OrganizationResponse res)
            {
                res.ValidateNameOrThrowEvalEx("AIClassify");

                var str = res.Results.GetOrThrowEvalEx<string>(nameof(ClassifyResponse.Classification));

                return new ClassifyResponse
                {
                    Classification = str
                };
            }
        }

        // Entry called by Power Fx interpreter. 
        public async Task<StringValue> Execute(IDataverseExecute client, StringValue value, TableValue categories, CancellationToken cancel)
        {
            if (client == null)
            {
                // We missed a call to AddDataverseExecute in the runtime config.
                throw new CustomFunctionErrorException("Org not available");
            }

            var result = await Classification(value.Value, categories, client, cancel);

            return FormulaValue.New(result);
        }

        private async Task<string> Classification(string myText, TableValue myCategories, IDataverseExecute service, CancellationToken cancel)
        {
            var req = new ClassifyRequest
            {
                Text = myText,
                Categories = myCategories,
            }.Get();

            var result = await service.ExecuteAsync(req, cancel);
            result.ThrowEvalExOnError();

            var response = ClassifyResponse.Parse(result.Response);

            return response.Classification;
        }
    }
}
