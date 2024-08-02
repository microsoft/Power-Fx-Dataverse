// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Data.Common;
using System.Net;
using System.Reflection;
using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Microsoft.Extensions.Azure;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Dataverse;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Newtonsoft.Json.Linq;
using Xunit.Abstractions;

namespace Microsoft.PowerFx.AzureStorage.Tests
{
    public class DelegationTests
    {
        public readonly ITestOutputHelper _output;

        public DelegationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static string connectionString = $"<azuretableconnectionstring>";

        [Fact(Skip = "Live test")]
        public async Task FirstAzureTableTest()
        {
            using TestAzureTable tat = new TestAzureTable("Table", _output);
            TableClient tableClient = tat.TableClient;
            RecordType recordType = tat.RecordType;
            string tableName = tat.TableName;

            AzureTableValue atv = new AzureTableValue(tableClient, recordType);
            PowerFxConfig config = new PowerFxConfig();

            SymbolValues sv = new SymbolValues("Delegable_19");
            sv.Add(tableName, atv);

            RecalcEngine engine = new RecalcEngine(config);
            engine.EnableDelegation();

            string expr = $"First({tableName})";
            CheckResult check = new CheckResult(engine).SetText(expr).SetBindingInfo(sv.SymbolTable);

            List<ExpressionError> errors = check.ApplyErrors().ToList();
            Assert.Empty(errors);

            string actualIr = check.GetCompactIRString();
            Assert.Equal<object>($"__retrieveSingle({tableName}, __noop(), __noop(), )", actualIr);

            IExpressionEvaluator eval = check.GetEvaluator();
            FormulaValue result = eval.EvalAsync(CancellationToken.None, new RuntimeConfig(sv)).Result;

            RecordValue rv = Assert.IsAssignableFrom<RecordValue>(result);
            Assert.IsAssignableFrom<DecimalValue>(rv.GetField("I"));
            Assert.IsAssignableFrom<StringValue>(rv.GetField("Str"));
        }

        [Fact(Skip = "Live test")]
        public async Task ForAllAzureTableTest()
        {
            using TestAzureTable tat = new TestAzureTable("Table", _output);
            TableClient tableClient = tat.TableClient;
            RecordType recordType = tat.RecordType;
            string tableName = tat.TableName;

            AzureTableValue atv = new AzureTableValue(tableClient, recordType);
            PowerFxConfig config = new PowerFxConfig();

            SymbolValues sv = new SymbolValues("Delegable_19");
            sv.Add(tableName, atv);

            RecalcEngine engine = new RecalcEngine(config);
            engine.EnableDelegation();

            string expr = $"ForAll({tableName}, Str)";
            CheckResult check = new CheckResult(engine).SetText(expr).SetBindingInfo(sv.SymbolTable);

            List<ExpressionError> errors = check.ApplyErrors().ToList();
            Assert.Empty(errors);

            string actualIr = check.GetCompactIRString();
            Assert.Equal<object>($@"__retrieveMultiple({tableName}, __noop(), __noop(), 1000, , {{Value:Str}})", actualIr);

            IExpressionEvaluator eval = check.GetEvaluator();
            FormulaValue result = eval.EvalAsync(CancellationToken.None, new RuntimeConfig(sv)).Result;

            TableValue tv = Assert.IsAssignableFrom<TableValue>(result);
            Assert.Equal(10, tv.Rows.Count());

            RecordValue rv = tv.Rows.First().Value;
            StringValue strV = Assert.IsAssignableFrom<StringValue>(rv.GetField("Value"));
            Assert.Equal("String_", strV.Value.Substring(0, 7));
        }

        private class TestAzureTable : IDisposable
        {
            public string TableName { get; }

            public TableClient TableClient { get; }

            public RecordType RecordType
            {
                get
                {
                    RecordType rt = RecordType.Empty();
                    rt = rt.Add("Str", FormulaType.String);
                    rt = rt.Add("I", FormulaType.Decimal);
                    return rt;
                }
            }

            private readonly TableServiceClient _tableServiceClient;

            private readonly ITestOutputHelper _output;

            private bool _disposedValue;

            public TestAzureTable(string tableNamePrefix, ITestOutputHelper output)
            {
                _tableServiceClient = new TableServiceClient(connectionString);
                _output = output;

                (TableName, TableItem tItem) = SafeCreateTableAsync(_tableServiceClient, tableNamePrefix, CancellationToken.None).Result;
                _output.WriteLine($"Created table {TableName}");

                TableClient = new TableClient(connectionString, TableName);

                for (int i = 0; i < 10; i++)
                {
                    TestTableEntity tte = new TestTableEntity(i);
                    TableClient.AddEntity(tte, CancellationToken.None);
                }
            }

            protected virtual void Dispose(bool disposing)
            {
                if (!_disposedValue)
                {
                    if (disposing)
                    {
                        Response delete = _tableServiceClient.DeleteTable(TableName, CancellationToken.None);
                        _output.WriteLine($"Delete Http Status {delete.Status}");
                    }

                    _disposedValue = true;
                }
            }

            public void Dispose()
            {
                // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
        }

        private class TestTableEntity : ITableEntity
        {
            private string _partitionKey;

            private string _rowKey;

            private DateTimeOffset? _timestamp;

            private ETag _eTag;

            public TestTableEntity(int i)
            {
                Str = $"String_{i}";
                I = i;

                _partitionKey = "pKey181";
                _rowKey = Guid.NewGuid().ToString();
                _timestamp = DateTimeOffset.UtcNow;
                _eTag = default;
            }

            public string Str { get; set; }

            public int I { get; set; }

            public string PartitionKey
            {
                get => _partitionKey;
                set => _partitionKey = value;
            }

            public string RowKey
            {
                get => _rowKey;
                set => _rowKey = value;
            }

            public DateTimeOffset? Timestamp
            {
                get => _timestamp;
                set => _timestamp = value;
            }

            public ETag ETag
            {
                get => _eTag;
                set => _eTag = value;
            }
        }

        private static async Task<(string, TableItem)> SafeCreateTableAsync(TableServiceClient tableServiceClient, string tableNamePrefix, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int i = 0;

            while (true)
            {
                try
                {
                    string tableName = $"{tableNamePrefix}{++i}";
                    Response<TableItem> tableItem = await tableServiceClient.CreateTableIfNotExistsAsync(tableName, CancellationToken.None);
                    int status = tableItem.GetRawResponse().Status;

                    if (status == 204)
                    {
                        return (tableName, tableItem.Value);
                    }

                    if (i > 100)
                    {
                        throw new InvalidOperationException("Too many retries");
                    }
                }
                catch (RequestFailedException)
                {
                    // If a table is pending deletion we'll get this exception
                }
            }
        }
    }
}
