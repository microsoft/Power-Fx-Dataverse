using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Types;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class ApiDelegationTests
    {
        // Delegation using direct API. 
        [Theory]
        [InlineData(
            "Filter(t1, Price < 120 And 90 < Price)",
            "((Price le 120) and (Price ge 90))",
            1000, // default fetch size
            "Table({Price:100,opt:Blank()})")]
        [InlineData(
            "First(t1).Price",
            null,
            1,
            "100")]
        [InlineData(
            "Filter(t1, ThisRecord.opt = Opt.display2)",
            "(opt eq 'logical2')",
            1000, // default fetch size
            "Table({Price:100,opt:Blank()})")]

        public async Task TestDirectApi(string expr, string odataFilter, int top, string expectedStr)
        {
            var dnp = DisplayNameUtility.MakeUnique(new Dictionary<string, string>()
            {
                { "logical1", "display1" },
                { "logical2", "display2" }
            });
            var optionSet = new OptionSet("Opt", dnp);

            var recordType = RecordType.Empty()
                .Add("Price", FormulaType.Number)
                .Add("opt", optionSet.FormulaType);

            var recordValue = FormulaValue.NewRecordFromFields(recordType, new NamedValue[]
            {
                new NamedValue("Price", FormulaValue.New(100f))
            });

            var t1 = new MyTable(recordType);

            var st = new SymbolValues("Delegable_1");
            st.Add("t1", t1);

            Assert.Equal("Delegable_1", st.SymbolTable.DebugName);

            var config = new PowerFxConfig();
            config.AddOptionSet(optionSet);
            var engine = new RecalcEngine(config);
            engine.EnableDelegation();

            var check = new CheckResult(engine)
                .SetText(expr)
                .SetBindingInfo(st.SymbolTable);

            var errors = check.ApplyErrors().ToArray();

            var ir = check.GetCompactIRString();

            var eval = check.GetEvaluator();

            var rc = new RuntimeConfig(st);
            var myService = new MyService()
                .SetResult(recordValue); // all queries expect just 1 row returned. 
            
            rc.AddService(myService);

            var result = await eval.EvalAsync(CancellationToken.None, rc);

            string actualODataFilter = myService._parameters.GetOdataFilter();
            Assert.Equal(odataFilter, actualODataFilter);
            Assert.Equal(top, myService._parameters.Top);

            var sb = new StringBuilder();
            result.ToExpression(sb, new FormulaValueSerializerSettings {  UseCompactRepresentation = true});
            var resultStr = sb.ToString();
            Assert.Equal(expectedStr, resultStr);

            Assert.NotNull(myService._parameters);            
        }
    }

    // Test that we can pass services through the IServiceProvider to the table. 
    // also used for test infra.
    internal class MyService
    {
        public IReadOnlyCollection<DValue<RecordValue>> _result;

        public MyService SetResult(RecordValue row1)
        {
            _result = new DValue<RecordValue>[] { DValue<RecordValue>.Of(row1) };
            return this;
        }

        public DelegationParameters _parameters;
    }

    // An example class that implements IDelegatingTableValue
    public class MyTable : TableValue, IDelegatableTableValue
    {
        public MyTable(RecordType recordType) : base(recordType)
        {
        }

        // should never get called since we're delegating 
        public override IEnumerable<DValue<RecordValue>> Rows => throw new NotImplementedException();

        public async Task<IReadOnlyCollection<DValue<RecordValue>>> GetRowsAsync(IServiceProvider services, DelegationParameters parameters, CancellationToken cancel)
        {
            // Esnure service was plumbed through...
            var myService = (MyService) services.GetService(typeof(MyService));
            Assert.NotNull(myService);

            myService._parameters = parameters;

            return myService._result;
        }

        public override string ToString()
        {
            return "MyTable";
        }
    }
}
