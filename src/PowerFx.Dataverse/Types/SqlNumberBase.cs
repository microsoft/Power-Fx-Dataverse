//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// A base type to represent the various numeric types that SQL supports on top of Number (int, decimal, float, etc.)
    /// </summary>
    public abstract class SqlNumberBase : NumberType
    {
        public SqlNumberBase() : base()
        {
        }

        public override void Visit(ITypeVisitor vistor)
        {
            vistor.Visit(this);
        }

        /// <summary>
        /// Convert the formula type to a string for SQL generation
        /// </summary>
        /// <returns>The string representation of the type</returns>
        internal abstract string ToSqlType();
    }
}
