// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;

namespace Microsoft.PowerFx.Dataverse
{
    // Rewrite the tree to inject delegation.
    // If we encounter a dataverse table (something that should be delegated) during the walk, we either:
    // - successfully delegate, which means rewriting to a call an efficient DelegatedFunction,
    // - leave IR unchanged (don't delegate), but issue a warning.
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        // Return Value passed through at each phase of the walk.
        public class RetVal
        {
            // supports $select with column renames and distinct
            public readonly ColumnMap ColumnMap;

            public readonly DelegationHooks Hooks;

            // Original IR node for non-delegating path.
            // This should always be set. Even if we are attempting to delegate, we may need to use this if we can't support the delegation.
            public readonly IntermediateNode OriginalNode;

            // IR node that will resolve to the TableValue at runtime.
            // From here, we can downcast at get the services. Ideally would be either Scope node or ResolvedObjectNode
            public readonly DelegableIntermediateNode _sourceTableIRNode;

            // Table type  and original metadata for table that we're delegating to.
            public readonly TableType TableType;

            public readonly IDelegationMetadata DelegationMetadata;

            private readonly IntermediateNode _filter;

            private readonly IntermediateNode _orderBy;

            private readonly IntermediateNode _topCount;

            private readonly NumberLiteralNode _maxRows;

            internal NumberLiteralNode MaxRows => _maxRows;

            // Null if not dataverse
            private readonly EntityMetadata _metadata;

            /// <summary>
            /// Will be null for non-dataverse tables.
            /// </summary>
            public EntityMetadata Metadata => _metadata ?? throw new ArgumentNullException(nameof(Metadata));

            public bool IsDataverseDelegation => _metadata != null;

            private FxGroupByNode _groupByNode;

            internal bool HasGroupByNode => _groupByNode != null;

            internal IntermediateNode GroupByNode => GenerateGroupByIR(_groupByNode, TableType);

            public RetVal(DelegationHooks hooks, IntermediateNode originalNode, IntermediateNode sourceTableIRNode, TableType tableType, IntermediateNode filter, IntermediateNode orderBy, IntermediateNode count, int maxRows, ColumnMap columnMap, FxGroupByNode groupByNode = null)
            {
                this._maxRows = new NumberLiteralNode(IRContext.NotInSource(FormulaType.Number), maxRows);
                this._sourceTableIRNode = new DelegableIntermediateNode(sourceTableIRNode ?? throw new ArgumentNullException(nameof(sourceTableIRNode)));
                this.TableType = tableType ?? throw new ArgumentNullException(nameof(tableType));
                this.OriginalNode = originalNode ?? throw new ArgumentNullException(nameof(originalNode));
                this.Hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
                this.DelegationMetadata = tableType._type.AssociatedDataSources.FirstOrDefault().DelegationMetadata;

                // topCount and filter are optional.
                this._topCount = count;
                this._filter = filter;
                this._orderBy = orderBy;
                this._groupByNode = groupByNode;
                this.ColumnMap = columnMap;
                this.IsDelegating = true;

                _ = DelegationUtility.TryGetEntityMetadata(tableType, out this._metadata);                
            }

            // Non-delegating path
            public RetVal(IntermediateNode node, bool isDelegating = false)
            {
                OriginalNode = node;
                IsDelegating = isDelegating;
            }

            public bool HasFilter => _filter != null;

            public bool HasOrderBy => _orderBy != null;

            public bool HasTopCount => _topCount != null;

            public bool HasColumnMap => ColumnMap != null;

            public IntermediateNode Filter => _filter ?? MakeBlankCall(Hooks);

            public IntermediateNode OrderBy => _orderBy ?? MakeBlankCall(Hooks);

            public IntermediateNode TopCountOrDefault => _topCount ?? _maxRows;

            // If set, we're attempting to delegate the current expression specifeid by _node.
            public bool IsDelegating { get; init; }

            public bool IsElasticTable => _metadata?.IsElasticTable() ?? false;

            public bool TryGetPrimaryIdFieldName(out string primaryId)
            {
                if (_metadata == null)
                {
                    primaryId = null;
                    return false;
                }

                primaryId = _metadata.PrimaryIdAttribute;
                return true;
            }

            public bool TryGetLogicalName(out string logicalName)
            {
                if (_metadata != null)
                {
                    logicalName = _metadata.LogicalName;
                    return true;
                }

                logicalName = null;
                return false;
            }

            internal IntermediateNode AddFilter(IntermediateNode newFilter, ScopeSymbol scope)
            {
                if (_filter != null)
                {
                    var combinedFilter = new List<IntermediateNode> { _filter, newFilter };
                    var result = Hooks.MakeAndCall(TableType, combinedFilter, scope);
                    return result;
                }

                return newFilter;
            }

            internal CallNode MakeBlankCall(DelegationHooks hooks)
            {
                var func = new DelegatedBlank(hooks);
                var node = new CallNode(IRContext.NotInSource(FormulaType.Blank), func);
                return node;
            }

            private static IntermediateNode GenerateGroupByIR(FxGroupByNode groupByNode, TableType tableType)
            {
                // convert _groupByNode to IR
                var groupByFormulaValue = new GroupByObjectFormulaValue(groupByNode, tableType);
                var groupByIRNode = new ResolvedObjectNode(IRContext.NotInSource(groupByFormulaValue.Type), groupByFormulaValue);
                return groupByIRNode;
            }
        }
    }
}
