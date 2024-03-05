//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Dataverse.EntityMock;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Intellisense;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests
{

    public class DataverseIntellisenseTests
    {
        // For testing, provide a new engine instance each time to ensure their caches are reset between tests. 
        internal static PowerFx2SqlEngine _engine =>
            new PowerFx2SqlEngine(
                MockModels.RelationshipModels[0].ToXrm(),
                new CdsEntityMetadataProvider(new MockXrmMetadataProvider(MockModels.RelationshipModels)));

        internal static PowerFx2SqlEngine _allAttributesEngine => GetAllAttributesEngine(null);

        internal static PowerFx2SqlEngine GetAllAttributesEngine(CultureInfo locale) =>
            new PowerFx2SqlEngine(
                MockModels.AllAttributeModels[0].ToXrm(),
                new CdsEntityMetadataProvider(new MockXrmMetadataProvider(MockModels.AllAttributeModels)) { NumberIsFloat = DataverseEngine.NumberIsFloat },
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
            Assert.NotNull(expression);

            var cursorMatches = Regex.Matches(expression, @"\|");
            Assert.True(cursorMatches.Count == 1, "Invalid cursor.  Exactly one cursor must be specified.");
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
        [Theory]
        [InlineData("sel|", "Self")]
        public void CheckForbiddenKeywords(string expression, string unexpectedOutput)
        {
            Contracts.AssertValue(expression);
            var intellisense = Suggest(expression);
            Assert.DoesNotContain(intellisense.Suggestions, suggestion => suggestion.DisplayText.Text == unexpectedOutput);
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
        [Theory]
        [InlineData("ab|", "Abs")]
        [InlineData("TimeUni|", "TimeUnit",
            "TimeUnit.Days",
            "TimeUnit.Hours",
            "TimeUnit.Milliseconds",
            "TimeUnit.Minutes",
            "TimeUnit.Months",
            "TimeUnit.Quarters",
            "TimeUnit.Seconds",
            "TimeUnit.Years")]
        [InlineData("ErrorKin|")] // "ErrorKind is excluded"
        [InlineData("DateTimeFo|")] // "DateTimeFormat is excluded"
        [InlineData("Ye|", "TimeUnit.Years", "Year")] // "Only Namespaced Enums"
        [InlineData("DateAdd(x, 1,|",
            "'Boolean (Locals)'",
            "'Global Picklist'",
            "'Rating (Locals)'",
            "'State (Locals)'",
            "Status",
            "TimeUnit.Days",
            "TimeUnit.Hours",
            "TimeUnit.Milliseconds",
            "TimeUnit.Minutes",
            "TimeUnit.Months",
            "TimeUnit.Quarters",
            "TimeUnit.Seconds",
            "TimeUnit.Years")] // "TimeUnit inside DateAdd"
        [InlineData("Text(UTCToday(),|",
            "'Boolean (Locals)'",
            "'Global Picklist'",
            "'Rating (Locals)'",
            "'State (Locals)'",
            "Status")] // "DateTimeFormat in Text on Date"            
        [InlineData("Locals|", "'Boolean (Locals)'", "'Rating (Locals)'", "'State (Locals)'")] // "One To Many not shown"
        [InlineData("Sel|", "'Self Reference'")] // "Lookup (Many To One) is shown"
        [InlineData("Err|", "IfError", "IsError")] // "IfError and IsError are shown, but Error is excluded"
        [InlineData("Tod|", "IsUTCToday", "UTCToday")] // "Today and IsToday are not suggested"
        [InlineData("Pric|", "Old_Price", "Price")] // "Display Name of field is suggested, but logical name is not"
        [InlineData("Floa|", "Float")]
        [InlineData("Other.Actual|", "'Actual Float'")] 
        [InlineData("Other.Floa|", "'Actual Float'", "Float")] // "Name collisions with floating point fields are handled"
        [InlineData("Virtual|", "'Virtual Lookup'")] // "Lookups to virtual tables are still suggested"
        [InlineData("'Virtual Lookup'.|")] // "Fields on virtual tables are not"
        public void CheckSuggestions(string expression, params string[] expectedSuggestions)
        {
            var intellisense = Suggest(expression);
            var actualSuggestions = ToArray(intellisense);
            Assert.Equal(
                expectedSuggestions,
                actualSuggestions); // $"<Expected>: {string.Join(",", expectedSuggestions)} " + $"<Actual>: {string.Join(",", actualSuggestions)}");
        }

        [Theory]
        [InlineData("Rat|", "Rating", "'Rating (Locals)'")] // "Picklist name with no conflict"
        [InlineData("'Rating (Locals)'.|", "Cold", "Hot", "Warm")] // "Disambiguated picklist values with no conflict"
        [InlineData("Other.Rating + Rating|", "Rating", "'Rating (Locals)'", "'Rating (Remotes)'")] // "Picklist with conflict"
        [InlineData("Other.Rating + 'Rating (Locals)'.|", "Cold", "Hot", "Warm")] // "Explicit Picklist one values with conflict"
        [InlineData("Other.Rating + 'Rating (Remotes)'.|", "Large", "Medium", "Small")] // "Explicit Picklist two values with conflict"
        [InlineData("Global|", "[@'Global Picklist']", "'Global Picklist'")] // "Global picklist"
        [InlineData("[@'Global Picklist'].|", "High", "Low", "Medium")] // "Global picklist values"
        public void CheckOptionSetSuggestions(string expression, params string[] expectedSuggestions)
        {
            var intellisense = Suggest(expression);
            var actualSuggestions = ToArray(intellisense);
            Assert.Equal(expectedSuggestions, actualSuggestions);
        }

        // Engines have an intellisense cache. 
        // The cache grows as we evaluate lookups into other tables.
        // This means users can get more intellisense suggestions the more evaluations they do. 
        [Fact]
        public void CheckOptionSetSuggestionsCaches()
        {
            var engine = _engine;

            List<string> expected = new List<string> { "Rating", "'Rating (Locals)'" };
            Assert.Equal(2, expected.Count);

            var intellisense = Suggest("Rat|", engine);
            var actualSuggestions = ToArray(intellisense);
            Assert.Equal(expected, actualSuggestions);


            // Suggestion to cache in more metadata from another table. This will bring in "'Rating (Remotes)'"            
            Suggest("Other.Rating|", engine);
            expected.Add("'Rating (Remotes)'");

            // Now repeating the original request will get more suggestions. 
            intellisense = Suggest("Rat|", engine);
            actualSuggestions = ToArray(intellisense);
            Assert.Equal(expected, actualSuggestions);
            Assert.Equal(3, expected.Count);
        }

        [Theory]
        [InlineData("Strin|", "String")] // "String suggested"
        [InlineData("Hyperlin|")] // "Hyperlink not suggested"
        [InlineData("Emai|")] // "Email not suggested"
        [InlineData("Ticke|")] // "Ticker not suggested"        
        [InlineData("Duratio|")] // "Duration not suggested"
        [InlineData("Doubl|", "Double")] // "Double suggested"
        [InlineData("Mone|", "Money")] // "Currency suggested"
        [InlineData("Imag|")] // "Image not suggested"
        [InlineData("Fil|")] // "File not suggested"
        [InlineData("MultiSelec|", "MultiSelect", "'MultiSelect (All Attributes)'")] // "MultiSelect suggested"
        public void CheckUnsupportedTypeSuggestions(string expression, params string[] expectedSuggestions)
        {
            var intellisense = Suggest(expression, _allAttributesEngine);
            var actualSuggestions = ToArray(intellisense);
            Assert.Equal(expectedSuggestions, actualSuggestions);
        }

        [Fact]
        public void CheckSuggestion()
        {
            var intellisense = Suggest("a|");

            // implemented function: Abs
            Assert.Contains(intellisense.Suggestions, sug => sug.Kind == SuggestionKind.Function && sug.DisplayText.Text == "Abs");

            // keyword:false
            Assert.Contains(intellisense.Suggestions, sug => sug.Kind == SuggestionKind.KeyWord && sug.DisplayText.Text == "false");

            // TODO: this will be false until additional math operations are implemented
            Assert.DoesNotContain(intellisense.Suggestions, sug => sug.DisplayText.Text == "Acos");
        }
    }
}
