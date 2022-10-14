//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.Errors;
using Microsoft.PowerFx.Core.Public;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Result of a compilation 
    /// </summary>
    public class SqlCompileResult : CheckResult
    {
        public SqlCompileResult() {  }

        public SqlCompileResult(CheckResult inner)
        {
            this.ReturnType = inner.ReturnType;
            this.TopLevelIdentifiers = inner.TopLevelIdentifiers;
            this.Errors = inner.Errors;
        }
        
        /// <summary>
        /// A SQL UDF. This is referenced by SqlCreateRow
        /// </summary>
        public string SqlFunction { get; set; }

        /// <summary>
        /// The text that goes into the Sql CREATE table statement.
        /// </summary>
        public string SqlCreateRow { get; set; }

        /// <summary>
        /// A modified version of the formula that contains only logical names
        /// </summary>
        public string LogicalFormula { get; set; }

        /// <summary>
        /// A modified version of the formula that does not contain string or numeric literals, for telemetry purposes
        /// </summary>
        public string SanitizedFormula { get; set; }

        // Test harness can use to inspect exceptions.
        internal List<string> _unsupportedWarnings;

        /// <summary>
        /// A dictionary of field logical names on related records, indexed by the related entity logical name
        /// </summary>
        /// <example>
        /// On account, the formula "Name & 'Primary Contact'.'Full Name'" would return
        ///    "contact" => { "fullname" }
        /// The formula "Name & 'Primary Contact'.'Full Name' & Sum(Contacts, 'Number Of Childeren')" would return
        ///    "contact" => { "fullname", "numberofchildren" }
        /// </example>
        public Dictionary<string, HashSet<string>> RelatedIdentifiers { get; set; }

        /// <summary>
        /// A dictionary of relationship schema names, indexed by the entity logical name
        /// </summary>
        /// <example>
        /// On account, the formula "Name & 'Primary Contact'.'Full Name'" would return
        ///    "account" => { "account_primary_contact" }
        /// </example>
        public Dictionary<string, HashSet<string>> DependentRelationships { get; set; }
    }
}
