using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Span = Microsoft.PowerFx.Syntax.Span;

namespace Microsoft.PowerFx.Dataverse
{
    public static class DelegationEngineExtensions
    {
        // Only Dataverse Eval should use this.  
        // Nested class to decrease visibility. 
        public class DelegationHooks
        {
            // Return Blank if not found. 
            public virtual async Task<DValue<RecordValue>> RetrieveAsync(TableValue table, Guid id, CancellationToken cancel)
            {
                throw new NotImplementedException();
            }

            public virtual async Task<IEnumerable<DValue<RecordValue>>> RetrieveMultipleAsync(TableValue table, FilterExpression filter, int? count, CancellationToken cancel)
            {
                throw new NotImplementedException();
            }

            // Are symbols from this table delegable?
            public virtual bool IsDelegableSymbolTable(ReadOnlySymbolTable symTable)
            {
                return false;
            }

            // Generate a lookup call for: Lookup(Table, Id=Guid)  
            internal CallNode MakeRetrieveCall(DelegationIRVisitor.RetVal query, IntermediateNode argGuid)
            {
                var func = new DelegateLookupFunction(this, query._tableType);

                var node = new CallNode(IRContext.NotInSource(query._tableType), func, query._sourceTableIRNode, argGuid);
                return node;
            }

            // Generate a top call for: FirstN(Table, count) => __top(Table, count)
            internal CallNode MakeTopCall(DelegationIRVisitor.RetVal query, IntermediateNode argCount)
            {
                var func = new DelegatedFirstNFunction(this, query._tableType);
                var node = new CallNode(IRContext.NotInSource(query._tableType), func, query._sourceTableIRNode, argCount);
                return node;
            }

            internal CallNode MakeQueryExecutorCall(DelegationIRVisitor.RetVal query, IntermediateNode filterValue)
            {
                var func = new DelegatedQueryFunction(this, query._tableType);
                var node = new CallNode(IRContext.NotInSource(query._tableType), func, query._sourceTableIRNode, filterValue);
                return node;
            }

            internal CallNode MakeCallNode(DelegateFunction func, FormulaType tableType, string fieldName, IntermediateNode value)
            {
                var field = new TextLiteralNode(IRContext.NotInSource(FormulaType.String), fieldName);
                var node = new CallNode(IRContext.NotInSource(tableType), func, field, value);
                return node;
            }

            internal CallNode MakeEqCall(FormulaType tableType, string fieldName, IntermediateNode value)
            {
                var func = new DelegatedEq(this, (TableType)tableType);
                var node = MakeCallNode(func, tableType, fieldName, value);
                return node;
            }

            internal CallNode MakeGtCall(FormulaType tableType, string fieldName, IntermediateNode value)
            {
                var func = new DelegatedGt(this, (TableType)tableType);
                var node = MakeCallNode(func, tableType, fieldName, value);
                return node;
            }

            internal CallNode MakeGeqCall(FormulaType tableType, string fieldName, IntermediateNode value)
            {
                var func = new DelegatedGeq(this, (TableType)tableType);
                var node = MakeCallNode(func, tableType, fieldName, value);
                return node;
            }

            internal CallNode MakeLtCall(FormulaType tableType, string fieldName, IntermediateNode value)
            {
                var func = new DelegatedLt(this, (TableType)tableType);
                var node = MakeCallNode(func, tableType, fieldName, value);
                return node;
            }

            internal CallNode MakeLeqCall(FormulaType tableType, string fieldName, IntermediateNode value)
            {
                var func = new DelegatedLeq(this, (TableType)tableType);
                var node = MakeCallNode(func, tableType, fieldName, value);
                return node;
            }

            internal CallNode MakeAndCall(FormulaType tableType, IList<IntermediateNode> args)
            {
                var func = new DelegatedAnd(this, (TableType)tableType);
                var node = new CallNode(IRContext.NotInSource(tableType), func, args);
                return node;
            }

            internal CallNode MakeOrCall(FormulaType tableType, IList<IntermediateNode> args)
            {
                var func = new DelegatedOr(this, (TableType)tableType);
                var node = new CallNode(IRContext.NotInSource(tableType), func, args);
                return node;
            }
        }

        private class DelegationIRTransform : IRTransform
        {
            private readonly DelegationEngineExtensions.DelegationHooks _hooks;

            public DelegationIRTransform(DelegationEngineExtensions.DelegationHooks hooks)
                : base("DelegationIRTransform")
            {
                _hooks = hooks; 
            }

            public override IntermediateNode Transform(IntermediateNode node, ICollection<ExpressionError> errors)
            {
                return node.Accept(
                    new DelegationIRVisitor(_hooks, errors),
                    new DelegationIRVisitor.Context())._node;
            }
        }

        // Called by extensions in Dataverse.Eval, which will pass in retrieveSingle.
        public static void EnableDelegationCore(this Engine engine, DelegationEngineExtensions.DelegationHooks hooks)
        {
            IRTransform t = new DelegationIRTransform(hooks);

            engine.IRTransformList.Add(t);
        }
    }
}