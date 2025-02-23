// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse.Eval.Core
{
    /// <summary>
    /// This class is used to "smuggle" a FilterExpression into a FormulaValue. So another function can use it
    /// to combine or execute FilterExpressions. e.g. <see cref="DelegatedAnd"/>"/> or <see cref="DelegatedRetrieveMultipleFunction"/>"/>.
    /// </summary>
    internal class DelegationFormulaValue : ValidFormulaValue
    {
        /// <summary>
        /// Filter to apply while retrieving records.
        /// </summary>
        internal readonly FxFilterExpression _filter;

        /// <summary>
        /// Count of records to return. If null, all records are returned.
        /// </summary>
        internal readonly int? _top;

        internal readonly string _partitionId;

        // OrderBy commands
        internal readonly IList<OrderExpression> _orderBy;

        // Multiple Joins is needed in case there is and condition on two different tables dot walking. Today we only support one join.
        internal readonly ISet<FxJoinNode> _join;

        internal readonly FxGroupByNode _groupBy;

        internal DelegationFormulaValue(FxFilterExpression filter, IList<OrderExpression> orderBy, FxGroupByNode groupBy = null, ISet<FxJoinNode> join = null, string partitionId = null, int? top = null)
            : base(IRContext.NotInSource(FormulaType.Blank))
        {
            _filter = filter ?? new FxFilterExpression();
            _orderBy = orderBy ?? new List<OrderExpression>();
            _top = top;
            _join = join ?? new HashSet<FxJoinNode>(new JoinComparer());

            if (_join.Count > 1)
            {
                throw new InvalidOperationException("Multiple joins not supported");
            }

            _groupBy = groupBy;
            _partitionId = partitionId;
        }

        public override void Visit(IValueVisitor visitor)
        {
            throw new NotImplementedException();
        }

        public override void ToExpression(StringBuilder sb, FormulaValueSerializerSettings settings)
        {
            throw new NotImplementedException();
        }

        public override object ToObject()
        {
            throw new NotImplementedException("DelegationFormulaValue.ToObject not implemented");
        }
    }
}
