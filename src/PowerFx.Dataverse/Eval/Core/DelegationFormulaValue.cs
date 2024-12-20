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
    /// to combine or execute FilterExpressions. e.g. <see cref="DelegatedAnd"/>"/> or <see cref="DelegatedRetrieveMultipleFunction"/>"/>
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

        internal readonly ISet<LinkEntity> _relation;

        internal readonly string _partitionId;

        // OrderBy commands
        internal readonly IList<OrderExpression> _orderBy;

        internal readonly FxGroupByNode _groupBy;

        internal DelegationFormulaValue(FxFilterExpression filter, ISet<LinkEntity> relation, IList<OrderExpression> orderBy, FxGroupByNode groupBy = null, string partitionId = null, int? top = null)
            : base(IRContext.NotInSource(FormulaType.Blank))
        {
            _filter = filter ?? new FxFilterExpression();
            _orderBy = orderBy ?? new List<OrderExpression>();
            _top = top;
            _relation = relation ?? new HashSet<LinkEntity>(new LinkEntityComparer());
            _partitionId = partitionId;
            _groupBy = groupBy;
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
