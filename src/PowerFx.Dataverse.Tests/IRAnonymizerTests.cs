// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.PowerFx.Core.App.ErrorContainers;
using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class IRAnonymizerTests
    {
        private readonly ITestOutputHelper _console;

        public IRAnonymizerTests(ITestOutputHelper output)
        {
            _console = output;
        }

        [Theory]
        [Obsolete("Using EnableJoinFunction")]
        [InlineData(
            @"19",
            true,
            @"19:w",
            @"1:w")]
        [InlineData(
            @"-19",
            true,
            @"NegateDecimal:w(19:w)",
            @"NegateDecimal:w(1:w)")]
        [InlineData(
            @"false",
            true,
            @"False:b",
            @"False:b")]
        [InlineData(
            @"true",
            true,
            @"True:b",
            @"True:b")]
        [InlineData(
            @"2+2",
            true,
            @"AddDecimals:w(2:w, 2:w)",
            @"AddDecimals:w(1:w, 1:w)")]
        [InlineData(
            @"123+abc",
            true,
            @"AddDecimals:w(123:w, ResolvedObject('abc:ST_XXXX'))",
            @"AddDecimals:w(1:w, ResolvedObject(abc))")]
        [InlineData(
            @"""abc""",
            true,
            @"""abc"":s",
            @"""xxx"":s")]
        [InlineData(
            @"{x: -17.98E-06}",
            true,
            @"{x: NegateDecimal:w(0.00001798:w)}",
            @"{x: NegateDecimal:w(1:w)}")]
        [InlineData(
            @"{y: DateTime(2025,11,30,14,57,44,0)}",
            true,
            @"{y: DateTime:d(Coalesce:n(Float:n(2025:w), 0:n), Coalesce:n(Float:n(11:w), 0:n), Coalesce:n(Float:n(30:w), 0:n), Coalesce:n(Float:n(14:w), 0:n), Coalesce:n(Float:n(57:w), 0:n), Coalesce:n(Float:n(44:w), 0:n), Coalesce:n(Float:n(0:w), 0:n))}",
            @"{y: DateTime:d(Coalesce:n(Float:n(1:w), 0:n), Coalesce:n(Float:n(1:w), 0:n), Coalesce:n(Float:n(1:w), 0:n), Coalesce:n(Float:n(1:w), 0:n), Coalesce:n(Float:n(1:w), 0:n), Coalesce:n(Float:n(1:w), 0:n), Coalesce:n(Float:n(1:w), 0:n))}")]
        [InlineData(
            @"{z: DateTimeValue(""2025/11/30 14:57:44Z"")}",
            true,
            @"{z: DateTimeValue:d(""2025/11/30 14:57:44Z"":s)}",
            @"{z: DateTimeValue:d(""xxxxxxxxxxxxxxxxxxxx"":s)}")]
        [InlineData(
            @"{t: TimeUnit.Days}",
            true,
            @"{t: FieldAccess(ResolvedObject(Microsoft.PowerFx.Core.Types.Enums.EnumSymbol), Days)}",
            @"{t: FieldAccess(ResolvedObject(TimeUnit), Days)}")]
        [InlineData(
            @"{x: [10, 20], y:30}",
            true,
            @"{x: Table:*[Value:w]({Value: 10:w}, {Value: 20:w})}, {y: 30:w}",
            @"{x: Table:*[Value:w]({Value: 1:w}, {Value: 1:w}), y: 1:w}")]
        [InlineData(
            @"{x: [10, 20], y:30",
            false, // syntax error
            null,  // => no IR is generated
            null)]
        [InlineData(
            @"Filter([@incident], ThisRecord.number = ""INC0000060"")",
            true,
            @"Filter:*[number:s], Scope 1(ResolvedObject('incident:ST_XXXX'), Lazy(EqText:b(ScopeAccess(Scope 1, number), ""INC0000060"":s)))",
            @"Filter:*[number:s](ResolvedObject(incident), Lazy(EqText:b(ScopeAccess(number), ""xxxxxxxxxx"":s)))")]
        [InlineData(
            @"",
            true,
            @"Blank:N()",
            @"Blank:N()")]
        [InlineData(
            @"Power(2, 3)",
            true,
            @"Power:n(Coalesce:n(Float:n(2:w), 0:n), Coalesce:n(Float:n(3:w), 0:n))",
            @"Power:n(Coalesce:n(Float:n(1:w), 0:n), Coalesce:n(Float:n(1:w), 0:n))")]
        [InlineData(
            @"With({ tbl: [ {name:""John"", Age:33}, {name:""Jane"", Age:32} ] }, CountRows(tbl) + First(tbl).Age )",
            true,
            @"With:w, Scope 1({tbl: Table:*[Age:w, name:s]({name: ""John"":s}, {Age: 33:w}, {name: ""Jane"":s}, {Age: 32:w})}, Lazy(AddDecimals:w(CountRows:w(ScopeAccess(Scope 1, tbl)), FieldAccess(First:![Age:w, name:s](ScopeAccess(Scope 1, tbl)), Age))))",
            @"With:w({tbl: Table:*[Age:w, name:s]({name: ""xxxx"":s, Age: 1:w}, {name: ""xxxx"":s, Age: 1:w})}, Lazy(AddDecimals:w(CountRows:w(ScopeAccess(tbl)), FieldAccess(First:![Age:w, name:s](ScopeAccess(tbl)), Age))))")]
        [InlineData(
            @"MadeUpFunction(19)",
            true,
            @"MadeUpFunction:n(Float:n(19:w))",
            @"MadeUpFunction:n(Float:n(1:w))")]
        [InlineData(
            @"ShowColumns(incident, number)",
            true,
            @"ShowColumns:*[number:s], Scope 1(ResolvedObject('incident:ST_XXXX'), ""number"":s)",
            @"ShowColumns:*[number:s](ResolvedObject(incident), ""xxxxxx"":s)")]
        [InlineData(
            @"Collect(Yep, { a: [90], b: ""Hello"" })",
            true,
            @"Collect:![a:*[Value:w], b:s](Lazy(ResolvedObject('Yep:ST_XXXX')), {a: Table:*[Value:w]({Value: 90:w})}, {b: ""Hello"":s})",
            @"Collect:![a:*[Value:w], b:s](Lazy(ResolvedObject(Yep)), {a: Table:*[Value:w]({Value: 1:w}), b: ""xxxxx"":s})")]
        [InlineData(
            @"Set(abc, 10 + 3); Launch(""example.com"", ThisItem.Text)",
            true,
            @"Chained(Set:-(ResolvedObject('abc:ST_XXXX'), AddDecimals:w(10:w, 3:w)),Launch:b(""example.com"":s, FieldAccess(ResolvedObject('ThisItem:ST_XXXX'), Text)))",
            @"Chained(Set:-(ResolvedObject(abc), AddDecimals:w(1:w, 1:w)),Launch:b(""xxxxxxxxxxx"":s, FieldAccess(ResolvedObject(ThisItem), Text)))")]
        [InlineData(
            @"$""Hello {""World""}""",
            true,
            @"Concatenate:s(""Hello "":s, ""World"":s)",
            @"Concatenate:s(""xxxxxx"":s, ""xxxxx"":s)")]
        [InlineData(
            @"$""Hello {-55E18}""",
            true,
            @"Concatenate:s(""Hello "":s, DecimalToText:s(NegateDecimal:w(55000000000000000000:w)))",
            @"Concatenate:s(""xxxxxx"":s, DecimalToText:s(NegateDecimal:w(1:w)))")]
        [InlineData(
            @"ParseJSON(""[{ """"Age"""": 5}]"", Type([{Age: Number}]))",
            true,
            @"ParseJSON:*[Age:n](""[{ """"Age"""": 5}]"":s, ""*[Age:n]"":s)",
            @"ParseJSON:*[Age:n](""xxxxxxxxxxxxx"":s, ""xxxxxxxx"":s)")]
        [InlineData(
            @"Set(x, 1); Set(y, 2); x + y",
            true,
            @"Chained(Set:-(ResolvedObject('x:ST_XXXX'), 1:w),Set:-(ResolvedObject('y:ST_XXXX'), Float:n(2:w)),AddNumbers:n(Float:n(ResolvedObject('x:ST_XXXX')), ResolvedObject('y:ST_XXXX')))",
            @"Chained(Set:-(ResolvedObject(x), 1:w),Set:-(ResolvedObject(y), Float:n(1:w)),AddNumbers:n(Float:n(ResolvedObject(x)), ResolvedObject(y)))")]
        [InlineData(
            @"ForAll([1,2,3], Value * 2)",
            true,
            @"ForAll:*[Value:w], Scope 1(Table:*[Value:w]({Value: 1:w}, {Value: 2:w}, {Value: 3:w}), Lazy(MulDecimals:w(ScopeAccess(Scope 1, Value), 2:w)))",
            @"ForAll:*[Value:w](Table:*[Value:w]({Value: 1:w}, {Value: 1:w}, {Value: 1:w}), Lazy(MulDecimals:w(ScopeAccess(Value), 1:w)))")]
        [InlineData(
            @"Join(Table({a:6}), Table({a:7}), LeftRecord.a = RightRecord.a, JoinType.Inner, RightRecord.a As AAA)",
            true,
            @"Join:*[AAA:w, a:w], Scope 1(Table:*[a:w]({a: 6:w}), Table:*[a:w]({a: 7:w}), Lazy(EqDecimals:b(FieldAccess(ScopeAccess(Scope 1, LeftRecord), a), FieldAccess(ScopeAccess(Scope 1, RightRecord), a))), FieldAccess(ResolvedObject(Microsoft.PowerFx.Core.Types.Enums.EnumSymbol), Inner), {LeftRecord: ""LeftRecord"":s}, {RightRecord: ""RightRecord"":s}, , {a: ""AAA"":s})",
            @"Join:*[AAA:w, a:w](Table:*[a:w]({a: 1:w}), Table:*[a:w]({a: 1:w}), Lazy(EqDecimals:b(FieldAccess(ScopeAccess(LeftRecord), a), FieldAccess(ScopeAccess(RightRecord), a))), FieldAccess(ResolvedObject(JoinType), Inner), {LeftRecord: ""xxxxxxxxxx"":s, RightRecord: ""xxxxxxxxxxx"":s}, {}, {a: ""xxx"":s})")]
        [InlineData(
            @"WeekNum(Date(2020, 12, 8),StartOfWeek.Sunday)",
            true,
            @"WeekNum:w(DateToDateTime:d(Date:D(Coalesce:n(Float:n(2020:w), 0:n), Coalesce:n(Float:n(12:w), 0:n), Coalesce:n(Float:n(8:w), 0:n))), FieldAccess(ResolvedObject(Microsoft.PowerFx.Core.Types.Enums.EnumSymbol), Sunday))",
            @"WeekNum:w(DateToDateTime:d(Date:D(Coalesce:n(Float:n(1:w), 0:n), Coalesce:n(Float:n(1:w), 0:n), Coalesce:n(Float:n(1:w), 0:n))), FieldAccess(ResolvedObject(StartOfWeek), Sunday))")]
        [InlineData(
            @"Error(""Foo"")",
            true,
            @"Error:N(""Foo"":s)",
            @"Error:N(""xxx"":s)")]
        public void TestIRAnonyzer(string expr, bool success, string expectedIr, string expectedAnonymousIr)
        {
            PowerFxConfig config = new PowerFxConfig(Features.PowerFxV1);
            config.AddFunction(new MadeUpFunction("MadeUpFunction", FormulaType.Number, new FormulaType[] { FormulaType.Number }));
            config.AddFunction(new SubTexlFunction("Launch", DType.Boolean, 1, int.MaxValue));
            config.SymbolTable.EnableMutationFunctions();
            config.EnableJsonFunctions();
            config.EnableJoinFunction();

            Engine engine = new Engine(config);

            SymbolTable symbolTable = new SymbolTable() { DebugName = "ST" };
            symbolTable.AddVariable("incident", RecordType.Empty().Add("number", FormulaType.String).ToTable());
            symbolTable.AddVariable("Yep", RecordType.Empty().Add("a", RecordType.Empty().Add("Value", FormulaType.Decimal).ToTable()).Add("b", FormulaType.String).ToTable(), true);
            symbolTable.AddVariable("ThisItem", RecordType.Empty().Add("Text", FormulaType.String));
            symbolTable.AddVariable("abc", FormulaType.Decimal, true);
            symbolTable.AddVariable("x", FormulaType.Decimal, true);
            symbolTable.AddVariable("y", FormulaType.Number, true);

            CheckResult check = engine.Check(expr, new ParserOptions() { AllowsSideEffects = true }, symbolTable);
            Assert.True(success == check.IsSuccess, string.Join(", ", check.Errors.Select(er => $"[{er.Span.Min}-{er.Span.Lim} '{GetPart(expr, er.Span)}'] {er.Message}")));

            if (success)
            {
                _ = check.ApplyIR();

                string nonAnonymousIr = Regex.Replace(check.PrintIR(), "ST_[0-9]+", "ST_XXXX");
                _console.WriteLine(nonAnonymousIr);
                Assert.Equal<object>(expectedIr, nonAnonymousIr);

                string anonymousIR = Regex.Replace(check.GetAnonymousIR(), "ST_[0-9]+", "ST_XXXX");
                _console.WriteLine(anonymousIR);
                Assert.Equal<object>(expectedAnonymousIr, anonymousIR);
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => check.ApplyIR());
                Assert.Throws<InvalidOperationException>(() => check.PrintIR());

                Assert.Null(check.GetAnonymousIR());
            }
        }

        private string GetPart(string expr, Span span) => expr.Substring(span.Min, span.Lim - span.Min);

        private class MadeUpFunction : ReflectionFunction
        {
            internal MadeUpFunction(string name, FormulaType returnType, params FormulaType[] paramTypes)
                : base(name, returnType, paramTypes)
            {
            }

            public NumberValue Execute(NumberValue x)
            {
                var val = x.Value;
                return FormulaValue.New(val * 2);
            }
        }

        private class SubTexlFunction : TexlFunction
        {
            private readonly DType _returnType;

            private readonly bool _isSelfContained;

            internal SubTexlFunction(string functionName, DType returnType, int arityMin, int arityMax, FunctionCategories functionCategories = FunctionCategories.Behavior, bool isSelfContained = false)
                : base(DPath.Root, functionName, functionName, TexlStrings.AboutSet, functionCategories, returnType, 0, arityMin, arityMax)
            {
                _returnType = returnType;
                _isSelfContained = isSelfContained;
            }

            public override bool IsSelfContained => _isSelfContained;

            public override IEnumerable<TexlStrings.StringGetter[]> GetSignatures()
            {
                yield return new[] { TexlStrings.SetArg1 };
            }

            public override bool CheckTypes(CheckTypesContext context, TexlNode[] args, DType[] argTypes, IErrorContainer errors, out DType returnType, out Dictionary<TexlNode, DType> nodeToCoercedTypeMap)
            {
                nodeToCoercedTypeMap = null;
                returnType = _returnType;

                return true;
            }
        }
    }
}
