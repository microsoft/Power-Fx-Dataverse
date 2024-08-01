// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.PowerFx.Dataverse;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.AzureStorage
{
    public static class AzureTableExtensions
    {
        /// <summary>
        /// Read the Azure Table to infer the schema.
        /// </summary>
        /// <param name="tableClient"></param>
        /// <param name="cancel"></param>
        /// <returns></returns>
        public static async Task<AzureTableValue> NewAsync(this TableClient tableClient, CancellationToken cancel = default)
        {
            var recordType = await InferRecordType(tableClient, cancel);

            var tableValue = new AzureTableValue(tableClient, recordType);
            return tableValue;
        }

        // Azure Tables are schema-less. Read a few rows and see.
        public static async Task<RecordType> InferRecordType(this TableClient tableClient, CancellationToken cancel = default)
        {
            //TableClient.CreateQueryFilter(new FormattableString("PartitionKey eq 'pk'"))

            Pageable<TableEntity> r = tableClient.Query<TableEntity>(maxPerPage: 10, cancellationToken: cancel);

            foreach (var page in r.AsPages())
            {
                foreach (TableEntity entity in page.Values)
                {
                    IDictionary<string, object> keyValuePairs = entity;

                    var val = AzureTableValue._marshaller.Marshal(entity, typeof(TableEntity));

                    var recordVal = (RecordValue)val;
                    var type = recordVal.Type;

                    return type;
                }
            }

            throw new InvalidOperationException($"Can't infer table type");
        }
    }
}
