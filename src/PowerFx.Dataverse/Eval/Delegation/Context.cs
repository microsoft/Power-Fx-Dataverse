// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;

namespace Microsoft.PowerFx.Dataverse
{
    // Rewrite the tree to inject delegation.
    // If we encounter a dataverse table (something that should be delegated) during the walk, we either:
    // - successfully delegate, which means rewriting to a call an efficient DelegatedFunction,
    // - leave IR unchanged (don't delegate), but issue a warning.
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        public class Context
        {
            public readonly CallNode CallerNode;

            public readonly DelegableIntermediateNode CallerTableNode;

            public readonly Stack<IDictionary<string, RetVal>> WithScopes;

            public readonly RetVal CallerTableRetVal;

            public bool _ignoreDelegation;            

            public Context()
            {
                WithScopes = new ();
            }

            private Context(bool ignoreDelegation, Stack<IDictionary<string, RetVal>> withScopes, CallNode callerNode, RetVal callerTableRetVal)
            {
                WithScopes = withScopes;
                CallerNode = callerNode;
                CallerTableNode = callerTableRetVal._sourceTableIRNode;
                CallerTableRetVal = callerTableRetVal;
                _ignoreDelegation = ignoreDelegation;
            }

            public bool IsPredicateEvalInProgress => CallerNode != null && CallerTableNode != null;

            public bool IsDataverseDelegation => CallerTableRetVal.Metadata != null;

            public Context GetContextForPredicateEval(CallNode callerNode, RetVal callerTableNode)
            {
                return new Context(this._ignoreDelegation, this.WithScopes, callerNode, callerTableNode);
            }

            internal void PushWithScope(IDictionary<string, RetVal> withScope)
            {
                WithScopes.Push(withScope);
            }

            internal IDictionary<string, RetVal> PopWithScope()
            {
                return WithScopes.Pop();
            }
        }
    }
}
