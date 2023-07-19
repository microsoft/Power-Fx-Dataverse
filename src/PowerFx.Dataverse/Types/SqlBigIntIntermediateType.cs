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
    /// A type to represent intermediate calculations with bigint type
    /// Only used to avoid overflows
    /// </summary>
    [DebuggerDisplay("{_type}:WB")]
    internal class SqlBigIntIntermediateType : SqlNumberBase
    {
        public SqlBigIntIntermediateType() : base()
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
