using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Text;

namespace Microsoft.PowerFx.Dataverse.Eval.Core
{
    internal class DelegationFormulaValue : ValidFormulaValue
    {
        internal FilterExpression _value;
        internal int? _top;

        internal DelegationFormulaValue(FilterExpression value, int? top = null)
            : base(IRContext.NotInSource(FormulaType.Blank))
        {
            _value = value;
            _top = top;
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
