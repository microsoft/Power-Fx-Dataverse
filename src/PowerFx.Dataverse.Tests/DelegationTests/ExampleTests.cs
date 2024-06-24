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
        // Return map of FileName --> Expression contents. 
        private static Dictionary<string, string> GetExpressionTests()
        {
            var baseDirectory = Path.Join(Directory.GetCurrentDirectory(), "DelegationTests", "ExpressionExamples");

            var files = Directory.GetFiles(baseDirectory, "*.txt", SearchOption.AllDirectories);

            var tests = new Dictionary<string, string>();
            foreach(var file in files)
            {
                string expr = File.ReadAllText(file); 

                string shortFilename = Path.GetFileName(file);
                tests[shortFilename] = expr;
            }

            return tests;
        }

        [Fact]
        public void BasicCompile()
        {
            // var expr = File.ReadAllText(@"C:\dev\power-fx-dataverse\src\PowerFx.Dataverse.Tests\DelegationTests\Examples\FilterLiteralWithString.txt");

            var symbols = GetSymbols();

            var tests = GetExpressionTests();

            int pass = 0;
            foreach (var kv in tests)
            {
                string shortName = kv.Key;
                string expr = kv.Value;

                var engine = new RecalcEngine();
                var check = new CheckResult(engine)
                    .SetText(expr, new ParserOptions { MaxExpressionLength = 5000 })
                    .SetBindingInfo(symbols);

                var errors = check.ApplyErrors();

                // $$$ check for delegation warnings. 
                if (errors.Count() > 0)
                {
                    string msg = string.Join(";", errors.Select(e => e.Message));
                    Assert.Fail($"{shortName} failed with {errors.Count()} errors: {msg}");
                } 
                else
                {
                    pass++;
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
