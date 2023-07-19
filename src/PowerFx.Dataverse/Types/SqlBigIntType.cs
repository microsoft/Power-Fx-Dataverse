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
    /// A type to represent a numeric value as a bigint value +/- 9.22e18
    /// </summary>
    [DebuggerDisplay("{_type}:W")]
    internal class SqlBigIntType : SqlNumberBase
    {
        public SqlBigIntType() : base()
        {
        }
        public override void Visit(ITypeVisitor vistor)
        {
            vistor.Visit(this);
        }

        internal override string ToSqlType()
        {
            return "bigint";
        }
    }
}
