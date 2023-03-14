//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AppMagic;
using Microsoft.AppMagic.Authoring;
using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    [TestClass]
    public class DataverseIntellisenseTests
    {
        // For testing, provide a new engine instance each time to ensure their caches are reset between tests. 
        internal static PowerFx2SqlEngine _engine =>
            new PowerFx2SqlEngine(
                DataverseTests.RelationshipModels[0].ToXrm(),
                new CdsEntityMetadataProvider(new MockXrmMetadataProvider(DataverseTests.RelationshipModels)));

        internal static PowerFx2SqlEngine _allAttributesEngine => GetAllAttributesEngine(null);

        internal static PowerFx2SqlEngine GetAllAttributesEngine(CultureInfo locale) =>
            new PowerFx2SqlEngine(
                DataverseTests.AllAttributeModels[0].ToXrm(),
                new CdsEntityMetadataProvider(new MockXrmMetadataProvider(DataverseTests.AllAttributeModels)),
                locale);

        /// <summary>
        /// This method receives a test case string, along with an optional context type that defines the valid
        /// names and types in the expression and invokes Intellisense.Suggest on it, and returns a the result
        /// </summary>
        /// <param name="expression">
        /// Expression in which is expected exactly one occurence of | to indicate cursor position
        /// </param>
        /// <param name="engine">
        /// The engine to use when performing the suggest.  Optional.  Defaults to the base engine
        /// </param>
        /// <returns>
        /// An intellisense result to be tested
        /// </returns>
        internal IIntellisenseResult Suggest(string expression, PowerFx2SqlEngine engine = null)
        {
            engine ??= _engine;
            Assert.IsNotNull(expression);

            var cursorMatches = Regex.Matches(expression, @"\|");
            Assert.IsTrue(cursorMatches.Count == 1, "Invalid cursor.  Exactly one cursor must be specified.");
            var cursorPosition = cursorMatches.First().Index;

            expression = expression.Replace("|", string.Empty);

            return engine.Suggest(expression, cursorPosition);
        }

        /// <param name="expression">
        /// The formula on which Intellisense should be run.  The index of the | represents the cursor
        /// position that would be considered.
        /// </param>
        /// <param name="unexpectedOutput">
        /// The value of the suggestion which should not be shown in the list
        /// </param>
        [DataTestMethod, Owner("jokellih")]
        [DataRow("sel|", "Self")]
        public void CheckForbiddenKeywords(string expression, string unexpectedOutput)
        {
            Contracts.AssertValue(expression);
            var intellisense = Suggest(expression);
            Assert.IsFalse(intellisense.Suggestions.Any(suggestion => suggestion.DisplayText.Text == unexpectedOutput));
        }

        static string[] ToArray(IIntellisenseResult intellisense)
        {
            return intellisense.Suggestions.Select(suggestion => suggestion.DisplayText.Text).ToArray();
        }

        /// <summary>
        /// Compares expected suggestions with suggestions made by PFx Intellisense for a given
        /// <see cref="expression"/> and cursor position. The cursor position is determined by the index of
        /// the | character in <see cref="expression"/>, which will be removed but the test is run. Note that
        /// the use of the `|` char is for this reason disallowed from test cases except to indicate cursor
        /// position. Note also that these test suggestion order as well as contents.
        /// </summary>
        /// <param name="expression">
        /// Expression on which intellisense will be run
        /// </param>
        /// <param name="expectedSuggestions">
        /// A list of arguments that will be compared with the names of the output of
        /// <see cref="Workspace.Suggest"/> in order
        /// </param>
        [DataTestMethod, Owner("jokellih")]
        [DataRow("ab|", "Abs")]
        [DataRow("TimeUni|", "TimeUnit",
            "TimeUnit.Days",
            "TimeUnit.Hours",
            "TimeUnit.Milliseconds",
            "TimeUnit.Minutes",
            "TimeUnit.Months",
            "TimeUnit.Quarters",
            "TimeUnit.Seconds",
            "TimeUnit.Years")]
        [DataRow("ErrorKin|", DisplayName = "ErrorKind is excluded")]
        [DataRow("DateTimeFo|", DisplayName = "DateTimeFormat is excluded")]
        [DataRow("Ye|", "Year", "TimeUnit.Years", DisplayName = "Only Namespaced Enums")]
        [DataRow("DateAdd(x, 1,|",
            "'Global Picklist'",
            "'Rating (Locals)'",
            "TimeUnit.Days",
            "TimeUnit.Hours",
            "TimeUnit.Milliseconds",
            "TimeUnit.Minutes",
            "TimeUnit.Months",
            "TimeUnit.Quarters",
            "TimeUnit.Seconds",
            "TimeUnit.Years",
            DisplayName = "TimeUnit inside DateAdd")]
        [DataRow("Text(UTCToday(),|",
            "'Global Picklist'",
            "'Rating (Locals)'", DisplayName = "DateTimeFormat in Text on Date")]
        [DataRow("Locals|", "'Rating (Locals)'", DisplayName = "One To Many not shown")]
        [DataRow("Sel|", "'Self Reference'", DisplayName = "Lookup (Many To One) is shown")]
        [DataRow("Err|", "IfError", "IsError", DisplayName = "IfError and IsError are shown, but Error is excluded")]
        [DataRow("Tod|", "IsUTCToday", "UTCToday", DisplayName = "Today and IsToday are not suggested")]
        [DataRow("Pric|", "Price", DisplayName = "Display Name of field is suggested, but logical name is not")]
        [DataRow("Floa|", DisplayName = "Floating point fields are not suggested at all")]
        [DataRow("Other.Actual|", DisplayName = "Floating point fields on relationships are not suggested")]
        [DataRow("Other.Floa|", "Float", DisplayName = "Name collisions with floating point fields are handled")]
        [DataRow("Virtual|", "'Virtual Lookup'", DisplayName = "Lookups to virtual tables are still suggested")]
        [DataRow("'Virtual Lookup'.|", DisplayName = "Fields on virtual tables are not")]
        public void CheckSuggestions(string expression, params string[] expectedSuggestions)
        {
            var intellisense = Suggest(expression);
            var actualSuggestions = ToArray(intellisense);
            CollectionAssert.AreEqual(expectedSuggestions, actualSuggestions);
        }

        [DataTestMethod]
        [DataRow("Rat|", "Rating", "'Rating (Locals)'", DisplayName = "Picklist name with no conflict")]
        [DataRow("'Rating (Locals)'.|", "Cold", "Hot", "Warm", DisplayName = "Disambiguated picklist values with no conflict")]
        [DataRow("Other.Rating + Rating|", "Rating", "'Rating (Locals)'", "'Rating (Remotes)'", DisplayName = "Picklist with conflict")]
        [DataRow("Other.Rating + 'Rating (Locals)'.|", "Cold", "Hot", "Warm", DisplayName = "Explicit Picklist one values with conflict")]
        [DataRow("Other.Rating + 'Rating (Remotes)'.|", "Large", "Medium", "Small", DisplayName = "Explicit Picklist two values with conflict")]
        [DataRow("Global|", "[@'Global Picklist']", "'Global Picklist'", DisplayName = "Global picklist")]
        [DataRow("[@'Global Picklist'].|", "High", "Low", "Medium", DisplayName = "Global picklist values")]
        public void CheckOptionSetSuggestions(string expression, params string[] expectedSuggestions)
        {
            var intellisense = Suggest(expression);
            var actualSuggestions = ToArray(intellisense);
            CollectionAssert.AreEqual(expectedSuggestions, actualSuggestions);
        }

        // Engines have an intellisense cache. 
        // The cache grows as we evaluate lookups into other tables.
        // This means users can get more intellisense suggestions the more evaluations they do. 
        [TestMethod]
        public void CheckOptionSetSuggestionsCaches()
        {
            var engine = _engine;

            List<string> expected = new List<string> { "Rating", "'Rating (Locals)'" };
            Assert.AreEqual(2, expected.Count);

            var intellisense = Suggest("Rat|", engine);
            var actualSuggestions = ToArray(intellisense);
            CollectionAssert.AreEqual(expected, actualSuggestions);
            

            // Suggestion to cache in more metadata from another table. This will bring in "'Rating (Remotes)'"            
            Suggest("Other.Rating|", engine);
            expected.Add("'Rating (Remotes)'");

            // Now repeating the original request will get more suggestions. 
            intellisense = Suggest("Rat|", engine);
            actualSuggestions = ToArray(intellisense);
            CollectionAssert.AreEqual(expected, actualSuggestions);
            Assert.AreEqual(3, expected.Count);
        }

        [DataTestMethod]
        [DataRow("Strin|", "String", DisplayName = "String suggested")]
        [DataRow("Hyperlin|", DisplayName = "Hyperlink not suggested")]
        [DataRow("Emai|", DisplayName = "Email not suggested")]
        [DataRow("Ticke|", DisplayName = "Ticker not suggested")]
        [DataRow("Strin|", "String", DisplayName = "String suggested")]
        [DataRow("Duratio|", DisplayName = "Duration not suggested")]
        [DataRow("Doubl|", DisplayName = "Double not suggested")]
        [DataRow("Mone|", "Money", DisplayName = "Currency suggested")]
        [DataRow("Imag|", DisplayName = "Image not suggested")]
        [DataRow("Fil|", DisplayName = "File not suggested")]
        [DataRow("MultiSelec|", "MultiSelect", "'MultiSelect (All Attributes)'", DisplayName ="MultiSelect suggested")]
        public void CheckUnsupportedTypeSuggestions(string expression, params string[] expectedSuggestions)
        {
            var intellisense = Suggest(expression, _allAttributesEngine);
            var actualSuggestions = ToArray(intellisense);
            CollectionAssert.AreEqual(expectedSuggestions, actualSuggestions);
        }

        [TestMethod]
        public void CheckSuggestion()
        {
            var intellisense = Suggest("a|");

            // No crashes
            Assert.IsNull(intellisense.Exception);

            // implemented function: Abs
            Assert.IsTrue(intellisense.Suggestions.Any(sug => sug.Kind == SuggestionKind.Function && sug.DisplayText.Text == "Abs"));

            // keyword:false
            Assert.IsTrue(intellisense.Suggestions.Any(sug => sug.Kind == SuggestionKind.KeyWord && sug.DisplayText.Text == "false"));

            // TODO: this will be false until additional math operations are implemented
            Assert.IsFalse(intellisense.Suggestions.Any(sug => sug.DisplayText.Text == "Acos"));
        }
    }
}