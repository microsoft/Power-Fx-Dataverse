using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Text;

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
        internal readonly FilterExpression _value;

        /// <summary>
        /// Count of records to return. If null, all records are returned.
        /// </summary>
        internal readonly int? _top;

        internal readonly ISet<LinkEntity> _relation;

        internal DelegationFormulaValue(FilterExpression value, ISet<LinkEntity> relation, int? top = null)
            : base(IRContext.NotInSource(FormulaType.Blank))
        {
            _value = value ?? new FilterExpression();
            _top = top;
            _relation = relation ?? new HashSet<LinkEntity>(new LinkEntityComparer());
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
            return _value;
        }
    }
}
