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
    /// A type to represent a numeric value represented as a decimal value, e.g. decimal(29,10)
    /// </summary>
    [DebuggerDisplay("{_type}:d")]
    public class SqlDecimalType : SqlNumberBase
    {
        public SqlDecimalType() : base()
        {
        }
        public override void Visit(ITypeVisitor vistor)
        {
            vistor.Visit(this);
        }
        internal override string ToSqlType()
        {
            return "decimal(29,10)";
        }
    }
}
