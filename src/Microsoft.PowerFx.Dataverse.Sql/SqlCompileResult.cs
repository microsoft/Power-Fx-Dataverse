//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.PowerFx.Core.Errors;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Result of a compilation 
    /// </summary>
    public class SqlCompileResult : CheckResult
    {
        public SqlCompileResult(PowerFx2SqlEngine engine)
        : base(engine)
        {  
        }

        internal SqlCompileResult(IEnumerable<IDocumentError> errors)
            : base(ExpressionError.New(errors))
        {
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

        /// <summary>
        /// A modified version of the formula that does not contain string or numeric literals, for telemetry purposes
        /// </summary>
        public bool IsHintApplied { get; set; }

        /// <summary>
        /// OptionsetId of the optionset returned by formula fields of type optionset
        /// </summary>
        /// /// <example>
        /// expression: "If( 'Option1' = 'Option1 (Table)'.Choice1, 'Option2 (Table)'.Choice1, 'Option2 (Table)'.Choice2)"
        /// OptionsetId of the Optionset - 'Option2 (Table)'
        /// </example>
        public Guid OptionSetId { get; set; }

        // Test harness can use to inspect exceptions.
        internal List<string> _unsupportedWarnings;

        // Results of SQL visitor. 
        internal SqlCompileInfo _sqlInfo;

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

        /// <summary>
        /// Hashset of optionsetids of optionsets used by formula fields
        /// </summary>
        /// <example>
        /// expression: "If( 'Option1 (Table)'.Choice1 = 'Option1 (Table)'.Choice1, 100, 'Option2 (Table)'.Choice1 = 'Option2 (Table)'.Choice1, 200, 300)"
        /// OptionsetIds of Optionsets => {'Option1 (Table)', 'Option2 (Table)'}
        /// </example>
        public HashSet<Guid> DependentOptionSetIds { get; set; }
    }

    // Additional info computed by the SQL comilation work
    internal class SqlCompileInfo
    {
        internal SqlVisitor.RetVal _retVal;

        internal SqlVisitor.Context _ctx;
    }
}
