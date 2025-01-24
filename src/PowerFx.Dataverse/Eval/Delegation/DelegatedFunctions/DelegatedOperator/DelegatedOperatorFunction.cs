// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.Texl.Builtins;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
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
        private readonly FxConditionOperator _op;

        private readonly BinaryOpKind _binaryOpKind;

        public DelegatedOperatorFunction(DelegationHooks hooks, string name, BinaryOpKind binaryOpKind, FieldFunction parentOperation = FieldFunction.None)
          : base(hooks, name, FormulaType.Blank)
        {
            _binaryOpKind = binaryOpKind;

            if (DelegationIRVisitor.IsOpKindEqualityComparison(_binaryOpKind))
            {
                _op = FxConditionOperator.Equal;
            }
            else if (DelegationIRVisitor.IsOpKindInequalityComparison(_binaryOpKind))
            {
                _op = FxConditionOperator.NotEqual;
            }
            else if (DelegationIRVisitor.IsOpKindLessThanComparison(_binaryOpKind))
            {
                _op = FxConditionOperator.LessThan;
            }
            else if (DelegationIRVisitor.IsOpKindLessThanEqualComparison(_binaryOpKind))
            {
                _op = FxConditionOperator.LessEqual;
            }
            else if (DelegationIRVisitor.IsOpKindGreaterThanComparison(_binaryOpKind))
            {
                _op = FxConditionOperator.GreaterThan;
            }
            else if (DelegationIRVisitor.IsOpKindGreaterThanEqalComparison(_binaryOpKind))
            {
                _op = FxConditionOperator.GreaterEqual;
            }
            else if (_binaryOpKind == BinaryOpKind.InText)
            {
                // case insensitive
                _op = FxConditionOperator.Contains;
            }
            else if (_binaryOpKind == BinaryOpKind.Invalid)
            {
                if (parentOperation == FieldFunction.StartsWith)
                {
                    _op = FxConditionOperator.BeginsWith;
                }
                else if (parentOperation == FieldFunction.EndsWith)
                {
                    _op = FxConditionOperator.EndsWith;
                }
                else
                {
                    throw new NotSupportedException($"Unsupported operation {_op} :  {parentOperation}");
                }
            }
            else
            {
                throw new NotSupportedException($"Unsupported operation {_op}");
            }
        }

        private static async Task<FieldFunction> GetFieldFunctionAsync(RecordValue infoRecord, CancellationToken cancellationToken)
        {
            var tableFieldFunction = (TableValue)await infoRecord.GetFieldAsync(FieldInfoRecord.FieldFunctionColumnName, cancellationToken);

            FieldFunction fieldFunction = default;
            if (tableFieldFunction.Count() == 1)
            {
                var fieldFunctionDoubleValue = ((NumberValue)(await tableFieldFunction.Rows.First().Value.GetFieldAsync(FieldInfoRecord.SingleColumnTableColumnName, cancellationToken))).Value;

                fieldFunction = (FieldFunction)((int)fieldFunctionDoubleValue);
            }
            else if (tableFieldFunction.Count() > 1)
            {
                throw new InvalidOperationException("Multiple field functions are not supported");
            }
            else
            {
                fieldFunction = FieldFunction.None;
            }

            return fieldFunction;
        }

        protected override async Task<FormulaValue> ExecuteAsync(IServiceProvider services, FormulaValue[] args, CancellationToken cancellationToken)
        {
            // propagate args[0] if it's not a table (e.g. Blank/Error)
            if (args[0] is not TableValue table)
            {
                return args[0];
            }

            var arg1 = (RecordValue)args[1];
            var field = ((StringValue)await arg1.GetFieldAsync(FieldInfoRecord.FieldNameColumnName, cancellationToken)).Value;
            var tableFieldFunction = (TableValue)await ((RecordValue)args[1]).GetFieldAsync(FieldInfoRecord.FieldFunctionColumnName, cancellationToken);

            var fieldFunction = await GetFieldFunctionAsync((RecordValue)args[1], cancellationToken);

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

                var filter = GenerateFilterExpression(field, _op, dvValue, fieldFunction);
                filter.Conditions[0].TableName = relation.EntityAlias;

                result = new DelegationFormulaValue(filter, new HashSet<LinkEntity>(new LinkEntityComparer()) { relation }, null);
            }
            else
            {
                dvValue = _hooks.RetrieveAttribute(table, field, value);
                if (DelegationUtility.IsElasticTable(table.Type) && field == "partitionid" && _op == FxConditionOperator.Equal)
                {
                    result = new DelegationFormulaValue(filter: null, relation: null, partitionId: (string)dvValue, orderBy: null);
                }
                else
                {
                    var filter = GenerateFilterExpression(field, _op, dvValue, fieldFunction);
                    result = new DelegationFormulaValue(filter, relation: null, orderBy: null);
                }
            }

            return result;
        }

        internal static FxFilterExpression GenerateFilterExpression(string field, FxConditionOperator op, object dataverseValue, FieldFunction fieldFunction)
        {
            var filter = new FxFilterExpression();

            if (dataverseValue == null)
            {
                switch (op)
                {
                    case FxConditionOperator.Equal:
                        filter.AddCondition(field, FxConditionOperator.Null);
                        break;

                    case FxConditionOperator.NotEqual:
                        filter.AddCondition(field, FxConditionOperator.NotNull);
                        break;

                    default:
                        throw new InvalidOperationException($"Unsupported operator {op} for null value");
                }
            }
            else
            {
                filter.AddCondition(field, op, dataverseValue, fieldFunction);
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

        public override bool ComposeDependencyInfo(CallNode node, DependencyVisitor visitor, DependencyVisitor.DependencyContext context)
        {
            Contract.Assert(visitor is DependencyVisitorDataverse);

            var dvVisitor = (DependencyVisitorDataverse)visitor;

            string tableLogicalName = null;
            if (node.Args[0].IRContext.ResultType is AggregateType aggType)
            {
                tableLogicalName = aggType.TableSymbolName;
            }
            else
            {
                throw new InvalidOperationException($"{nameof(DelegatedOperatorFunction)} IR helper must have first argument an aggregate type");
            }

            // Relationship case
            if (node.Args.Count > 3)
            {
                if (node.Args[3] is CallNode maybeTableArg && maybeTableArg.Function is TableFunction tableArg && maybeTableArg.Args.Count == 1)
                {
                    var arg0 = (RecordNode)maybeTableArg.Args[0];
                    if (arg0.Fields.TryGetValue(FieldInfoRecord.SingleColumnTableColumnDName, out var valueNode) && valueNode is TextLiteralNode textNode)
                    {
                        var relationshipObj = DelegationUtility.DeserializeRelatioMetadata(textNode.LiteralValue);
                        visitor.AddFieldRead(tableLogicalName, relationshipObj.ReferencingFieldName);

                        var referencedEntityName = relationshipObj.ReferencedEntityName;

                        // referencedEntityName is null for non polymorphic relationships.
                        if (referencedEntityName == null)
                        {
                            if (dvVisitor.MetadataCache.TryGetXrmEntityMetadata(tableLogicalName, out var entityMetadata) &&
                                entityMetadata.TryGetManyToOneRelationship(relationshipObj.ReferencingFieldName, out var relation))
                            {
                                referencedEntityName = relation.ReferencedEntity;
                            }
                            else
                            {
                                throw new InvalidOperationException(referencedEntityName + " is null and can't be resolved");
                            }
                        }

                        // Arg1 is the field FieldInfoRecord being compared on the target entity.
                        var infoRecord = (RecordNode)node.Args[1];

                        var fieldName = ((TextLiteralNode)infoRecord.Fields.First(field => field.Key.Value == FieldInfoRecord.FieldNameColumnName).Value).LiteralValue;
                        visitor.AddFieldRead(referencedEntityName, fieldName);
                    }
                    else
                    {
                        throw new InvalidOperationException($"{nameof(DelegatedOperatorFunction)} IR helper must have fourth argument a table function with a single argument of type record with a field named Value");
                    }
                }
                else
                {
                    throw new InvalidOperationException($"{nameof(DelegatedOperatorFunction)} IR helper must have fourth argument a table function");
                }
            }
            else
            {
                if (node.Args[1] is RecordNode fieldInfoRecord
                    && fieldInfoRecord.Fields.TryGetValue(new Core.Utils.DName(FieldInfoRecord.FieldNameColumnName), out var maybeField)
                    && maybeField is TextLiteralNode field)
                {
                    string fieldName = field.LiteralValue;

                    if (dvVisitor.ColumnMap != null && dvVisitor.ColumnMap.TryGetColumnInfo(fieldName, out var fieldInfo))
                    {
                        fieldName = fieldInfo.RealColumnName;
                    }

                    visitor.AddFieldRead(tableLogicalName, fieldName);
                }
                else
                {
                    throw new InvalidOperationException($"{nameof(DelegatedOperatorFunction)} IR helper must have second argument a field Info record");
                }
            }

            base.ComposeDependencyInfo(node, dvVisitor, context);

            return true;
        }
    }
}
