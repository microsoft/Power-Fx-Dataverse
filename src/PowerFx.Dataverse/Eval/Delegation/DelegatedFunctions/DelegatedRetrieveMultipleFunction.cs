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

        private const int TableArg = 0;
        private const int FilterArg = 1;
        private const int OrderbyArg = 2;
        private const int JoinArg = 3;
        private const int GroupByArg = 4;
        private const int CountArg = 5;
        private const int DistinctArg = 6;
        private const int ColumnRenameArg = 7;
        private const int ColumnRenameArg1 = ColumnRenameArg + 1;

        // args[0]: table
        // args[1]: filter
        // args[2]: orderby
        // args[3]: join
        // args[4]: groupby
        // args[5]: count
        // args[6]: distinct column
        // args[7]: columns with renames (in Record)
        protected override async Task<FormulaValue> ExecuteAsync(IServiceProvider services, FormulaValue[] args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (args[TableArg] is not IDelegatableTableValue table)
            {
                throw new InvalidOperationException($"args{TableArg} should always be of type {nameof(TableValue)} : found {args[TableArg]}");
            }

            int? topCount = null;
            FxFilterExpression filter;
            IList<OrderExpression> orderBy;
            ISet<LinkEntity> relation;
            string partitionId = null;

            if (args[CountArg] is NumberValue count)
            {
                topCount = (int)count.Value;
            }
            else if (args[CountArg] is BlankValue)
            {
                // If Count is Blank(), return empty table.
                var emptyList = new List<DValue<RecordValue>>();
                return new InMemoryTableValue(IRContext.NotInSource(this.ReturnFormulaType), emptyList);
            }
            else
            {
                throw new InvalidOperationException($"args{CountArg} should always be of type {nameof(NumberValue)} or {nameof(BlankValue)} : found {args[CountArg]}");
            }

            if (args[FilterArg] is DelegationFormulaValue delegationFormulaValue)
            {
                filter = delegationFormulaValue._filter;
                relation = delegationFormulaValue._relation;
                partitionId = delegationFormulaValue._partitionId;
            }
            else
            {
                throw new InvalidOperationException($"args{FilterArg} should always be of type {nameof(delegationFormulaValue)} : found {args[FilterArg]}");
            }

            if (args[OrderbyArg] is DelegationFormulaValue delegationFormulaValue2)
            {
                orderBy = delegationFormulaValue2._orderBy;
            }
            else
            {
                throw new InvalidOperationException($"args{OrderbyArg} should always be of type {nameof(delegationFormulaValue)} : found {args[OrderbyArg]}");
            }

            FxGroupByNode groupBy = null;
            if (args[GroupByArg] is GroupByObjectFormulaValue groupByObjectFormula)
            {
                groupBy = groupByObjectFormula.GroupBy;
            }
            else
            {
                throw new InvalidOperationException($"args{GroupByArg} should always be of type {nameof(GroupByObjectFormulaValue)} : found {args[GroupByArg]}");
            }

            string distinctColumn = null;
            if (args[DistinctArg] is StringValue sv)
            {
                distinctColumn = sv.Value;
            }
            else if (args[DistinctArg] is not BlankValue)
            {
                throw new InvalidOperationException($"args{DistinctArg} should always be of type {nameof(StringValue)} : found {args[DistinctArg]}");
            }

            FxJoinNode join = null;

            if (args[JoinArg] is JoinFormulaValue jv)
            {
                join = jv.JoinNode;
            }
            else
            {
                throw new InvalidOperationException($"args{JoinArg} should always be of type {nameof(JoinFormulaValue)} : found {args[JoinArg]}");
            }

            ColumnMap columnMap = null;

            if (args.Length > ColumnRenameArg)
            {
                columnMap = args[ColumnRenameArg] is RecordValue rv
                    ? new ColumnMap(rv, distinctColumn)
                    : throw new InvalidOperationException($"Expecting args5 to be a {nameof(RecordValue)} : found {args[ColumnRenameArg].GetType().Name}");
            }

#pragma warning disable CS0618 // Type or member is obsolete
            var delegationParameters = new DataverseDelegationParameters()
            {
                FxFilter = filter,
                OrderBy = orderBy,
                Top = topCount,
                Join = join,
                GroupBy = groupBy,
                ColumnMap = columnMap,
                _partitionId = partitionId,
                Relation = relation,
                ExpectedReturnType = (ReturnFormulaType as TableType).ToRecord()
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
            if (node.Args.Count == ColumnRenameArg1 &&
                node.Args[DistinctArg] is TextLiteralNode distinctNode &&
                node.Args[ColumnRenameArg] is RecordNode columnMapNode)
            {
                columnMap = new ColumnMap(columnMapNode, distinctNode);
                return true;
            }

            columnMap = null;
            return false;
        }
    }
}
