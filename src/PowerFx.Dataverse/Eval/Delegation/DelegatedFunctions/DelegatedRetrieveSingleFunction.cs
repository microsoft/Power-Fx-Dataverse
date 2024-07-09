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

            var isDistinct = args[3] is BooleanValue bv
                ? bv.Value
                : throw new InvalidOperationException($"args3 should always be of type {nameof(BooleanValue)} : found {args[3]}");

            // column names to fetch.
            IEnumerable<string> columns = null;
            if (args.Length > 4)
            {
                columns = args.Skip(4).Select(x => x is StringValue stringValue
                                                        ? stringValue.Value
                                                        : throw new InvalidOperationException($"From Args4 onwards, all args should have been String Value"));                
            }

#pragma warning disable CS0618 // Type or member is obsolete
            var delegationParameters = new DataverseDelegationParameters
            {
                Filter = filter,
                OrderBy = orderBy,
                Top = 1,

                _columnSet = columns,
                _isDistinct = isDistinct,
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
                if (isDistinct)
                {
                    if (columns == null || !columns.Any() || columns.Count() > 1)
                    {
                        throw new InvalidOperationException("Distinct requires single column to be specified");
                    }

                    resultRecord = DelegatedRetrieveMultipleFunction.ToValueColumn(result, columns.First()).Value;
                }
                else
                {
                    resultRecord = CompileTimeTypeWrapperRecordValue.AdjustType((RecordType)ReturnFormulaType, result.Value);
                }

                return resultRecord;
            }
        }
    }
}
