using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Dataverse.Eval.Core;
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

        // arg0: table
        // arg1: filter
        // arg2: orderby
        protected override async Task<FormulaValue> ExecuteAsync(IServiceProvider services, FormulaValue[] args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (args[0] is not IDelegatableTableValue table)
            {
                throw new InvalidOperationException($"args0 should always be of type {nameof(TableValue)} : found {args[0]}");
            }

            FilterExpression filter;
            IList<OrderExpression> orderBy;
            ISet<LinkEntity> relation;
            string partitionId;

            if (args[1] is DelegationFormulaValue DelegationFormulaValue)
            {
                filter = DelegationFormulaValue._filter;
                relation = DelegationFormulaValue._relation;
                partitionId = DelegationFormulaValue._partitionId;
            }
            else
            {
                throw new InvalidOperationException($"Input arg1 should always be of type {nameof(DelegationFormulaValue)}"); ;
            }

            if (args[2] is DelegationFormulaValue DelegationFormulaValue2)
            {
                orderBy = DelegationFormulaValue2._orderBy;
            }
            else
            {
                throw new InvalidOperationException($"Input arg2 should always be of type {nameof(DelegationFormulaValue)}"); ;
            }

            string distinctColumn = null;
            if (args[3] is StringValue sv)
            {
                distinctColumn = sv.Value;
            }
            else if (args[3] is not BlankValue)
            {
                throw new InvalidOperationException($"args3 should always be of type {nameof(StringValue)} : found {args[3]}");
            }

            ColumnMap columnMap = null;

            if (args.Length > 4)
            {
                columnMap = args[4] is RecordValue rv
                    ? new ColumnMap(rv)
                    : throw new InvalidOperationException($"Expecting args4 to be a {nameof(RecordValue)} : found {args[4].GetType().Name}");
            }

#pragma warning disable CS0618 // Type or member is obsolete
            var delegationParameters = new DataverseDelegationParameters
            {
                Filter = filter,
                OrderBy = orderBy,
                Top = 1,

                _columnMap = columnMap,
                _distinctColumn = distinctColumn,
                _partitionId = partitionId,
                _relation = relation,
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
    }
}
