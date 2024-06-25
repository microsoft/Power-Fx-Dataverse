using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.PowerFx.Types;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class ExampleTests
    {
        // Return expression contents from file \DelegationTests\ExpressionExamples\{shortFilename}
        private static string ReadFile(string shortFilename)
        {
            var file = Path.Join(Directory.GetCurrentDirectory(), "DelegationTests", "ExpressionExamples", shortFilename);

            string expr = File.ReadAllText(file);
            return expr;
        }

        // $$$ Ensure expectDelegationFailures=false in all cases. 
        [Theory]
        [InlineData("FilterLiteralWithDateTimeValue.txt")]
        [InlineData("FilterLiteralWithGuid.txt")]
        [InlineData("FilterLiteralWithInteger.txt")]
        [InlineData("FilterLiteralWithString.txt")]
        [InlineData("ProposalIssuerApproverRelatedPartyFilter.txt")]
        [InlineData("ProposalIssuerRelatedPartyFilter.txt")]
        [InlineData("ProposalMultipleFiltersWithLiteral.txt")]
        [InlineData("ProposalMultipleFiltersWithTwoLiterals.txt")]
        [InlineData("ProposalSingleFilter.txt")]

        // $$$ Sort doesn't delegate: https://github.com/microsoft/Power-Fx-Dataverse/issues/510
        [InlineData("ProposalNoFilter.txt", true)]
        
        // $$$ C# mocks don't support polymorphism
        //[InlineData("ProposalIssuerRelatedPartyNoFilterPolymorphic.txt", true)] // polymorphism
        public void BasicCompile(string shortFilename, bool expectDelegationFailures = false)
        {
            var symbols = GetSymbols();
            var opts = new ParserOptions { MaxExpressionLength = 5000 };

            string expr = ReadFile(shortFilename);

            // First do a basic sanity-check compilation without delegation. 
            // This should always pass. 
            {
                var engine = new RecalcEngine();
                var check = new CheckResult(engine)
                    .SetText(expr, opts)
                    .SetBindingInfo(symbols);

                var errors = check.ApplyErrors();

                if (errors.Count() > 0)
                {
                    string msg = string.Join(";", errors.Select(e => e.Message));
                    Assert.Fail($"Basic compile failed with {errors.Count()} errors: {msg}");
                }
            }

            // Now compile with delegation and verify there are no delegation warnings.
            {
                var engine = new RecalcEngine();

                engine.EnableDelegation(); // Important!!

                var check = new CheckResult(engine)
                    .SetText(expr, opts)
                    .SetBindingInfo(symbols);

                var errors = check.ApplyErrors();

                // $$$ check for delegation warnings. 
                var delegationWarnings = errors.Where(e => e.MessageKey == "WrnDelegationTableNotSupported").ToArray();

                if (delegationWarnings.Length > 0)
                {
                    Assert.True(expectDelegationFailures);
                }
                else
                {
                    Assert.False(expectDelegationFailures);
                }
            }           
        }

        #region Schema
        // aib_proposal
        private class AibProposal
        {
            public Guid aib_proposalid { get; set; }

            public DateTime modifiedon { get; set; }

            public string aib_name { get; set; }
            public string aib_status { get; set; }
            public double aib_agreementdurationamount { get; set; }
            public DateTime  aib_agreementdurationunits { get; set; }

            public AibIssuer aib_Issuer { get; set; }
            public AibIssuer aib_Approver { get; set; }
        }

        private class AibIssuer
        {
            public Guid aib_issuerid { get; set; }
            public string aib_name { get; set; }
            public string aib_title { get; set; }
        }

        private class AibRelatedParty
        {
            public Guid aib_relatedpartyid { get; set; }
            public string aib_name { get; set; }
            public string aib_referredtype { get; set; }

            public AibProposal aib_Proposal { get; set; }
        }
        #endregion

        private readonly TypeMarshallerCache _typeCache = new TypeMarshallerCache();

        private void AddTable<T>(SymbolTable symbolTable, string tableName)
        {
            var marshaller = _typeCache.GetMarshaller(typeof(T));
            var tableType = ((RecordType)marshaller.Type).ToTable();

            symbolTable.AddVariable(tableName, tableType);            
        }

        public ReadOnlySymbolTable GetSymbols()
        {
            var st = new SymbolTable { DebugName = "Delegable_1" }; // name allows delegation

            AddTable<AibProposal>(st, "aib_proposal");
            AddTable<AibIssuer>(st, "aib_issuer");
            AddTable<AibRelatedParty>(st, "aib_relatedparty");


            // Input parameters
            st.AddVariable("inputStatus", FormulaType.String);
            st.AddVariable("inputName", FormulaType.String);

            return st;
        }
    }
}
