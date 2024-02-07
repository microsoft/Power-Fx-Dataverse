using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        protected override async Task<FormulaValue> ExecuteAsync(FormulaValue[] args, CancellationToken cancellationToken)
        {
            if (args[0] is not TableValue table)
            {
                throw new InvalidOperationException($"args0 should alway be of type {nameof(TableValue)} : found {args[0]}");
            }

            int? topCount = null;
            FilterExpression filter;
            ISet<LinkEntity> relation;
            if (args[2] is NumberValue count)
            {
                topCount = (int)(count).Value;
            }
            else if (args[2] is BlankValue)
            {
                // If Count is Blank(), return empty table.
                var emptyList = new List<DValue<RecordValue>>();
                return new InMemoryTableValue(IRContext.NotInSource(this.ReturnFormulaType), emptyList);
            }
            else
            {
                throw new InvalidOperationException($"args2 should alway be of type {nameof(NumberValue)} or {nameof(BlankValue)} : found {args[2]}");
            }

            if (args[1] is DelegationFormulaValue DelegationFormulaValue)
            {
                filter = DelegationFormulaValue._value;
                relation = DelegationFormulaValue._relation;
            }
            else
            {
                throw new InvalidOperationException($"args1 should alway be of type {nameof(DelegationFormulaValue)} : found {args[1]}");
            }

            bool isDistinct = false;
            if (args[3] is BooleanValue bv)
            {
                isDistinct = bv.Value;
            }
            else
            {
                throw new InvalidOperationException($"args3 should alway be of type {nameof(BooleanValue)} : found {args[3]}");
            }

            // column names to fetch. if kept null, fetches all columns.
            IEnumerable<string> columns = null;
            if(args.Length > 4)
            {
                columns = args.Skip(4).Select(x => {
                    if (x is StringValue stringValue)
                    {
                        return stringValue.Value;
                    }
                    else
                    {
                        throw new InvalidOperationException($"From Args3 onwards, all args should have been type {nameof(StringValue)} : found {args[4]}");
                    }
                });
            }

            var rows = await _hooks.RetrieveMultipleAsync(table, relation, filter, topCount, columns, isDistinct, cancellationToken).ConfigureAwait(false);

            // Distinct outputs always in default single column table.
            if (isDistinct)
            {
                if(columns == null || !columns.Any() || columns.Count() > 1)
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
