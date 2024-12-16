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

        private const int TableArg = 0;
        private const int FilterArg = 1;
        private const int OrderbyArg = 2;
        private const int JoinArg = 3;
        private const int DistinctArg = 4;
        private const int ColumnRenameArg = 5;
        private const int ColumnRenameArg1 = 6;

        // args[0]: table
        // args[1]: filter
        // args[2]: orderby
        // args[3]: join
        // args[4]: distinct column
        // args[5]: columns with renames (in Record)
        protected override async Task<FormulaValue> ExecuteAsync(IServiceProvider services, FormulaValue[] args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (args[TableArg] is not IDelegatableTableValue table)
            {
                throw new InvalidOperationException($"args{TableArg} should always be of type {nameof(TableValue)} : found {args[TableArg]}");
            }

            FxFilterExpression filter;
            IList<OrderExpression> orderBy;
            ISet<LinkEntity> relation;
            string partitionId;

            if (args[FilterArg] is DelegationFormulaValue delegationFormulaValue)
            {
                filter = delegationFormulaValue._filter;
                relation = delegationFormulaValue._relation;
                partitionId = delegationFormulaValue._partitionId;
            }
            else
            {
                throw new InvalidOperationException($"Input arg{FilterArg} should always be of type {nameof(delegationFormulaValue)}");
            }

            if (args[OrderbyArg] is DelegationFormulaValue delegationFormulaValue2)
            {
                orderBy = delegationFormulaValue2._orderBy;
            }
            else
            {
                throw new InvalidOperationException($"Input arg{OrderbyArg} should always be of type {nameof(delegationFormulaValue)}");
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
                    : throw new InvalidOperationException($"Expecting args{ColumnRenameArg} to be a {nameof(RecordValue)} : found {args[ColumnRenameArg].GetType().Name}");
            }

#pragma warning disable CS0618 // Type or member is obsolete
            var delegationParameters = new DataverseDelegationParameters
            {
                FxFilter = filter,
                OrderBy = orderBy,
                Top = 1,
                Join = join,
                ColumnMap = columnMap,
                _partitionId = partitionId,
                Relation = relation,
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
            if (node.Args.Count == ColumnRenameArg1 &&
                node.Args[DistinctArg] is Core.IR.Nodes.TextLiteralNode distinctNode &&
                node.Args[ColumnRenameArg] is Core.IR.Nodes.RecordNode columnMapNode)
            {
                columnMap = new ColumnMap(columnMapNode, distinctNode);
                return true;
            }

            columnMap = null;
            return false;
        }
    }
}
