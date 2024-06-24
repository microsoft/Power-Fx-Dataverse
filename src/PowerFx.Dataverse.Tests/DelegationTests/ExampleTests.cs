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
        private  static string ReadFile(string shortFilename)
        {
            var file = Path.Join(Directory.GetCurrentDirectory(), "DelegationTests", "ExpressionExamples", shortFilename);

            string expr = File.ReadAllText(file);
            return expr;
        }

        [Theory]
        [InlineData("FilterLiteralWithDateTimeValue.txt")]
        [InlineData("FilterLiteralWithGuid.txt")]
        [InlineData("FilterLiteralWithInteger.txt")]
        [InlineData("FilterLiteralWithString.txt")]
        [InlineData("ProposalIssuerApproverRelatedPartyFilter.txt")]
        [InlineData("ProposalIssuerRelatedPartyFilter.txt")]
        [InlineData("ProposalMultipleFiltersWithLiteral.txt")]
        [InlineData("ProposalMultipleFiltersWithTwoLiterals.txt")]
        [InlineData("ProposalNoFilter.txt")]
        [InlineData("ProposalSingleFilter.txt")]
        public void BasicCompile(string shortFilename)
        {
            var symbols = GetSymbols();

            string expr = ReadFile(shortFilename);

            var engine = new RecalcEngine();
            var check = new CheckResult(engine)
                .SetText(expr, new ParserOptions { MaxExpressionLength = 5000 })
                .SetBindingInfo(symbols);

            var errors = check.ApplyErrors();

            // $$$ check for delegation warnings. 
            if (errors.Count() > 0)
            {
                string msg = string.Join(";", errors.Select(e => e.Message));
                Assert.Fail($"Basic compile failed with {errors.Count()} errors: {msg}");
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
            var st = new SymbolTable { DebugName = "PromptBuilder" };

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
