// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation
{
    internal class JoinFormulaValue : ValidFormulaValue
    {
        private readonly FxJoinNode _join;

        internal FxJoinNode JoinNode => _join;

        public JoinFormulaValue(FxJoinNode join, TableType tableType)
            : base(IRContext.NotInSource(tableType))
        {
            _join = join;
        }

        public override void ToExpression(StringBuilder sb, FormulaValueSerializerSettings settings)
        {
            sb.Append(DescribeJoinTransformationNode(_join));
        }

        public override object ToObject()
        {
            return _join;
        }

        public override void Visit(IValueVisitor visitor)
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            return DescribeJoinTransformationNode(_join);
        }

        private static string DescribeJoinTransformationNode(FxJoinNode joinNode)
        {
            if (joinNode == null)
            {
                return "__noJoin()";
            }

            return joinNode.ToString();
        }
    }
}
