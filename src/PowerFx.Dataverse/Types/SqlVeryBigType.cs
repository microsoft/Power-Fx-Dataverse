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
    /// A type to represent a numeric value as a "very big" value -- decimal(38,0)
    /// Used for intermediate calculations to avoid overflows when manipulating "big" values
    /// </summary>
    [DebuggerDisplay("{_type}:G")]
    internal class SqlVeryBigType : SqlNumberBase
    {
        public SqlVeryBigType() : base()
        {
        }
        public override void Visit(ITypeVisitor vistor)
        {
            vistor.Visit(this);
        }

        internal override string ToSqlType()
        {
            return "decimal(38,0)";
        }
    }
}
