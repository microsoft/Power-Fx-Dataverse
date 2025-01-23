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
    internal class DelegatedRetrieveCountFunction : DelegateFunction
    {
        public DelegatedRetrieveCountFunction(DelegationHooks hooks)
          : base(hooks, "__retrieveCount", FormulaType.Number)
        {
        }

        private const int TableArg = 0;
        private const int FilterArg = 1;
        private const int OrderbyArg = 2;
        private const int JoinArg = 3;
        private const int GroupByArg = 4;
        private const int CountArg = 5;
        private const int ColumnMapArg = 6;

        // args[0]: table
        // args[1]: filter
        // args[2]: orderby
        // args[3]: join
        // args[4]: groupby
        // args[5]: count
        // args[6]: columns with possible renames 

        protected override async Task<FormulaValue> ExecuteAsync(IServiceProvider services, FormulaValue[] args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (args[TableArg] is not IDelegatableTableValue table)
            {
                throw new InvalidOperationException($"args{TableArg} should always be of type {nameof(IDelegatableTableValue)} : found {args[TableArg]}");
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

            FxJoinNode join = null;
            if (args[JoinArg] is JoinFormulaValue jv)
            {
                join = jv.JoinNode;
            }
            else
            {
                throw new InvalidOperationException($"args{JoinArg} should always be of type {nameof(JoinFormulaValue)} : found {args[JoinArg]}");
            }

            FxColumnMap columnMap = null;
            if (args[ColumnMapArg] is ColumnMapFormulaValue columnMapFormulaValue)
            {
                columnMap = columnMapFormulaValue.ColumnMap;
            }
            else
            {
                throw new InvalidOperationException($"args{ColumnMapArg} should always be of type {nameof(ColumnMapFormulaValue)} : found {args[ColumnMapArg]}");
            }

#pragma warning disable CS0618 // Type or member is obsolete
            var delegationParameters = new DataverseDelegationParameters(FormulaType.Number) 
            {
                FxFilter = filter,
                OrderBy = orderBy,
                Top = topCount,
                Join = join,
                GroupBy = groupBy,
                ColumnMap = columnMap,
                _partitionId = partitionId,
                Relation = relation
            };
#pragma warning restore CS0618 // Type or member is obsolete

            var rowCount = await _hooks.RetrieveCount(services, table, delegationParameters, cancellationToken);

            if (rowCount < 0)
            {
                var expressionError = new ExpressionError()
                {
                    Message = "Datasource could not count the rows.",
                    Kind = ErrorKind.Custom,
                    Severity = ErrorSeverity.Severe,
                };

                return FormulaValue.NewError(expressionError);
            }

            var countFV = FormulaValue.New(rowCount);
            return countFV;
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

        internal override bool IsUsingColumnMap(CallNode node, out FxColumnMap columnMap)
        {
            if (node.Args[ColumnMapArg] is ResolvedObjectNode columnMapIR)
            {
                columnMap = ((ColumnMapFormulaValue)columnMapIR.Value).ColumnMap;

                if (columnMap != null)
                {
                    return true;
                }
            }

            columnMap = null;
            return false;
        }

        internal override bool IsUsingJoinNode(CallNode node, out FxJoinNode joinNode)
        {
            if (node.Args[JoinArg] is ResolvedObjectNode ron &&
                ron.Value is JoinFormulaValue jfv)
            {
                joinNode = jfv.JoinNode;
                return joinNode != null;
            }

            joinNode = null;
            return false;
        }
    }
}
