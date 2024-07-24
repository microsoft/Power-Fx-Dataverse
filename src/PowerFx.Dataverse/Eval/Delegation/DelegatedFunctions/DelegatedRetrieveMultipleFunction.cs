using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Executes a query against a table and returns a table.
    /// First Arg is the table to query, Second Arg is the filter to apply, Third Arg is the number of records to return.
    /// </summary>
    internal class DelegatedRetrieveMultipleFunction : DelegateFunction
    {
        public DelegatedRetrieveMultipleFunction(DelegationHooks hooks, TableType tableType)
          : base(hooks, "__retrieveMultiple", tableType)
        {
        }

        // arg0: table
        // arg1: filter
        // arg2: orderby
        // arg3: top
        protected override async Task<FormulaValue> ExecuteAsync(IServiceProvider services, FormulaValue[] args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (args[0] is not IDelegatableTableValue table)
            {
                throw new InvalidOperationException($"args0 should always be of type {nameof(TableValue)} : found {args[0]}");
            }

            int? topCount = null;
            FilterExpression filter;
            IList<OrderExpression> orderBy;
            ISet<LinkEntity> relation;
            string partitionId = null;

            if (args[3] is NumberValue count)
            {
                topCount = (int)(count).Value;
            }
            else if (args[3] is BlankValue)
            {
                // If Count is Blank(), return empty table.
                var emptyList = new List<DValue<RecordValue>>();
                return new InMemoryTableValue(IRContext.NotInSource(this.ReturnFormulaType), emptyList);
            }
            else
            {
                throw new InvalidOperationException($"args3 should always be of type {nameof(NumberValue)} or {nameof(BlankValue)} : found {args[2]}");
            }

            if (args[1] is DelegationFormulaValue DelegationFormulaValue)
            {
                filter = DelegationFormulaValue._filter;                
                relation = DelegationFormulaValue._relation;
                partitionId = DelegationFormulaValue._partitionId;                
            }
            else
            {
                throw new InvalidOperationException($"args1 should always be of type {nameof(DelegationFormulaValue)} : found {args[1]}");
            }

            if (args[2] is DelegationFormulaValue DelegationFormulaValue2)
            {                
                orderBy = DelegationFormulaValue2._orderBy;             
            }
            else
            {
                throw new InvalidOperationException($"args2 should always be of type {nameof(DelegationFormulaValue)} : found {args[1]}");
            }

            bool isDistinct = false;
            if (args[4] is BooleanValue bv)
            {
                isDistinct = bv.Value;
            }
            else
            {
                throw new InvalidOperationException($"args4 should always be of type {nameof(BooleanValue)} : found {args[3]}");
            }
            
            // column names to fetch. if kept null, fetches all columns.
            IEnumerable<string> columns = null;
            Dictionary<string, string> columnMap = null;

            if (args.Length > 5)
            {
                if (args[5] is RecordValue map)
                {
                    columnMap = map.Fields.ToDictionary(f => f.Name, f => f.Value is StringValue sv ? sv.Value : throw new InvalidOperationException($"Invalid type in column map, got {f.Value.GetType().Name}"));
                }
                else
                {
                    columns = args.Skip(5).Select((x, i) => x is StringValue sv ? sv.Value : throw new InvalidOperationException($"From args{5 + i} onwards, all args should have been type {nameof(StringValue)} : found {args[5 + i]}"));
                }
            }

#pragma warning disable CS0618 // Type or member is obsolete
            var delegationParameters = new DataverseDelegationParameters
            {
                Filter = filter,
                OrderBy = orderBy,
                Top = topCount,

                _columnSet = columns,
                _columnMap = columnMap,
                _isDistinct = isDistinct,
                _partitionId = partitionId,
                _relation = relation
            };
#pragma warning restore CS0618 // Type or member is obsolete

            var rows = await _hooks.RetrieveMultipleAsync(services, table, delegationParameters, cancellationToken).ConfigureAwait(false);

            // Distinct outputs always in default single column table.
            if (isDistinct)
            {
                if (columns == null || !columns.Any() || columns.Count() > 1)
                {
                    throw new InvalidOperationException("Distinct requires single column to be specified");
                }

                rows = ToValueColumn(rows, columns.First());
            }

            var result = new InMemoryTableValue(IRContext.NotInSource(this.ReturnFormulaType), rows);
            return result;
        }

        internal static IEnumerable<DValue<RecordValue>> ToValueColumn(IEnumerable<DValue<RecordValue>> records, string column)
        {
            foreach (var record in records)
            {
                yield return ToValueColumn(record, column);
            }
        }

        internal static DValue<RecordValue> ToValueColumn(DValue<RecordValue> record, string column)
        {
            var columnValue = record.Value.GetField(column);
            var valueRecord = FormulaValue.NewRecordFromFields(new NamedValue("Value", columnValue));
            return DValue<RecordValue>.Of(valueRecord);
        }
    }
}
