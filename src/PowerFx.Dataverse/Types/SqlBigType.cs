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
    /// A type to represent a numeric value as a "big" value in CDS code, e.g. decimal(38,9)   
    /// CDS bigint type range is [-9223372036854775808 ... +9223372036854775807]
    /// </summary>
    [DebuggerDisplay("{_type}:B")]
    public class SqlBigType : SqlNumberBase
    {
        public SqlBigType() : base()
        {
        }
        public override void Visit(ITypeVisitor vistor)
        {
            vistor.Visit(this);
        }

        internal override string ToSqlType()
        {
            // Supports [-79228162514264337593.543950335 ... +79228162514264337593.543950335]
            return "decimal(38,9)";
        }
    }
}
