//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using static Microsoft.PowerFx.Dataverse.SqlCompileOptions;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    [TestClass]
    public class FullTests
    {
        [TestMethod]
        public void SqlCompileBaselineTest()
        {
            using (var cx = GetSql())
            {
                var exprStr = DataverseTests.BaselineFormula;
                var metadata = DataverseTests.BaselineMetadata;

                CreateTable(cx, metadata, new Dictionary<string, string> { { "new_CurrencyPrice_Schema", "1" } }, calculations: new Dictionary<string, string> { { "new_Calc", "new_CurrencyPrice_Schema + 1" } });

                var result = ExecuteSqlTest(exprStr, 3M, cx, new EntityMetadataModel[] { metadata }, false, false, "fn_testUdf1");
                StringMatchIgnoreNewlines(DataverseTests.BaselineFunction, result.SqlFunction, "Baseline SQL has changed");
                Assert.AreEqual(DataverseTests.BaselineCreateRow, result.SqlCreateRow, "Baseline create row has changed");
                Assert.AreEqual(DataverseTests.BaselineLogicalFormula, result.LogicalFormula, "Baseline logical formula has changed");
            }
        }

        [TestMethod]
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
                var calc1 = ExecuteSqlTest("raw + 1", 2, cx, metadataArray, true, false, "udfCalc1", AttributeMetadataModel.GetIntegerHint());
                var calc2 = ExecuteSqlTest("raw + 2", 3, cx, metadataArray, true, false, "udfCalc2", AttributeMetadataModel.GetIntegerHint());

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

                var calc3 = ExecuteSqlTest("calc1 + calc2 + raw", 6, cx, metadataArray, true, false, "udfCalc3", AttributeMetadataModel.GetIntegerHint());

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

                Assert.AreEqual(1, rawValue, "Raw Value Mismatch");
                Assert.AreEqual(2, calc1Value, "Calc1 Value Mismatch");
                Assert.AreEqual(3, calc2Value, "Calc2 Value Mismatch");
                Assert.AreEqual(6, calc3Value, "Calc3 Value Mismatch");;
            }
        }

        [TestMethod]
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

                Assert.IsNotNull(actualResult);
            }
        }

        public void ExecuteScript(SqlConnection connection, String script)
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

        [TestMethod]
        public void SqlCalculatedReferenceTest()
        {
            using (var cx = GetSql())
            {
                Guid selfrefid;
                using (var tx = cx.BeginTransaction())
                {
                    DropTable(cx, tx, DataverseTests.LocalModel);
                    DropTable(cx, tx, DataverseTests.RemoteModel);
                    DropTable(cx, tx, DataverseTests.DoubleRemoteModel);
                    DropTable(cx, tx, DataverseTests.TripleRemoteModel);

                    var createCmd = cx.CreateCommand();
                    createCmd.Transaction = tx;
                    createCmd.CommandText = GenerateTableScript(DataverseTests.LocalModel);
                    createCmd.ExecuteNonQuery();

                    createCmd.CommandText = GenerateTableScript(DataverseTests.RemoteModel, calculations: new Dictionary<string, string> { { "calc", "data + 1" } });
                    createCmd.ExecuteNonQuery();

                    createCmd.CommandText = GenerateTableScript(DataverseTests.DoubleRemoteModel);
                    createCmd.ExecuteNonQuery();

                    createCmd.CommandText = GenerateTableScript(DataverseTests.TripleRemoteModel);
                    createCmd.ExecuteNonQuery();

                    // first create the referenced triple remote row
                    var trid = Guid.NewGuid();
                    InsertRow(cx, tx, DataverseTests.TripleRemoteModel, new Dictionary<string, string> {
                        { DataverseTests.TripleRemoteModel.PrimaryIdAttribute, GuidToSql(trid) },
                        { "data3", "1" }
                    });

                    // then create the referenced double remote row
                    var drid = Guid.NewGuid();
                    InsertRow(cx, tx, DataverseTests.DoubleRemoteModel, new Dictionary<string, string> {
                        { DataverseTests.DoubleRemoteModel.PrimaryIdAttribute, GuidToSql(drid) },
                        { "otherotherotherid", GuidToSql(trid) },
                        { "data2", "1" }
                    });

                    // then create the referenced remote row
                    var rid = Guid.NewGuid();
                    InsertRow(cx, tx, DataverseTests.RemoteModel, new Dictionary<string, string> {
                        { DataverseTests.RemoteModel.PrimaryIdAttribute, GuidToSql(rid) },
                        { "data", "1" },
                        { "otherotherid", GuidToSql(drid) }
                    });

                    // then create the referenced local row
                    var lid = Guid.NewGuid();
                    InsertRow(cx, tx, DataverseTests.LocalModel, new Dictionary<string, string> {
                        { DataverseTests.LocalModel.PrimaryIdAttribute, GuidToSql(lid) },
                        { "new_price", "1" }
                    });

                    // then create the row that references both
                    selfrefid = Guid.NewGuid();
                    InsertRow(cx, tx, DataverseTests.LocalModel, new Dictionary<string, string> {
                        { DataverseTests.LocalModel.PrimaryIdAttribute, GuidToSql(selfrefid) },
                        { "new_price", "1" },
                        { "otherid", GuidToSql(rid) },
                        { "selfid", GuidToSql(lid) }
                    });

                    tx.Commit();
                }

                var calc = ExecuteSqlTest("Price + Other.'Calculated Data'", 3M, cx, DataverseTests.RelationshipModels, rowid: selfrefid);
                Assert.AreEqual("new_price + refg.calc", calc.LogicalFormula);

                calc = ExecuteSqlTest("Price + Other.Data", 2M, cx, DataverseTests.RelationshipModels, rowid: selfrefid);
                Assert.AreEqual("new_price + refg.data", calc.LogicalFormula);

                calc = ExecuteSqlTest("Price + 'Self Reference'.Price", 2M, cx, DataverseTests.RelationshipModels, rowid: selfrefid);
                Assert.AreEqual("new_price + self.new_price", calc.LogicalFormula);

                calc = ExecuteSqlTest("Other.'Other Other'.'Data Two' + Other.'Other Other'.'Other Other Other'.'Data Three'", 2M, cx, DataverseTests.RelationshipModels, rowid: selfrefid);
                Assert.AreEqual("refg.doublerefg.data2 + refg.doublerefg.triplerefg.data3", calc.LogicalFormula);
            }
        }
        private static string GuidToSql(Guid guid)
        {
            return $"'{guid}'";
        }

        [TestMethod]
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

        [TestMethod]
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

        [TestMethod]
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

        [TestMethod]
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

        [TestMethod]
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

        [TestMethod]
        public void SqlNestElseIf()
        {
            using (var cx = GetSql())
            {
                // SQL doesn't support nested else if, and execution would fail without rewriting SQL to support short circuiting
                var result = ExecuteSqlTest("If(1=3,1,If(1=2,2,1=1,3))", 3M, cx, null);

                // Rewriting to have top-level else if also succeeds
                ExecuteSqlTest("If(1=3,1,1=2,2,1=1,3)", 3M, cx, null);
            }
        }

        [TestMethod]
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
                    AttributeMetadataModel.NewBoolean("null_boolean", "NullBoolean", "Yup", "Naw")
                }
            };
            var metadata = new EntityMetadataModel[] { model };
            using (var cx = GetSql())
            {
                CreateTable(cx, model, new Dictionary<string, string>
                {
                    { "decimal", "1" },
                    { "string", "N'foo'"}
                });

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
                ExecuteSqlTest("Text(NullStr) = \"\"", true, cx, metadata);
                ExecuteSqlTest("Upper(NullStr) = \"\"", true, cx, metadata);
                ExecuteSqlTest("Lower(NullStr) = \"\"", true, cx, metadata);
                ExecuteSqlTest("Concatenate(NullStr, \"a\", NullStr, \"b\") = \"ab\"", true, cx, metadata);
                ExecuteSqlTest("Left(NullStr, 2) = \"\"", true, cx, metadata);
                ExecuteSqlTest("Right(NullStr, 2) = \"\"", true, cx, metadata);
                ExecuteSqlTest("Mid(NullStr,1,2) = \"\"", true, cx, metadata);
                ExecuteSqlTest("Len(NullStr) = 0", true, cx, metadata);
                ExecuteSqlTest("TrimEnds(NullStr) = \"\"", true, cx, metadata);
                ExecuteSqlTest("Trim(NullStr) = \"\"", true, cx, metadata);
                ExecuteSqlTest("Replace(\"ab\", 5, 1, NullStr) = \"ab\"", true, cx, metadata);
                ExecuteSqlTest("Substitute(NullStr, NullStr, \"a\") = \"\"", true, cx, metadata);
            }
        }

        [TestMethod]
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
            }
        }

        [TestMethod]
        public void SqlOverflows()
        {
            var model = new EntityMetadataModel
            {
                LogicalName = "local",
                Attributes = new AttributeMetadataModel[]
                {
                    AttributeMetadataModel.NewDecimal("decimal", "Decimal"),
                    AttributeMetadataModel.NewInteger("int", "Integer"),
                    AttributeMetadataModel.NewDecimal("big_decimal", "BigDecimal"),
                    AttributeMetadataModel.NewInteger("big_int", "BigInteger"),
                }
            };
            var metadata = new EntityMetadataModel[] { model };
            using (var cx = GetSql())
            {
                CreateTable(cx, model, new Dictionary<string, string>
                {
                    { "decimal", "20" },
                    { "int", "20"},
                    { "big_decimal", SqlStatementFormat.DecimalTypeMax },
                    { "big_int", SqlStatementFormat.IntTypeMax }
                });

                // Arithmatic
                ExecuteSqlTest("BigDecimal + 1", null, cx, metadata);
                ExecuteSqlTest("BigInteger * BigDecimal", null, cx, metadata);
                ExecuteSqlTest("BigInteger * BigInteger", null, cx, metadata);
            }
        }

        #region Full Test infra

        const string ConnectionStringVariable = "FxTestSQLDatabase";

        private static SqlConnection GetSql()
        {
            // "Data Source=tcp:SQL_SERVER;Initial Catalog=test;Integrated Security=True;Persist Security Info=True;";
            var cx = Environment.GetEnvironmentVariable(ConnectionStringVariable);

            if (string.IsNullOrEmpty(cx))
            {
                // Throws
                Assert.Inconclusive($"Test skipped - No SQL database configured. Set the {ConnectionStringVariable} env var to a connection string.");
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
            Assert.AreEqual(success, compileResult.IsSuccess, $"Compilation failed for formula: '{formula}'");

            if (compileResult.IsSuccess)
            {
                using (var tx = connection.BeginTransaction())
                {
                    if (!String.IsNullOrWhiteSpace(udfName))
                    {
                        var dropCmd = connection.CreateCommand();
                        dropCmd.Transaction = tx;
                        dropCmd.CommandText = $"DROP FUNCTION IF EXISTS [dbo].[{udfName}]";
                        dropCmd.ExecuteNonQuery();
                    }

                    var createCmd = connection.CreateCommand();
                    createCmd.Transaction = tx;
                    createCmd.CommandText = compileResult.SqlFunction;
                    var rows = createCmd.ExecuteNonQuery();

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
                    Assert.AreEqual(expectedResult, actualResult, $"Incorrect result from '{formula}'");

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
            // This NumberIsFloat should be removed once the SQL compiler natively supports Decimal
            // Tracked with https://github.com/microsoft/Power-Fx-Dataverse/issues/117
            var provider = new MockXrmMetadataProvider(metadata);
            var engine = new PowerFx2SqlEngine(
                metadata[0].ToXrm(),
                new CdsEntityMetadataProvider(provider, globalOptionSets: globalOptionSets) { NumberIsFloat = true });

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

        public static void StringMatchIgnoreNewlines(string expected, string actual, string message = null)
        {
            // ignore differences in newlines
            var cleanActual = actual.Trim().Replace("\r\n", "\n");
            var cleanExpected = expected.Trim().Replace("\r\n", "\n");
            Assert.AreEqual(cleanExpected, cleanActual, message);
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
                    continue;
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
                    type = $"{SqlVisitor.ToSqlType(attr.AttributeType.Value.FormulaType())} NULL";
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
            insertCmd.CommandText = $"INSERT INTO {metadata.SchemaName}Base ({String.Join(",", initializations.Keys)}) VALUES ({String.Join(",", initializations.Values)})";
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

        public const string BaselineFunction = @"CREATE FUNCTION fn_testUdf1(
    @v0 decimal(23,10), -- new_CurrencyPrice
    @v2 uniqueidentifier -- accountid
) RETURNS decimal(23,10)
AS BEGIN
    DECLARE @v1 decimal(23,10)
    DECLARE @v4 decimal(23,10)
    DECLARE @v3 decimal(38,10)
    DECLARE @v5 decimal(38,10)
    SELECT TOP(1) @v1 = [new_Calc_Schema] FROM [dbo].[AccountBase1] WHERE[AccountId] = @v2
    SELECT TOP(1) @v4 = [address1_latitude] FROM [dbo].[Account1] WHERE[AccountId] = @v2

    -- expression body
    SET @v3 = (CAST(ISNULL(@v0,0) AS decimal(23,10)) + CAST(ISNULL(@v1,0) AS decimal(23,10)))
    IF(@v3<-100000000000 OR @v3>100000000000) BEGIN RETURN NULL END
    SET @v5 = (CAST(ISNULL(@v3,0) AS decimal(23,10)) + CAST(ISNULL(@v4,0) AS decimal(23,10)))
    -- end expression body

    IF(@v5<-100000000000 OR @v5>100000000000) BEGIN RETURN NULL END
    RETURN ROUND(@v5, 10)
END
";

    }

    
}
