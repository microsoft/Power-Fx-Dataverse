//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using Microsoft.Dataverse.EntityMock;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Xunit;
using static Microsoft.PowerFx.Dataverse.SqlCompileOptions;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class FullTests
    {

        internal static readonly Dictionary<AttributeTypeCode, string> AttributeTypeCodeToSqlTypeDictionary = new Dictionary<AttributeTypeCode, string> {
                    { AttributeTypeCode.Integer, SqlStatementFormat.SqlIntegerType },
                    { AttributeTypeCode.Money, SqlStatementFormat.SqlMoneyType },
                    { AttributeTypeCode.Double, SqlStatementFormat.SqlFloatType }
                };

        [SkippableFact]
        public void SqlCompileBaselineTest()
        {
            using (var cx = GetSql())
            {
                var exprStr = DataverseTests.BaselineFormula;
                var metadata = DataverseTests.BaselineMetadata;

                CreateTable(cx, metadata, new Dictionary<string, string> { { "new_CurrencyPrice_Schema", "1" } }, calculations: new Dictionary<string, string> { { "new_Calc", "new_CurrencyPrice_Schema + 1" } });

                var result = ExecuteSqlTest(exprStr, 3M, cx, new EntityMetadataModel[] { metadata }, false, false, "fn_testUdf1");
                StringMatchIgnoreNewlines(DataverseTests.BaselineFunction, result.SqlFunction, "Baseline SQL has changed");
                Assert.Equal(DataverseTests.BaselineCreateRow, result.SqlCreateRow); // "Baseline create row has changed"
                Assert.Equal(DataverseTests.BaselineLogicalFormula, result.LogicalFormula); // "Baseline logical formula has changed"
            }
        }

        // Whole no is supported in current system so commenting this unit test, once system starts supporting whole no, uncomment this test
        /*
        [SkippableFact]
        public void SqlCalculatedDependencyTest()
        {
            var rawField = "raw";
            var metadata = new EntityMetadataModel
            {
                LogicalName = "foo",
                PrimaryIdAttribute = "fooid",
                Attributes = new AttributeMetadataModel[] {
                    AttributeMetadataModel.NewInteger(rawField, "Raw"),
                    AttributeMetadataModel.NewInteger("calc1", "Calc1").SetCalculated(),
                    AttributeMetadataModel.NewInteger("calc2", "Calc2").SetCalculated(),
                    AttributeMetadataModel.NewGuid("fooid", "FooId")
                }
            };

            using (var cx = GetSql())
            {
                CreateTable(cx, metadata, new Dictionary<string, string> { { rawField, "1" } }, calculations: new Dictionary<string, string> { { "calc1", "" }, { "calc2", "" } });

                var metadataArray = new EntityMetadataModel[] { metadata };
                var calc1 = ExecuteSqlTest("raw + 1", 2, cx, metadataArray, true, false, "udfCalc1", GetIntegerHint());
                var calc2 = ExecuteSqlTest("raw + 2", 3, cx, metadataArray, true, false, "udfCalc2", GetIntegerHint());

                using (var tx = cx.BeginTransaction())
                {
                    var alterCmd = cx.CreateCommand();
                    alterCmd.Transaction = tx;
                    alterCmd.CommandText = $@"ALTER TABLE {metadata.SchemaName}Base ADD [calc1]  AS ([dbo].{calc1.SqlCreateRow})";
                    alterCmd.ExecuteNonQuery();
                    alterCmd.CommandText = $@"ALTER TABLE {metadata.SchemaName}Base ADD [calc2]  AS ([dbo].{calc2.SqlCreateRow})";
                    alterCmd.ExecuteNonQuery();

                    tx.Commit();
                }

                var calc3 = ExecuteSqlTest("calc1 + calc2 + raw", 6, cx, metadataArray, true, false, "udfCalc3", GetIntegerHint());

                using (var tx = cx.BeginTransaction())
                {
                    var alterCmd = cx.CreateCommand();
                    alterCmd.Transaction = tx;
                    alterCmd.CommandText = $@"ALTER TABLE {metadata.SchemaName}Base ADD [calc3]  AS ([dbo].{calc3.SqlCreateRow})";
                    alterCmd.ExecuteNonQuery();
                    tx.Commit();
                }

                var selectCmd = cx.CreateCommand();
                selectCmd.CommandText = $"select {rawField}, calc1, calc2, calc3 from {metadata.SchemaName}Base";
                var reader = selectCmd.ExecuteReader();

                reader.Read();
                var rawValue = reader.GetInt32(0);
                var calc1Value = reader.GetInt32(1);
                var calc2Value = reader.GetInt32(2);
                var calc3Value = reader.GetInt32(3);

                Assert.Equal(1, rawValue); // "Raw Value Mismatch"
                Assert.Equal(2, calc1Value); // "Calc1 Value Mismatch"
                Assert.Equal(3, calc2Value); // "Calc2 Value Mismatch"
                Assert.Equal(6, calc3Value); // "Calc3 Value Mismatch"
            }
        }*/

        [SkippableFact]
        public void FormulaUDFTest()
        {
            using (var cx = GetSql())
            { 
                ExecuteScript(cx, TestCreateTableScript);
                ExecuteScript(cx, "drop view if exists account1;");
                ExecuteScript(cx, TestCreateViewScript);
                ExecuteScript(cx, "drop function if exists [dbo].[fn_testUdf1]");
                ExecuteScript(cx, DataverseTests.BaselineFunction);

                var alterCmd = cx.CreateCommand();
                alterCmd.CommandText = @"SELECT object_id FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[fn_testUdf1]');";
                var actualResult = alterCmd.ExecuteScalar();

                Assert.NotNull(actualResult);
            }
        }

        internal void ExecuteScript(SqlConnection connection, string script)
        {
            using (var tx = connection.BeginTransaction())
            {
                var alterCmd = connection.CreateCommand();
                alterCmd.Transaction = tx;
                alterCmd.CommandText = script;
                alterCmd.ExecuteNonQuery();
                tx.Commit();
            }
        }

        [SkippableFact]
        public void SqlCalculatedReferenceTest()
        {
            using (var cx = GetSql())
            {
                Guid selfrefid;
                using (var tx = cx.BeginTransaction())
                {
                    DropTable(cx, tx, MockModels.LocalModel);
                    DropTable(cx, tx, MockModels.RemoteModel);
                    DropTable(cx, tx, MockModels.DoubleRemoteModel);
                    DropTable(cx, tx, MockModels.TripleRemoteModel);

                    var createCmd = cx.CreateCommand();
                    createCmd.Transaction = tx;
                    createCmd.CommandText = GenerateTableScript(MockModels.LocalModel);
                    createCmd.ExecuteNonQuery();

                    createCmd.CommandText = GenerateTableScript(MockModels.RemoteModel, calculations: new Dictionary<string, string> { { "calc", "data + 1" } });
                    createCmd.ExecuteNonQuery();

                    createCmd.CommandText = GenerateTableScript(MockModels.DoubleRemoteModel);
                    createCmd.ExecuteNonQuery();

                    createCmd.CommandText = GenerateTableScript(MockModels.TripleRemoteModel);
                    createCmd.ExecuteNonQuery();

                    // first create the referenced triple remote row
                    var trid = Guid.NewGuid();
                    InsertRow(cx, tx, MockModels.TripleRemoteModel, new Dictionary<string, string> {
                        { MockModels.TripleRemoteModel.PrimaryIdAttribute, GuidToSql(trid) },
                        { "data3", "1" }
                    });

                    // then create the referenced double remote row
                    var drid = Guid.NewGuid();
                    InsertRow(cx, tx, MockModels.DoubleRemoteModel, new Dictionary<string, string> {
                        { MockModels.DoubleRemoteModel.PrimaryIdAttribute, GuidToSql(drid) },
                        { "otherotherotherid", GuidToSql(trid) },
                        { "data2", "1" }
                    });

                    // then create the referenced remote row
                    var rid = Guid.NewGuid();
                    InsertRow(cx, tx, MockModels.RemoteModel, new Dictionary<string, string> {
                        { MockModels.RemoteModel.PrimaryIdAttribute, GuidToSql(rid) },
                        { "data", "1" },
                        { "otherotherid", GuidToSql(drid) }
                    });

                    // then create the referenced local row
                    var lid = Guid.NewGuid();
                    InsertRow(cx, tx, MockModels.LocalModel, new Dictionary<string, string> {
                        { MockModels.LocalModel.PrimaryIdAttribute, GuidToSql(lid) },
                        { "new_price", "1" }
                    });

                    // then create the row that references both
                    selfrefid = Guid.NewGuid();
                    InsertRow(cx, tx, MockModels.LocalModel, new Dictionary<string, string> {
                        { MockModels.LocalModel.PrimaryIdAttribute, GuidToSql(selfrefid) },
                        { "new_price", "1" },
                        { "otherid", GuidToSql(rid) },
                        { "selfid", GuidToSql(lid) }
                    });

                    tx.Commit();
                }

                var calc = ExecuteSqlTest("Price + Other.'Calculated Data'", 3M, cx, MockModels.RelationshipModels, rowid: selfrefid);
                Assert.Equal("new_price + refg.calc", calc.LogicalFormula);

                calc = ExecuteSqlTest("Price + Other.Data", 2M, cx, MockModels.RelationshipModels, rowid: selfrefid);
                Assert.Equal("new_price + refg.data", calc.LogicalFormula);

                calc = ExecuteSqlTest("Price + 'Self Reference'.Price", 2M, cx, MockModels.RelationshipModels, rowid: selfrefid);
                Assert.Equal("new_price + self.new_price", calc.LogicalFormula);

                calc = ExecuteSqlTest("Other.'Other Other'.'Data Two' + Other.'Other Other'.'Other Other Other'.'Data Three'", 2M, cx, MockModels.RelationshipModels, rowid: selfrefid);
                Assert.Equal("refg.doublerefg.data2 + refg.doublerefg.triplerefg.data3", calc.LogicalFormula);
            }
        }
        private static string GuidToSql(Guid guid)
        {
            return $"'{guid}'";
        }

        [SkippableFact]
        public void SqlStringParameterTest()
        {
            var metadata = new EntityMetadataModel
            {
                LogicalName = "foo",
                PrimaryIdAttribute = "fooid",
                Attributes = new AttributeMetadataModel[] {
                    AttributeMetadataModel.NewString("new_null_param", "Null Param"),
                    AttributeMetadataModel.NewInteger("new_int", "Integer"),
                }
            };

            using (var cx = GetSql())
            {
                CreateTable(cx, metadata, new Dictionary<string, string> { { "new_int", "1" } });

                var metadataArray = new EntityMetadataModel[] { metadata };
                // v0 (return value) is not defaulted to empty string for null string input
                ExecuteSqlTest("If('Null Param' = \"test\",1,2)", 2M, cx, metadataArray);

                // Null string input is not converted to empty string and is considered blank
                ExecuteSqlTest("'Null Param' = \"\"", false, cx, metadataArray);
                ExecuteSqlTest("IsBlank('Null Param')", true, cx, metadataArray);
            }
        }

        [SkippableFact]
        public void SqlOptionSetTest()
        {
            using (var cx = GetSql())
            {
                var metadata = new EntityMetadataModel
                {
                    LogicalName = "this",
                    DisplayCollectionName = "Thises",
                    PrimaryIdAttribute = "thisid",
                    Attributes = new AttributeMetadataModel[] {
                        AttributeMetadataModel.NewPicklist("rating", "Rating", new OptionMetadataModel[]
                        {
                            new OptionMetadataModel { Label = "Hot", Value = 1 },
                            new OptionMetadataModel { Label = "Warm", Value = 2 },
                            new OptionMetadataModel { Label = "Cold", Value = 3 }
                        })
                    }
                };

                CreateTable(cx, metadata, new Dictionary<string, string> { { "rating", "2" } });

                ExecuteSqlTest("Rating = 'Rating (Thises)'.Hot", false, cx, new EntityMetadataModel[] { metadata });
                ExecuteSqlTest("Rating <> 'Rating (Thises)'.Hot", true, cx, new EntityMetadataModel[] { metadata });
            }
        }

        [SkippableFact]
        public void SqlGlobalOptionSetTest()
        {
            List<OptionSetMetadata> globalOptionSets = new List<OptionSetMetadata>();
            var optionSet1 = new OptionSetMetadata(new OptionMetadataCollection(new List<OptionMetadata>(
                new OptionMetadata[]
                {
                    new OptionMetadata { Label = new Label(new LocalizedLabel("One", 1033), new LocalizedLabel[0]), Value = 1 },
                    new OptionMetadata { Label = new Label(new LocalizedLabel("Two", 1033), new LocalizedLabel[0]), Value = 2 },
                }
            )))
            {
                IsGlobal = true,
                Name = "global1",
                DisplayName = new Label(new LocalizedLabel("Global1", 1033), new LocalizedLabel[0])
            };

            globalOptionSets.Add(optionSet1);

            using (var cx = GetSql())
            {
                ExecuteSqlTest("(Global1.One = Global1.Two)", false, cx, null, globalOptionSets: globalOptionSets);
            }
        }

        [SkippableFact]
        public void SqlBooleanTest()
        {
            var metadata = new EntityMetadataModel
            {
                LogicalName = "this",
                DisplayCollectionName = "Thises",
                PrimaryIdAttribute = "thisid",
                Attributes = new AttributeMetadataModel[]
                {
                    AttributeMetadataModel.NewBoolean("available", "Available", "Yes", "No")
                }
            };

            using (var cx = GetSql())
            {
                CreateTable(cx, metadata, new Dictionary<string, string> { { "available", "1" } });

                ExecuteSqlTest("If(Available,1,2)", 1M, cx, new EntityMetadataModel[] { metadata });
                ExecuteSqlTest("Not Available", false, cx, new EntityMetadataModel[] { metadata });
            }
        }

        [SkippableFact]
        public void SqlDateTimeBehaviors()
        {
            var model = new EntityMetadataModel
            {
                LogicalName = "local",
                Attributes = new AttributeMetadataModel[]
                {
                    AttributeMetadataModel.NewDateTime("userLocalDateTime", "UserLocalDateTime", DateTimeBehavior.UserLocal, DateTimeFormat.DateAndTime),
                    AttributeMetadataModel.NewDateTime("userLocalDateOnly", "UserLocalDateOnly", DateTimeBehavior.UserLocal, DateTimeFormat.DateOnly),
                    AttributeMetadataModel.NewDateTime("dateOnly", "DateOnly", DateTimeBehavior.DateOnly, DateTimeFormat.DateOnly),
                    AttributeMetadataModel.NewDateTime("tziDateTime", "TimeZoneIndependentDateTime", DateTimeBehavior.TimeZoneIndependent, DateTimeFormat.DateAndTime),
                    AttributeMetadataModel.NewDateTime("tziDateOnly", "TimeZoneIndependentDateOnly", DateTimeBehavior.TimeZoneIndependent, DateTimeFormat.DateOnly)
                }
            };
            var metadata = new EntityMetadataModel[] { model };
            using (var cx = GetSql())
            {
                CreateTable(cx, model, new Dictionary<string, string>
                {
                    { "userLocalDateTime", "N'2021-07-23 06:11:00.000'" },
                    { "userLocalDateOnly", "N'2021-08-01 07:00:00.000'" },
                    { "dateOnly","N'2021-07-16 00:00:00.000'" },
                    { "tziDateTime", "N'2021-07-23 18:00:00.000'" },
                    { "tziDateOnly", "N'2021-08-26 00:00:00.000'" },
                });

                ExecuteSqlTest("Day(tziDateOnly)", 26M, cx, metadata);
                ExecuteSqlTest("Month(dateOnly)", 7M, cx, metadata);
                ExecuteSqlTest("Hour(tziDateTime)", 18M, cx, metadata);
                ExecuteSqlTest("DateDiff(tziDateOnly, tziDateTime)", -34M, cx, metadata);
                ExecuteSqlTest("Hour(UTCNow())", (decimal)DateTime.UtcNow.Hour, cx, metadata);
                ExecuteSqlTest("Year(UTCToday())", (decimal)DateTime.UtcNow.Year, cx, metadata);
                ExecuteSqlTest("Month(If(true,tziDateOnly,tziDateTime))", 8M, cx, metadata);
                ExecuteSqlTest("Day(IfError(tziDateOnly,tziDateTime))", 26M, cx, metadata);
                ExecuteSqlTest("tziDateTime < tziDateOnly", true, cx, metadata);

                ExecuteSqlTest("WeekNum(tziDateOnly)", 35M, cx, metadata);
                ExecuteSqlTest("WeekNum(dateOnly)", 29M, cx, metadata);

                ExecuteSqlTest("DateDiff(Now(), Now())", 0.0M, cx, metadata);
                ExecuteSqlTest("Now() < Now()", false, cx, metadata);
                ExecuteSqlTest("If(DateDiff(userLocalDateTime,Now()),1,2)", 1.0M, cx, metadata);
                ExecuteSqlTest("userLocalDateTime > Now()", false, cx, metadata);

            }
        }

        [SkippableFact]
        public void SqlNestElseIf()
        {
            using (var cx = GetSql())
            {
                // SQL doesn't support nested else if, and execution would fail without rewriting SQL to support short circuiting
                var result = ExecuteSqlTest("If(1=3,1,If(1=2,2,1=1,3))", 3M, cx, null);

                // Rewriting to have top-level else if also succeeds
                ExecuteSqlTest("If(1=3,1,1=2,2,1=1,3)", 3M, cx, null);

                ExecuteSqlTest("If(1<0, false) & \"b\"", "b", cx, null);
                ExecuteSqlTest("If(1>0, false) & \"b\"", "falseb", cx, null);
                ExecuteSqlTest("If(1>0, \"a\") & \"b\"", "ab", cx, null);
                ExecuteSqlTest("If(1<0, \"a\") & \"b\"", "b", cx, null);
            }
        }

        [SkippableFact]
        public void SqlCoercions()
        {
            var model = new EntityMetadataModel
            {
                LogicalName = "local",
                Attributes = new AttributeMetadataModel[]
                {
                    AttributeMetadataModel.NewDecimal("decimal", "Decimal"),
                    AttributeMetadataModel.NewDecimal("null_decimal", "NullDec"),
                    AttributeMetadataModel.NewString("string", "String"),
                    AttributeMetadataModel.NewString("null_string", "NullStr"),
                    AttributeMetadataModel.NewPicklist("null_picklist", "NullPicklist", new []
                    {
                        new OptionMetadataModel
                        {
                            Label = "A",
                            Value = 1,
                        },
                        new OptionMetadataModel
                        {
                            Label = "B",
                            Value = 2,
                        }
                    }),
                    AttributeMetadataModel.NewBoolean("null_boolean", "NullBoolean", "Yup", "Naw"),
                    AttributeMetadataModel.NewGuid("guid", "Guid")
                }
            };
            var metadata = new EntityMetadataModel[] { model };
            using (var cx = GetSql())
            {
                CreateTable(cx, model, new Dictionary<string, string>
                {
                    { "decimal", "1" },
                    { "string", "N'foo'"},
                    { "guid", "'70278D61-CD79-467E-8E89-AA3FA802EC79'" }
                });

                ExecuteSqlTest("Value(\"123.4\")", null, cx, metadata);
                ExecuteSqlTest("Value(\"123,4\")", null, cx, metadata);
                ExecuteSqlTest("IsError(Value(\"123.4\"))", true, cx, metadata);
                ExecuteSqlTest("IsError(Value(\"123,4\"))", true, cx, metadata);

                // coerce null to 0 or empty string for logical operators that aren't equality
                ExecuteSqlTest("-5 < NullDec", true, cx, metadata);
                ExecuteSqlTest("5 < NullDec", false, cx, metadata);
                ExecuteSqlTest("NullDec < 5", true, cx, metadata);
                ExecuteSqlTest("NullDec < -5", false, cx, metadata);
                ExecuteSqlTest("NullDec %", 0M, cx, metadata);
                ExecuteSqlTest("\"o\" in NullStr", false, cx, metadata);
                ExecuteSqlTest("\"o\" in String", true, cx, metadata);
                // don't coerce null for equality checks
                ExecuteSqlTest("NullDec = 0", false, cx, metadata);
                ExecuteSqlTest("NullDec <> 0", true, cx, metadata);
                ExecuteSqlTest("0 <> NullDec", true, cx, metadata);
                ExecuteSqlTest("Decimal = NullDec", false, cx, metadata);
                ExecuteSqlTest("NullDec <> Decimal", true, cx, metadata);
                ExecuteSqlTest("NullDec <> Blank()", false, cx, metadata);
                ExecuteSqlTest("Blank() = NullDec", true, cx, metadata);
                ExecuteSqlTest("NullDec = Blank()", true, cx, metadata);
                ExecuteSqlTest("Blank() = NullStr", true, cx, metadata); 
                ExecuteSqlTest("Blank() <> NullStr", false, cx, metadata);
                ExecuteSqlTest("Blank() = \"\"", false, cx, metadata);
                ExecuteSqlTest("Blank() <> \"\"", true, cx, metadata);
                ExecuteSqlTest("Blank() = String", false, cx, metadata);
                ExecuteSqlTest("Blank() <> String", true, cx, metadata);
                // coerce null to 0 in math functions
                ExecuteSqlTest("IsError(Mod(NullDec, NullDec))", true, cx, metadata);
                // coerce null to 0 in math operations
                ExecuteSqlTest("IsError(Mod(NullDec, NullDec))", true, cx, metadata);
                ExecuteSqlTest("5 * NullDec", 0M, cx, metadata);
                ExecuteSqlTest("IsError(1/NullDec)", true, cx, metadata);
                // coerce null to empty string in string functions
                ExecuteSqlTest("Upper(NullStr)", "", cx, metadata);
                // don't coerce in IsBlank
                ExecuteSqlTest("IsBlank(NullDec)", true, cx, metadata);
                ExecuteSqlTest("IsBlank(NullStr)", true, cx, metadata);
                ExecuteSqlTest("IsBlank(Decimal)", false, cx, metadata);
                ExecuteSqlTest("IsBlank(String)", false, cx, metadata);
                ExecuteSqlTest("IsBlank(NullPicklist)", true, cx, metadata);
                ExecuteSqlTest("IsBlank(NullBoolean)", true, cx, metadata);
                ExecuteSqlTest("Value(NullStr) = Blank()", true, cx, metadata); 
                ExecuteSqlTest("Text(NullStr) = Blank()", true, cx, metadata);
                ExecuteSqlTest("Upper(NullStr) = \"\"", true, cx, metadata);
                ExecuteSqlTest("Lower(NullStr) = \"\"", true, cx, metadata);
                ExecuteSqlTest("Concatenate(NullStr, \"a\", NullStr, \"b\") = \"ab\"", true, cx, metadata);
                ExecuteSqlTest("Left(NullStr, 2) = \"\"", true, cx, metadata);
                ExecuteSqlTest("Right(NullStr, 2) = \"\"", true, cx, metadata);
                ExecuteSqlTest("Mid(NullStr,1,2) = \"\"", true, cx, metadata);
                ExecuteSqlTest("Mid(String,1,11111111111) = \"\"", null, cx, metadata);
                ExecuteSqlTest("Mid(String,11111111111,1) = \"\"", null, cx, metadata);
                ExecuteSqlTest("Mid(String,1,4) = \"\"", false, cx, metadata);
                ExecuteSqlTest("Len(NullStr) = 0", true, cx, metadata);
                ExecuteSqlTest("TrimEnds(NullStr) = \"\"", true, cx, metadata);
                ExecuteSqlTest("Trim(NullStr) = \"\"", true, cx, metadata);
                ExecuteSqlTest("Replace(\"ab\", 5, 1, NullStr) = \"ab\"", true, cx, metadata);
                ExecuteSqlTest("Substitute(NullStr, NullStr, \"a\") = \"\"", true, cx, metadata);
                ExecuteSqlTest("Concatenate(guid, string)", "70278D61-CD79-467E-8E89-AA3FA802EC79foo", cx, metadata);
                ExecuteSqlTest("guid & string", "70278D61-CD79-467E-8E89-AA3FA802EC79foo", cx, metadata);
                ExecuteSqlTest("Substitute(guid, guid, string, 1)", "foo", cx, metadata);
            }
        }

        [SkippableFact]
        public void SqlNumericFormat()
        {
            var model = new EntityMetadataModel
            {
                LogicalName = "local",
                Attributes = new AttributeMetadataModel[]
                {
                    AttributeMetadataModel.NewDecimal("whole", "WholeDecimal"),
                    AttributeMetadataModel.NewDecimal("fractional", "FractionalDecimal"),
                    AttributeMetadataModel.NewInteger("int", "Integer"),
                    AttributeMetadataModel.NewDecimal("nulldecimal", "NullDecimal")
                }
            };
            var metadata = new EntityMetadataModel[] { model };
            using (var cx = GetSql())
            {
                CreateTable(cx, model, new Dictionary<string, string>
                {
                    { "whole", "30" },
                    { "fractional", "100.5" },
                    { "int", "20"}
                });

                ExecuteSqlTest("Text(Integer, \"###\")", "20", cx, metadata);
                ExecuteSqlTest("Text(WholeDecimal, \"###\")", "30", cx, metadata);
                ExecuteSqlTest("IsError(Text(FractionalDecimal, \"###\"))", false, cx, metadata);
                ExecuteSqlTest("Int(\"30\")", 30M, cx, metadata);
                ExecuteSqlTest("IsError(Int(\"30.5\"))", true, cx, metadata);
                ExecuteSqlTest("Text(FractionalDecimal, \"0000\")", "0101", cx, metadata);
                ExecuteSqlTest("Text(WholeDecimal, \"0000\")", "0030", cx, metadata);

                ExecuteSqlTest("Text(Blank(), \"0\")", "0", cx, metadata);
                ExecuteSqlTest("IsError(Text(Blank(), \"0\"))", false, cx, metadata);
                ExecuteSqlTest("IsBlank(Text(Blank(), \"0\"))", false, cx, metadata);

                ExecuteSqlTest("Text(nulldecimal, \"0\")", "0", cx, metadata);
                ExecuteSqlTest("IsError(Text(nulldecimal, \"0\"))", false, cx, metadata);
                ExecuteSqlTest("IsBlank(Text(nulldecimal, \"0\"))", false, cx, metadata);

                ExecuteSqlTest("IsError(Text(423456789013, \"0\"))", true, cx, metadata); // IsError is true because '423456789013' overflows decimal range (-100000000000, 100000000000)
                ExecuteSqlTest("IsError(IsBlank(Text(423456789013, \"0\")))", true, cx, metadata); // IsError is true because of overflow numeric literal
                ExecuteSqlTest("IsError(IsBlank(423456789013))", true, cx, metadata); // IsError is true because of overflow numeric literal
            }
        }

        [SkippableFact]
        public void SqlOverflows()
        {
            var model = new EntityMetadataModel
            {
                LogicalName = "local",
                Attributes = new AttributeMetadataModel[]
                {
                    AttributeMetadataModel.NewDecimal("decimal", "Decimal"),
                    AttributeMetadataModel.NewDecimal("decimal2", "Decimal2"),
                    AttributeMetadataModel.NewDecimal("decimal3", "Decimal3"),
                    AttributeMetadataModel.NewDecimal("decimal4", "Decimal4"),
                    AttributeMetadataModel.NewDecimal("decimal5", "Decimal5"),
                    AttributeMetadataModel.NewMoney("decimal6", "Decimal6"),
                    AttributeMetadataModel.NewInteger("int", "Integer"),
                    AttributeMetadataModel.NewInteger("int2", "Integer2"),
                    AttributeMetadataModel.NewDecimal("big_decimal", "BigDecimal"),
                    AttributeMetadataModel.NewInteger("big_int", "BigInteger"),
                    AttributeMetadataModel.NewMoney("money1", "Money1"),
                    AttributeMetadataModel.NewMoney("money2", "Money2")
                }
            };
            var metadata = new EntityMetadataModel[] { model };
            using (var cx = GetSql())
            {
                CreateTable(cx, model, new Dictionary<string, string>
                {
                    { "decimal", "19.69658" },
                    { "decimal2", "0.02188" },
                    { "decimal3", "10000000000" },
                    { "decimal4", SqlStatementFormat.DecimalTypeMax },
                    { "decimal5", SqlStatementFormat.DecimalTypeMin },
                    { "decimal6",  "1000000000000"},
                    { "int", "20"},
                    { "int2", "2147483645" },
                    { "big_decimal", SqlStatementFormat.DecimalTypeMax },
                    { "big_int", SqlStatementFormat.IntTypeMax },
                    { "money1", "99999999999999" },
                    { "money2", "9999999999999" }
                });

                // Arithmetic
                ExecuteSqlTest("decimal*decimal2", 0.4309611704M, cx, metadata);
                ExecuteSqlTest("Decimal(decimal2)", 0.02188M, cx, metadata);
                ExecuteSqlTest("RoundUp(decimal2,3)", 0.022M, cx, metadata);
                ExecuteSqlTest("decimal2 + int2", 2147483645.02188M, cx, metadata);
                ExecuteSqlTest("decimal2 * int2", 46986942.1526M, cx, metadata);
                ExecuteSqlTest("int2 / decimal2", 98148247029.2504570384M, cx, metadata);
                ExecuteSqlTest("Text(decimal4, \"0\")", "100000000000", cx, metadata);
                ExecuteSqlTest("IsError(Text(decimal4, \"0\"))", false, cx, metadata);

                // Overflow cases - return null
                ExecuteSqlTest("BigDecimal + 1", null, cx, metadata);
                ExecuteSqlTest("BigInteger * BigDecimal", null, cx, metadata);
                ExecuteSqlTest("BigInteger * BigInteger", null, cx, metadata);
                ExecuteSqlTest("decimal3 * int2", null, cx, metadata);
                ExecuteSqlTest("decimal3 / decimal2", null, cx, metadata);
                ExecuteSqlTest("99999999 * 99999999", null, cx, metadata);
                ExecuteSqlTest("IsError(99999999 * 99999999)", true, cx, metadata);
                ExecuteSqlTest("decimal4 + 1 - 5", null, cx, metadata);
                ExecuteSqlTest("decimal5 - 1", null, cx, metadata);
                ExecuteSqlTest("decimal4 + decimal5 + 2", 2.00M, cx, metadata);
                ExecuteSqlTest("decimal4 + decimal5 + 2 + 99999999999", null, cx, metadata);
                ExecuteSqlTest("Decimal(money2)/int2", null, cx, metadata);
                ExecuteSqlTest("Decimal(money1)/int2", null, cx, metadata); // null as money1 value cannot fit into decimal(23,10)
                ExecuteSqlTest("Sum(Decimal(money2), Decimal(money2))", null, cx, metadata);
                ExecuteSqlTest("100000000000 * 10", null, cx, metadata);
                ExecuteSqlTest("IsError(100000000000 * 10)", true, cx, metadata);
                ExecuteSqlTest("If(IsError(100000000000 * 10), 1, 2)", 1M, cx, metadata);
                ExecuteSqlTest("100000000000 * 100", null, cx, metadata);
                ExecuteSqlTest("IsError(100000000000 * 100)", true, cx, metadata);
                ExecuteSqlTest("999999*999999/9999", null, cx, metadata);
                ExecuteSqlTest("Text(decimal4+1, \"0\")", null, cx, metadata);
                ExecuteSqlTest("IsError(Text(decimal4+1, \"0\"))", true, cx, metadata);
                ExecuteSqlTest("Text(Decimal(decimal6), \"0\")", null, cx, metadata);
                ExecuteSqlTest("IsError(Text(Decimal(decimal6), \"0\"))", true, cx, metadata);
                ExecuteSqlTest("Decimal(decimal6)", null, cx, metadata);
                ExecuteSqlTest("IsError(Decimal(decimal6))", true, cx, metadata);
            }
        }

        public static TypeDetails GetIntegerHint()
        {
            return new TypeDetails
            {
                TypeHint = AttributeTypeCode.Integer,
                Precision = 0
            };
        }

        #region Full Test infra

        private const string ConnectionStringVariable = "FxTestSQLDatabase";

        private static SqlConnection GetSql()
        {
            // "Data Source=tcp:SQL_SERVER;Initial Catalog=test;Integrated Security=True;Persist Security Info=True;";
            var cx = Environment.GetEnvironmentVariable(ConnectionStringVariable);

            if (string.IsNullOrEmpty(cx))
            {
                // Throws
                Skip.If(true, $"Test skipped - No SQL database configured. Set the {ConnectionStringVariable} env var to a connection string.");
            }
            var connection = new SqlConnection(cx);
            connection.Open();
            return connection;
        }

        private static SqlCompileResult ExecuteSqlTest(string formula, object expectedResult, SqlConnection connection, EntityMetadataModel[] metadata, bool commit = false, bool verbose = false, string udfName = null, TypeDetails typeHints = null, bool success = true, Guid? rowid = null, List<OptionSetMetadata> globalOptionSets = null)
        {
            if (metadata == null)
            {
                metadata = new EntityMetadataModel[] { new EntityMetadataModel() };
            }

            var compileResult = CompileToSql(formula, metadata, verbose, udfName, typeHints, globalOptionSets);
            Assert.Equal(success, compileResult.IsSuccess); // $"Compilation failed for formula: '{formula}'"

            if (compileResult.IsSuccess)
            {
                using (var tx = connection.BeginTransaction())
                {
                    if (!string.IsNullOrWhiteSpace(udfName))
                    {
                        var dropCmd = connection.CreateCommand();
                        dropCmd.Transaction = tx;
                        dropCmd.CommandText = $"DROP FUNCTION IF EXISTS [dbo].[{udfName}]";
                        dropCmd.ExecuteNonQuery();
                    }

                    var createCmd = connection.CreateCommand();
                    createCmd.Transaction = tx;
                    createCmd.CommandText = compileResult.SqlFunction;
                    _ = createCmd.ExecuteNonQuery();

                    var executeCmd = connection.CreateCommand();
                    executeCmd.Transaction = tx;
                    var from = compileResult.TopLevelIdentifiers.Count == 0 && !rowid.HasValue ? "" : $"FROM {metadata[0].SchemaName}Base";
                    if (from != "" && rowid.HasValue)
                    {
                        from = $"{from} WHERE {GetPrimaryIdSchemaName(metadata[0])} = '{rowid}'";
                    }
                    executeCmd.CommandText = $"SELECT dbo.{compileResult.SqlCreateRow}{from}";
                    var actualResult = executeCmd.ExecuteScalar();

                    if (actualResult is DBNull)
                    {
                        actualResult = null;
                    }
                    Assert.Equal(expectedResult, actualResult); // $"Incorrect result from '{formula}'"

                    if (verbose)
                    {
                        Debug.WriteLine("Passed");
                    }

                    if (commit)
                    {
                        tx.Commit();
                    }
                }
            }

            return compileResult;
        }

        private static SqlCompileResult CompileToSql(string formula, EntityMetadataModel[] metadata, bool verbose = true, string udfName = null, TypeDetails typeHints = null, List<OptionSetMetadata> globalOptionSets = null)
        {
            var provider = new MockXrmMetadataProvider(metadata);
            var engine = new PowerFx2SqlEngine(
                metadata[0].ToXrm(),
                new CdsEntityMetadataProvider(provider, globalOptionSets: globalOptionSets) { NumberIsFloat = DataverseEngine.NumberIsFloat });

            var options = new SqlCompileOptions
            {
                CreateMode = SqlCompileOptions.Mode.Create,
                UdfName = udfName, // null will auto generate with guid.
                TypeHints = typeHints
            };

            var result = engine.Compile(formula, options);

            if (result.Errors != null)
            {
                Debug.WriteLine($"Compile errors:");
                foreach (var error in result.Errors)
                {
                    Debug.WriteLine($"{error.Span.Min}-{error.Span.Lim}:{error.Message}");
                }
                return result;
            }

            if (verbose)
            {
                Debug.WriteLine($"Compiled this formula: {formula}");

                Debug.WriteLine("Uses these fields:");
                foreach (var fieldName in result.TopLevelIdentifiers)
                {
                    Debug.WriteLine("  " + fieldName);
                }
                Debug.WriteLine("");

                // Write actual function definition

                Debug.WriteLine("SQL function:");
                Debug.WriteLine("---------");
                Debug.WriteLine(result.SqlFunction);
                Debug.WriteLine("");

                Debug.WriteLine("------");
                Debug.WriteLine("For CreateTable command:");
                Debug.WriteLine(result.SqlCreateRow);
            }

            return result;
        }

        internal static void StringMatchIgnoreNewlines(string expected, string actual, string message = null)
        {
            // ignore differences in newlines
            var cleanActual = actual.Trim().Replace("\r\n", "\n");
            var cleanExpected = expected.Trim().Replace("\r\n", "\n");
            Assert.Equal(cleanExpected, cleanActual); // message
        }

        private static string GetPrimaryIdSchemaName(EntityMetadataModel model)
        {
            var primaryAttr = model.Attributes.FirstOrDefault(am => am.LogicalName == model.PrimaryIdAttribute);
            return primaryAttr != default ? primaryAttr.SchemaName : model.PrimaryIdAttribute;
        }

        private static string GenerateTableScript(EntityMetadataModel model, Mode mode = Mode.Create, Dictionary<string, string> calculations = null)
        {
            var op = mode == Mode.Alter ? "ALTER" : "CREATE";
            // use the schema name for the primary id attribute, or the primary id logical name if not found

            var primaryId = GetPrimaryIdSchemaName(model);
            var baseTable = $@"{op} TABLE {model.SchemaName}Base (
[{primaryId}] UNIQUEIDENTIFIER DEFAULT NEWID() PRIMARY KEY";
            foreach (var attr in model.Attributes)
            {
                if (attr.LogicalName == model.PrimaryIdAttribute)
                {
                    continue;
                }

                string type;
                string calc = null;
                var found = calculations?.TryGetValue(attr.LogicalName, out calc);
                if (found.HasValue && found.Value)
                {
                    if (calc == "")
                    {
                        // skip calculated fields with empty string calculations
                        continue;
                    }
                    else
                    {
                        type = $"AS ({calc})";
                    }
                }
                else
                {
                    string sqlType = null;

                    if (attr.AttributeType != null)
                    {
                        AttributeTypeCodeToSqlTypeDictionary.TryGetValue(attr.AttributeType.Value, out sqlType);
                    }

                    var attrType = sqlType ?? SqlVisitor.ToSqlType(attr.AttributeType.Value.FormulaType());
                    type = $"{attrType} NULL";
                }

                baseTable += $@",
[{attr.SchemaName}] {type}";
            }
            baseTable += ")";
            return baseTable;
        }

        private static void CreateTable(SqlConnection cx, EntityMetadataModel metadata, Dictionary<string, string> initializations, Mode mode = Mode.Create, Dictionary<string, string> calculations = null)
        {
            using (var tx = cx.BeginTransaction())
            {
                DropTable(cx, tx, metadata);

                var createCmd = cx.CreateCommand();
                createCmd.Transaction = tx;
                createCmd.CommandText = GenerateTableScript(metadata, mode, calculations);
                createCmd.ExecuteNonQuery();

                InsertRow(cx, tx, metadata, initializations);

                tx.Commit();
            }
        }

        private static void DropTable(SqlConnection cx, SqlTransaction tx, EntityMetadataModel metadata)
        {
            var dropCmd = cx.CreateCommand();
            dropCmd.Transaction = tx;
            dropCmd.CommandText = $"DROP TABLE IF EXISTS {metadata.SchemaName}Base";
            dropCmd.ExecuteNonQuery();
        }

        private static void InsertRow(SqlConnection cx, SqlTransaction tx, EntityMetadataModel metadata, Dictionary<string, string> initializations)
        {
            var insertCmd = cx.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = $"INSERT INTO {metadata.SchemaName}Base ({string.Join(",", initializations.Keys)}) VALUES ({string.Join(",", initializations.Values)})";
            insertCmd.ExecuteNonQuery();
        }

        #endregion

        public const string TestCreateTableScript = @"drop table if exists [dbo].AccountBase1;
drop table if exists [dbo].CustomerBase;
CREATE TABLE [dbo].CustomerBase(
customerId INT NOT NULL,
name VARCHAR NOT NULL,
address VARCHAR NOT NULL,
city VARCHAR NOT NULL,
state VARCHAR NOT NULL,
CONSTRAINT[cndx_PrimaryKey_Account1] PRIMARY KEY CLUSTERED
(
[customerId] ASC
)
);
    CREATE TABLE [dbo].AccountBase1(
    AccountId int,
    new_Calc_Schema varchar(255),
    customerId INT NOT NULL,
 FOREIGN KEY(customerId) REFERENCES CustomerBase(customerId)
);
";

        public const string TestCreateViewScript = @"CREATE VIEW [dbo].ACCOUNT1(
   AccountId, new_Calc_Schema, address1_latitude) with view_metadata as
(select t1.AccountId, t1.new_Calc_Schema,t2.address from[dbo].AccountBase1 t1 join[dbo].CustomerBase t2 on t1.customerId = t2.customerId);";

    }

    
}
