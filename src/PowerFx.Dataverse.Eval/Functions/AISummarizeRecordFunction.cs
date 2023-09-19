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
    // AISummarizeRecord(String, Guid) : string 
    // given a dataverse entity name and record Id in that entity, call GPT to return summarize that record and its linked records from other linked entities in dataverse.
    public class AISummarizeRecordFunction : ReflectionFunction
    {
        public AISummarizeRecordFunction()
            : base("AISummarizeRecord",
                  FormulaType.String,
                  FormulaType.String,
                  FormulaType.Guid)
        {
            this.ConfigType = typeof(IDataverseExecute);
        }

        // POCO for the OrganizationRequest message.
        private class AISummarizeRecordRequest
        {
            /// <summary>
            /// The incoming text. 
            /// </summary>
            public string EntityLogicalName { get; set; }
            public Guid Id { get; set; }

            public OrganizationRequest Get()
            {
                var req = new OrganizationRequest("AISummarizeRecord");
                req[nameof(AISummarizeRecordRequest.EntityLogicalName)] = this.EntityLogicalName;
                req[nameof(AISummarizeRecordRequest.Id)] = this.Id;

                return req;
            }
        }

        // POCO for the OrganizationResponse message.
        private class AISummarizeRecordResponse
        {
            public string SummarizedText { get; set; }

            public static AISummarizeRecordResponse Parse(OrganizationResponse res)
            {
                res.ValidateNameOrThrowEvalEx("AISummarizeRecord");

                var str = res.Results.GetOrThrowEvalEx<string>(nameof(AISummarizeRecordResponse.SummarizedText));

                return new AISummarizeRecordResponse
                {
                    SummarizedText = str
                };
            }
        }

        // Entry called by Power Fx interpreter. 
        public async Task<StringValue> Execute(IDataverseExecute client, StringValue entityName, GuidValue entityId, CancellationToken cancel)
        {
            if (client == null)
            {
                // We missed a call to AddDataverseExecute in the runtime config.
                throw new CustomFunctionErrorException("Organization is not available");
            }

            var result = await AISummarizeRecordAsync(entityName.Value, entityId.Value, client, cancel);

            return FormulaValue.New(result);
        }

        private async Task<string> AISummarizeRecordAsync(string entityName, Guid entityId, IDataverseExecute service, CancellationToken cancel)
        {
            var req = new AISummarizeRecordRequest
            {
                Id = entityId,
                EntityLogicalName = entityName,
            }.Get();

            var result = await service.ExecuteAsync(req, cancel);
            result.ThrowEvalExOnError();

            var response = AISummarizeRecordResponse.Parse(result.Response);

            return response.SummarizedText;
        }
    }
}
