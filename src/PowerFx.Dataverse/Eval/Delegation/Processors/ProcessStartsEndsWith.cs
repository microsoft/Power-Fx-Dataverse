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
using Microsoft.PowerFx.Dataverse.DataSource;
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
    internal partial class DelegationIRVisitor : RewritingIRVisitor<DelegationIRVisitor.RetVal, DelegationIRVisitor.Context>
    {
        private RetVal ProcessStartsEndsWith(CallNode node, Context context, bool isStartWith)
        {
            if (!context.IsPredicateEvalInProgress)
            {
                return base.Visit(node, context);
            }

            IList<string> relations = null;

            if ((TryGetFieldName(context: context, left: node.Args[0], right: node.Args[1], op: BinaryOpKind.Invalid, out var fieldName, out var rightNode, out _) 
                || TryGetRelationField(context: context, left: node.Args[0], right: node.Args[1], op: BinaryOpKind.Invalid, out fieldName, out relations, out rightNode, out _)) 
                &&
                context.CallerTableRetVal.AssociatedDS.DoesColumnSupportStartsEndsWith(fieldName, context.CallerTableRetVal.TableType.GetFieldType(fieldName), isStartWith))
            {
                var startsEndsWithNode = _hooks.MakeStartsEndsWithCall(context.CallerTableNode, context.CallerTableRetVal.TableType, relations, fieldName, rightNode, context.CallerNode.Scope, isStartWith);
                
                var ret = CreateBinaryOpRetVal(context, node, startsEndsWithNode);
                return ret;
            }

            RetVal arg0c = node.Args[0].Accept(this, context);

            return base.Visit(node, context, arg0c);
        }
    }
}
