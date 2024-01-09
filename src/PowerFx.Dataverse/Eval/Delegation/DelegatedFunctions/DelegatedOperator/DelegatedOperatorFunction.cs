using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json.Linq;
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

        protected override async Task<FormulaValue> ExecuteAsync(FormulaValue[] args, CancellationToken cancellationToken)
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
            if(args.Length > 3)
            {
                // If arguments have relation information, then we need to use that to generate the filter.
                links = ((TableValue)args[3]).Rows.Select(row => ((StringValue)row.Value.GetField("Value")).Value);
                relation = _hooks.RetreiveManyToOneRelation(table, links);
                dvValue = _hooks.RetrieveRelationAttribute(table, relation, field, value);
                var filter = GenerateFilterExpression(field, dvValue);
                filter.Conditions[0].EntityName = relation.LinkToEntityName;

                result = new DelegationFormulaValue(filter, new HashSet<LinkEntity>(new LinkEntityComparer()) { relation });
            }
            else
            {
                dvValue = _hooks.RetrieveAttribute(table, field, value);
                var filter = GenerateFilterExpression(field, dvValue);
                result = new DelegationFormulaValue(filter, relation: null);
            }

            return result;
        }

        private FilterExpression GenerateFilterExpression(string field, object dataverseValue)
        {
            var filter = new FilterExpression();

            if (dataverseValue == null)
            {
                switch (_op)
                {
                    case ConditionOperator.Equal:
                        filter.AddCondition(field, ConditionOperator.Null);
                        break;
                    case ConditionOperator.NotEqual:
                        filter.AddCondition(field, ConditionOperator.NotNull);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported operator {_op} for null value");
                }
            }
            else
            {
                filter.AddCondition(field, _op, dataverseValue);
            }

            return filter;
        }

        private FormulaValue MaybeReplaceBlank(FormulaValue formulaValue)
        {
            if (formulaValue is not BlankValue)
            {
                return formulaValue;
            }

            switch (_binaryOpKind)
            {
                // Equality and Non-Equality returns Blank itself.
                case BinaryOpKind.EqNumbers:
                case BinaryOpKind.EqDecimals:
                case BinaryOpKind.EqBoolean:
                case BinaryOpKind.EqText:
                case BinaryOpKind.EqTime:
                case BinaryOpKind.EqDateTime:
                case BinaryOpKind.EqGuid:
                case BinaryOpKind.EqCurrency:
                case BinaryOpKind.NeqNumbers:
                case BinaryOpKind.NeqBoolean:
                case BinaryOpKind.NeqText:
                case BinaryOpKind.NeqDate:
                case BinaryOpKind.NeqTime:
                case BinaryOpKind.NeqDateTime:
                case BinaryOpKind.NeqGuid:
                case BinaryOpKind.NeqDecimals:
                case BinaryOpKind.NeqCurrency:
                    return formulaValue;

                // Other Operations returns Default value.

                case BinaryOpKind.LtNumbers:
                case BinaryOpKind.LeqNumbers:
                case BinaryOpKind.GtNumbers:
                case BinaryOpKind.GeqNumbers:
                    return FormulaValue.New(0.0);

                case BinaryOpKind.LtDecimals:
                case BinaryOpKind.LeqDecimals:
                case BinaryOpKind.GtDecimals:
                case BinaryOpKind.GeqDecimals:
                    return FormulaValue.New(0m);

                case BinaryOpKind.LtDateTime:
                case BinaryOpKind.LeqDateTime:
                case BinaryOpKind.GtDateTime:
                case BinaryOpKind.GeqDateTime:
                    return FormulaValue.New(DelegationEngineExtensions._epoch);

                case BinaryOpKind.LtDate:
                case BinaryOpKind.LeqDate:
                case BinaryOpKind.GtDate:
                case BinaryOpKind.GeqDate:
                    return FormulaValue.NewDateOnly(DelegationEngineExtensions._epoch);

                case BinaryOpKind.LtTime:
                case BinaryOpKind.LeqTime:
                case BinaryOpKind.GtTime:
                case BinaryOpKind.GeqTime:
                    return FormulaValue.New(TimeSpan.Zero);

                default:
                    throw new NotSupportedException($"Unsupported operation {_op}");
            }
        }
    }
}
