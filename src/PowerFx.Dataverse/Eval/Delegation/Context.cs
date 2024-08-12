// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Texl;
using Microsoft.PowerFx.Core.Types.Enums;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.DelegatedFunctions;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;
using BinaryOpNode = Microsoft.PowerFx.Core.IR.Nodes.BinaryOpNode;
using CallNode = Microsoft.PowerFx.Core.IR.Nodes.CallNode;
using RecordNode = Microsoft.PowerFx.Core.IR.Nodes.RecordNode;
using Span = Microsoft.PowerFx.Syntax.Span;
using UnaryOpNode = Microsoft.PowerFx.Core.IR.Nodes.UnaryOpNode;

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
