//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System.Diagnostics;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse
{

    /// <summary>
    /// A type to represent a numeric value as a "big" value in CDS code, e.g. decimal(38,10)
    /// </summary>
    [DebuggerDisplay("{_type}:i")]
    public class SqlIntType : SqlNumberBase
    {
        public SqlIntType() : base()
        {
        }

        public override void Visit(ITypeVisitor vistor)
        {
            vistor.Visit(this);
        }

        internal override string ToSqlType()
        {
            return "int";
        }
    }
}