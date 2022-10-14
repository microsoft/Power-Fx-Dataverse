//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.AppMagic.Authoring;
using Microsoft.PowerFx.Types;
using Microsoft.PowerFx.Core.Types;

namespace Microsoft.PowerFx.Dataverse
{
    public class SqlMoneyType : FormulaType
    {
        public SqlMoneyType() : base(DType.Currency)
        {
        }

        public override void Visit(ITypeVisitor vistor)
        {
            // should never be visited
            throw new System.NotImplementedException();
        }
    }
}

