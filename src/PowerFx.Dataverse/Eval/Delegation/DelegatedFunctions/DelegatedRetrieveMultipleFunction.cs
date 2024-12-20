// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
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

        // args[0]: table
        // args[1]: filter
        // args[2]: orderby
        // args[3]: count
        // args[4]: distinct column
        // args[5]: columns with renames (in Record)
        protected override async Task<FormulaValue> ExecuteAsync(IServiceProvider services, FormulaValue[] args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (args[0] is not IDelegatableTableValue table)
            {
                throw new InvalidOperationException($"args0 should always be of type {nameof(TableValue)} : found {args[0]}");
            }

            int? topCount = null;
            FxFilterExpression filter;
            IList<OrderExpression> orderBy;
            ISet<LinkEntity> relation;
            string partitionId = null;

            if (args[3] is NumberValue count)
            {
                topCount = (int)count.Value;
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

            if (args[1] is DelegationFormulaValue delegationFormulaValue)
            {
                filter = delegationFormulaValue._filter;
                relation = delegationFormulaValue._relation;
                partitionId = delegationFormulaValue._partitionId;
            }
            else
            {
                throw new InvalidOperationException($"args1 should always be of type {nameof(delegationFormulaValue)} : found {args[1]}");
            }

            if (args[2] is DelegationFormulaValue delegationFormulaValue2)
            {
                orderBy = delegationFormulaValue2._orderBy;
            }
            else
            {
                throw new InvalidOperationException($"args2 should always be of type {nameof(delegationFormulaValue)} : found {args[2]}");
            }

            FxGroupByNode groupBy = null;
            if (args[4] is GroupByObjectFormulaValue groupByObjectFormula)
            {
                groupBy = groupByObjectFormula.GroupBy;
            }
            else
            {
                throw new InvalidOperationException($"args4 should always be of type {nameof(GroupByObjectFormulaValue)} : found {args[4]}");
            }

            string distinctColumn = null;
            if (args[5] is StringValue sv)
            {
                distinctColumn = sv.Value;
            }
            else if (args[5] is not BlankValue)
            {
                throw new InvalidOperationException($"args5 should always be of type {nameof(StringValue)} : found {args[5]}");
            }

            ColumnMap columnMap = null;

            if (args.Length > 6)
            {
                columnMap = args[6] is RecordValue rv
                    ? new ColumnMap(rv, distinctColumn)
                    : throw new InvalidOperationException($"Expecting args6 to be a {nameof(RecordValue)} : found {args[6].GetType().Name}");
            }

#pragma warning disable CS0618 // Type or member is obsolete
            var delegationParameters = new DataverseDelegationParameters(((TableType)this.ReturnFormulaType).ToRecord())
            {
                FxFilter = filter,
                OrderBy = orderBy,
                Top = topCount,

                ColumnMap = columnMap,
                _partitionId = partitionId,
                Relation = relation,
                GroupBy = groupBy,
            };
#pragma warning restore CS0618 // Type or member is obsolete

            var rows = await _hooks.RetrieveMultipleAsync(services, table, delegationParameters, cancellationToken).ConfigureAwait(false);
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

        internal override bool IsUsingColumnMap(CallNode node, out ColumnMap columnMap)
        {
            if (node.Args.Count == 6 &&
                node.Args[4] is TextLiteralNode distinctNode &&
                node.Args[5] is RecordNode columnMapNode)
            {
                columnMap = new ColumnMap(columnMapNode, distinctNode);
                return true;
            }

            columnMap = null;
            return false;
        }
    }
}
