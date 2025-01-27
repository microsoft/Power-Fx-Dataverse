// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.AzureStorage
{
    // https://learn.microsoft.com/en-us/dotnet/api/overview/azure/data.tables-readme?view=azure-dotnet
    [DebuggerDisplay("AzureTable({TableName})")]
    public class AzureTableValue : TableValue, IDelegatableTableValue
    {
        internal static readonly TypeMarshallerCache _marshaller = new TypeMarshallerCache().WithDynamicMarshallers(new TableEntityMarshaller());

        // Live connection to azure table.
        private readonly TableClient _tableClient;

        public string TableName => _tableClient.Name;

        public AzureTableValue(TableClient tableClient, RecordType recordType)
            : base(recordType)
        {
            _tableClient = tableClient ?? throw new ArgumentNullException(nameof(tableClient));
        }

        // Don't implement since we should be using delegation
        public override IEnumerable<DValue<RecordValue>> Rows => throw new NotImplementedException();

        public DelegationParameterFeatures SupportedFeatures => throw new NotImplementedException();

        public async Task<IReadOnlyCollection<DValue<RecordValue>>> GetRowsAsync(IServiceProvider services, DelegationParameters parameters, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();

            string oDataFilter = parameters.GetOdataFilter();

            int? top = parameters.Top;
            IEnumerable<string> select = parameters.GetColumns(); // which columns.

            // $$$ at which point do we get the error?
            var pages = _tableClient.Query<TableEntity>(
                filter: oDataFilter,
                maxPerPage: top,
                select: select,
                cancellationToken: cancel);

            var results = new List<DValue<RecordValue>>();

            foreach (TableEntity qEntity in pages)
            {
                RecordValue fxValue = (RecordValue)_marshaller.Marshal(qEntity); // $$$ better?

                // $$$ Ensure it has standard type?
                var dvalue = DValue<RecordValue>.Of(fxValue);
                results.Add(dvalue);
            }

            return results;
        }

        public Task<FormulaValue> ExecuteQueryAsync(IServiceProvider services, DelegationParameters parameters, CancellationToken cancel)
        {
            throw new NotImplementedException();
        }
    }
}
