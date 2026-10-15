// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Linq;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class SqlLiteralEncodingTests
    {
        private static string CompileToSql(string expr)
        {
            var engine = new PowerFx2SqlEngine();
            var result = engine.Compile(expr, new SqlCompileOptions() { UdfName = "test" });
            Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(err => err.Message)));
            return result.SqlFunction;
        }

        [Theory]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("it's")]
        [InlineData("it\u02BCs")]
        [InlineData("'\u02BC'")]
        public void NormalizeIsConsistentWithEncode(string input)
        {
            var expected = "'" + CrmEncodeDecode.SqlLiteralNormalize(input).Replace("'", "''") + "'";
            Assert.Equal(expected, CrmEncodeDecode.SqlLiteralEncode(input));
            Assert.Equal("N" + expected, SqlVisitor.EncodeStringLiteral(input));
        }

        [Theory]
        [InlineData("\"it's\"")]
        [InlineData("\"it\u02BCs\"")]
        public void TextLiteralEncoding(string expr)
        {
            Assert.Contains("N'it''s'", CompileToSql(expr));
        }

        [Theory]
        [InlineData("StartsWith(\"abc\", \"ab\")", "LIKE N'ab%'")]
        [InlineData("EndsWith(\"abc\", \"bc\")", "LIKE N'%bc'")]
        [InlineData("\"b\" in \"abc\"", "LIKE N'%b%'")]
        [InlineData("StartsWith(\"it's here\", \"it's\")", "LIKE N'it''s%'")]
        [InlineData("StartsWith(\"it's here\", \"it\u02BCs\")", "LIKE N'it''s%'")]
        [InlineData("EndsWith(\"five o'clock\", \"o'clock\")", "LIKE N'%o''clock'")]
        [InlineData("EndsWith(\"five o'clock\", \"o\u02BCclock\")", "LIKE N'%o''clock'")]
        [InlineData("\"o'c\" in \"five o'clock\"", "LIKE N'%o''c%'")]
        [InlineData("\"o\u02BCc\" in \"five o'clock\"", "LIKE N'%o''c%'")]
        [InlineData("\"o\u02BCc\" exactin \"five o'clock\"", "LIKE N'%o''c%'")]
        [InlineData("StartsWith(\"abc\", \"50%_[a]'\")", "LIKE N'50[%][_][[]a]''%'")]
        public void LikeArgumentEncoding(string expr, string expected)
        {
            var sql = CompileToSql(expr);
            Assert.Contains(expected, sql);
            Assert.DoesNotContain("\u02BC", sql);
        }

        [Fact]
        public void TextFormatEncoding()
        {
            // ordinary format strings are unchanged
            Assert.Matches(@"FORMAT\(ISNULL\(@v\d+,0\), N'0'\)", CompileToSql("Text(5, \"0\")"));
            Assert.Matches(@"FORMAT\(ISNULL\(@v\d+,0\), N'00000'\)", CompileToSql("Text(5, \"00000\")"));

            var withApostrophe = CompileToSql("Text(5, \"0'\")");
            Assert.Matches(@"FORMAT\(ISNULL\(@v\d+,0\), N'0[^']*''[^']*'\)", withApostrophe);

            var withModifierApostrophe = CompileToSql("Text(5, \"0\u02BC\")");
            Assert.Equal(withApostrophe, withModifierApostrophe);
        }
    }
}
