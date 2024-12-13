// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Executes a query against a table and returns a record.
    /// First Arg is the table to query, Second Arg is the filter to apply.
    /// </summary>
    internal class DelegatedRetrieveSingleFunction : DelegateFunction
    {
        public DelegatedRetrieveSingleFunction(DelegationHooks hooks, RecordType returnType)
          : base(hooks, "__retrieveSingle", returnType)
        {
        }

        // args[0]: table
        // args[1]: filter
        // args[2]: orderby
        // args[3]: distinct column
        // args[4]: columns with renames (in Record)
        protected override async Task<FormulaValue> ExecuteAsync(IServiceProvider services, FormulaValue[] args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (args[0] is not IDelegatableTableValue table)
            {
                throw new InvalidOperationException($"args0 should always be of type {nameof(TableValue)} : found {args[0]}");
            }

            FxFilterExpression filter;
            IList<OrderExpression> orderBy;
            ISet<LinkEntity> relation;
            string partitionId;

            if (args[1] is DelegationFormulaValue delegationFormulaValue)
            {
                filter = delegationFormulaValue._filter;
                relation = delegationFormulaValue._relation;
                partitionId = delegationFormulaValue._partitionId;
            }
            else
            {
                throw new InvalidOperationException($"Input arg1 should always be of type {nameof(delegationFormulaValue)}");
            }

            if (args[2] is DelegationFormulaValue delegationFormulaValue2)
            {
                orderBy = delegationFormulaValue2._orderBy;
            }
            else
            {
                throw new InvalidOperationException($"Input arg2 should always be of type {nameof(delegationFormulaValue)}");
            }

            FxGroupByNode groupBy = null;
            if (args[3] is GroupByObjectFormulaValue groupByObject)
            {
                groupBy = groupByObject.GroupBy;
            }
            else
            {
                throw new InvalidOperationException($"Input arg3 should always be of type {nameof(GroupByObjectFormulaValue)}");
            }

            string distinctColumn = null;
            if (args[4] is StringValue sv)
            {
                distinctColumn = sv.Value;
            }
            else if (args[4] is not BlankValue)
            {
                throw new InvalidOperationException($"args4 should always be of type {nameof(StringValue)} : found {args[4]}");
            }

            ColumnMap columnMap = null;

            if (args.Length > 5)
            {
                columnMap = args[5] is RecordValue rv
                    ? new ColumnMap(rv, distinctColumn)
                    : throw new InvalidOperationException($"Expecting args5 to be a {nameof(RecordValue)} : found {args[5].GetType().Name}");
            }

#pragma warning disable CS0618 // Type or member is obsolete
            var delegationParameters = new DataverseDelegationParameters
            {
                FxFilter = filter,
                OrderBy = orderBy,
                Top = 1,

                ColumnMap = columnMap,
                _partitionId = partitionId,
                Relation = relation,
                GroupBy = groupBy
            };
#pragma warning restore CS0618 // Type or member is obsolete

            var row = await _hooks.RetrieveMultipleAsync(services, table, delegationParameters, cancellationToken).ConfigureAwait(false);
            var result = row.FirstOrDefault();

            if (result == null || result.IsBlank)
            {
                return FormulaValue.NewBlank(this.ReturnFormulaType);
            }
            else if (result.IsError)
            {
                return result.Error;
            }
            else
            {
                // Adjust type, as function like ShowColumn() can manipulate it.
                RecordValue resultRecord;
                resultRecord = CompileTimeTypeWrapperRecordValue.AdjustType((RecordType)ReturnFormulaType, result.Value);

                return resultRecord;
            }
        }

        internal override bool IsUsingColumnMap(Core.IR.Nodes.CallNode node, out ColumnMap columnMap)
        {
            if (node.Args.Count == 5 &&
                node.Args[3] is Core.IR.Nodes.TextLiteralNode distinctNode &&
                node.Args[4] is Core.IR.Nodes.RecordNode columnMapNode)
            {
                columnMap = new ColumnMap(columnMapNode, distinctNode);
                return true;
            }

            columnMap = null;
            return false;
        }
    }
}
