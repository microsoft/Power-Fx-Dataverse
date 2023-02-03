//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QueryExpression = Microsoft.Xrm.Sdk.Query.QueryExpression;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Policy for which tables to include from dataverse. 
    /// </summary>
    public abstract class Policy
    {
        protected DataverseConnection _parent;

        // Called once on init by DataverseConnection. 
        internal ReadOnlySymbolTable CreateSymbols(DataverseConnection parent, CdsEntityMetadataProvider metadataCache)
        {
            if (_parent != null)
            {
                throw new InvalidOperationException($"Symbols already created");
            }

            _parent = parent;
            var symbols = this.CreateSymbols(metadataCache);
            return symbols;
        }

        internal abstract ReadOnlySymbolTable CreateSymbols(CdsEntityMetadataProvider metadataCache);

        // Given a logical name, get the "variable name" - which is the name that should 
        // be used in an expression to refer to this.
        public abstract bool TryGetVariableName(string logicalName, out string variableName);

        /// <summary>
        /// Hook called by <see cref="DataverseConnection.AddTable(string, string)"/>.
        /// </summary>
        /// <param name="variableName"></param>
        /// <param name="tableLogicalName"></param>
        /// <returns></returns>
        internal abstract TableValue AddTable(string variableName, string tableLogicalName);
    }
}
