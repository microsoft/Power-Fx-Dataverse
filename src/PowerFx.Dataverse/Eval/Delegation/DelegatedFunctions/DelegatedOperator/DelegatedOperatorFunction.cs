// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    // Delegation means rewriting a client-side functions into functions that make efficient server calls.
    // This means injecting new runtime helper functions into the IR.
    // As runtime helpers, they can't be referenced by binder and don't show in intellisense or source.
    // As such, the actual function name doesn't matter and is just used for diagnostics.
    internal abstract class DelegatedOperatorFunction : DelegateFunction
    {
        private readonly ConditionOperator _op;

        private readonly BinaryOpKind _binaryOpKind;

        public DelegatedOperatorFunction(DelegationHooks hooks, string name, BinaryOpKind binaryOpKind)
          : base(hooks, name, FormulaType.Blank)
        {
            _binaryOpKind = binaryOpKind;

            if (DelegationIRVisitor.IsOpKindEqualityComparison(_binaryOpKind))
            {
                _op = ConditionOperator.Equal;
            }
            else if (DelegationIRVisitor.IsOpKindInequalityComparison(_binaryOpKind))
            {
                _op = ConditionOperator.NotEqual;
            }
            else if (DelegationIRVisitor.IsOpKindLessThanComparison(_binaryOpKind))
            {
                _op = ConditionOperator.LessThan;
            }
            else if (DelegationIRVisitor.IsOpKindLessThanEqualComparison(_binaryOpKind))
            {
                _op = ConditionOperator.LessEqual;
            }
            else if (DelegationIRVisitor.IsOpKindGreaterThanComparison(_binaryOpKind))
            {
                _op = ConditionOperator.GreaterThan;
            }
            else if (DelegationIRVisitor.IsOpKindGreaterThanEqalComparison(_binaryOpKind))
            {
                _op = ConditionOperator.GreaterEqual;
            }
            else
            {
                throw new NotSupportedException($"Unsupported operation {_op}");
            }
        }

        protected override async Task<FormulaValue> ExecuteAsync(IServiceProvider services, FormulaValue[] args, CancellationToken cancellationToken)
        {
            // propagate args[0] if it's not a table (e.g. Blank/Error)
            if (args[0] is not TableValue table)
            {
                return args[0];
            }

            var field = ((StringValue)args[1]).Value;
            var value = MaybeReplaceBlank(args[2]);

            if (!value.Type._type.IsPrimitive && !(value.Type._type.IsRecord && AttributeUtility.TryGetLogicalNameFromOdataName(field, out field)))
            {
                throw new InvalidOperationException("Unsupported type : expected Primitive");
            }

            IEnumerable<string> links = null;
            LinkEntity relation = null;
            object dvValue = null;
            DelegationFormulaValue result = null;

            if (args.Length > 3)
            {
                // If arguments have relation information, then we need to use that to generate the filter.
                links = ((TableValue)args[3]).Rows.Select(row => ((StringValue)row.Value.GetField("Value")).Value);
                relation = _hooks.RetrieveManyToOneRelation(table, links);
                dvValue = _hooks.RetrieveRelationAttribute(table, relation, field, value);

                var filter = GenerateFilterExpression(field, _op, dvValue);
                filter.Conditions[0].EntityName = relation.EntityAlias;

                result = new DelegationFormulaValue(filter, new HashSet<LinkEntity>(new LinkEntityComparer()) { relation }, null);
            }
            else
            {
                dvValue = _hooks.RetrieveAttribute(table, field, value);
                if (DelegationUtility.IsElasticTable(table.Type) && field == "partitionid" && _op == ConditionOperator.Equal)
                {
                    result = new DelegationFormulaValue(filter: null, relation: null, partitionId: (string)dvValue, orderBy: null);
                }
                else
                {
                    var filter = GenerateFilterExpression(field, _op, dvValue);
                    result = new DelegationFormulaValue(filter, relation: null, orderBy: null);
                }
            }

            return result;
        }

        internal static FilterExpression GenerateFilterExpression(string field, ConditionOperator op, object dataverseValue)
        {
            var filter = new FilterExpression();

            if (dataverseValue == null)
            {
                switch (op)
                {
                    case ConditionOperator.Equal:
                        filter.AddCondition(field, ConditionOperator.Null);
                        break;

                    case ConditionOperator.NotEqual:
                        filter.AddCondition(field, ConditionOperator.NotNull);
                        break;

                    default:
                        throw new InvalidOperationException($"Unsupported operator {op} for null value");
                }
            }
            else
            {
                filter.AddCondition(field, op, dataverseValue);
            }

            return filter;
        }

        private FormulaValue MaybeReplaceBlank(FormulaValue formulaValue)
        {
            if (formulaValue is not BlankValue)
            {
                return formulaValue;
            }

            return _binaryOpKind switch
            {
                // Equality and Non-Equality returns Blank itself.
                BinaryOpKind.EqBoolean or
                BinaryOpKind.EqCurrency or
                BinaryOpKind.EqDateTime or
                BinaryOpKind.EqDecimals or
                BinaryOpKind.EqGuid or
                BinaryOpKind.EqNumbers or
                BinaryOpKind.EqOptionSetValue or
                BinaryOpKind.EqText or
                BinaryOpKind.EqTime or
                BinaryOpKind.NeqBoolean or
                BinaryOpKind.NeqCurrency or
                BinaryOpKind.NeqDate or
                BinaryOpKind.NeqDateTime or
                BinaryOpKind.NeqDecimals or
                BinaryOpKind.NeqGuid or
                BinaryOpKind.NeqNumbers or
                BinaryOpKind.NeqOptionSetValue or
                BinaryOpKind.NeqText or
                BinaryOpKind.NeqTime
                    => formulaValue,

                // Other Operations returns Default value.
                BinaryOpKind.GeqNumbers or
                BinaryOpKind.GtNumbers or
                BinaryOpKind.LeqNumbers or
                BinaryOpKind.LtNumbers
                    => FormulaValue.New(0.0),

                BinaryOpKind.GeqDecimals or
                BinaryOpKind.GtDecimals or
                BinaryOpKind.LeqDecimals or
                BinaryOpKind.LtDecimals
                    => FormulaValue.New(0m),

                BinaryOpKind.GeqDateTime or
                BinaryOpKind.GtDateTime or
                BinaryOpKind.LeqDateTime or
                BinaryOpKind.LtDateTime
                    => FormulaValue.New(DelegationEngineExtensions._epoch),

                BinaryOpKind.GeqDate or
                BinaryOpKind.GtDate or
                BinaryOpKind.LeqDate or
                BinaryOpKind.LtDate
                    => FormulaValue.NewDateOnly(DelegationEngineExtensions._epoch),

                BinaryOpKind.GeqTime or
                BinaryOpKind.GtTime or
                BinaryOpKind.LeqTime or
                BinaryOpKind.LtTime
                    => FormulaValue.New(TimeSpan.Zero),

                _ => throw new NotSupportedException($"Unsupported operation {_op}"),
            };
        }
    }
}
