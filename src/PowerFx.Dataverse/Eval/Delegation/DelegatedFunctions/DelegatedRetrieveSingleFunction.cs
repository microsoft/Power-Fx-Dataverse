using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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

        protected override async Task<FormulaValue> ExecuteAsync(IServiceProvider services, FormulaValue[] args, CancellationToken cancellationToken)
        {
            if (args[0] is not IDelegatableTableValue table)
            {
                throw new InvalidOperationException($"args0 should alway be of type {nameof(TableValue)} : found {args[0]}");
            }

            FilterExpression filter;
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
                throw new InvalidOperationException($"Input arg should alway be of type {nameof(DelegationFormulaValue)}"); ;
            }

            bool isDistinct = false;
            if (args[2] is BooleanValue bv)
            {
                isDistinct = bv.Value;
            }
            else
            {
                throw new InvalidOperationException($"args3 should alway be of type {nameof(BooleanValue)} : found {args[3]}");
            }

            // column names to fetch.
            IEnumerable<string> columns = null;
            if (args.Length > 3)
            {
                columns = args.Skip(3).Select(x => {
                    if (x is StringValue stringValue)
                    {
                        return stringValue.Value;
                    }
                    else
                    {
                        throw new InvalidOperationException($"From Args3 onwards, all args should have been String Value");
                    }
                });
            }

#pragma warning disable CS0618 // Type or member is obsolete
            var delegationParameters = new DataverseDelegationParameters
            {
                _relation = relation,
                Filter = filter,
                _partitionId = partitionId,
                Top = 1,
                _columnSet = columns,
                _isDistinct = isDistinct
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
