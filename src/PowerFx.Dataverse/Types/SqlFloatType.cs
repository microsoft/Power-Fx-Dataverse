//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse
{
    using System.Diagnostics;

    /// <summary>
    /// A type to represent a numeric value as a floating point
    /// </summary>
    [DebuggerDisplay("{_type}:f")]
    public class SqlFloatType : SqlNumberBase
    {
        public SqlFloatType() : base()
        {
        }

        public override void Visit(ITypeVisitor vistor)
        {
            vistor.Visit(this);
        }

        internal override string ToSqlType()
        {
            return "float";
        }
    }
}