using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Types;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.Functions
{
    public class AsTypeFunctionTests
    {
        [Theory]
        [InlineData("Collect(t1, {PolymorphicLookup: First(t2)}); AsType(Last(t1).PolymorphicLookup, t2)", false)]
        [InlineData("Collect(t1, {PolymorphicLookup: First(t2)}); AsType(Last(t1).PolymorphicLookup, t1)", true)]
        [InlineData("Collect(t1, {PolymorphicLookup: First(t2)}); AsType(Last(t1).PolymorphicLookup, t3)", true)]

        // Blank handling.
        [InlineData("AsType(LookUp(t1, false).PolymorphicLookup, t2)", null)]
        [InlineData("AsType(LookUp(t1, false).PolymorphicLookup, t3)", null)]
        public async Task AsTypeFunctionAsync(string expr, bool? isErrorValue)
        {
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            map.Add("remote", "t2");
            map.Add("virtualremote", "t3");

            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, _) = PluginExecutionTests.CreateMemoryForRelationshipModels(policy: policy);

            var engine = new RecalcEngine();
            engine.EnableDelegation();
            engine.Config.SymbolTable.EnableMutationFunctions();

            var opts = PluginExecutionTests._parserAllowSideEffects;

            var check = engine.Check(expr, options: opts, symbolTable: dv.Symbols);
            Assert.True(check.IsSuccess);

            var scan = check.ScanDependencies(dv.MetadataCache);

            var run = check.GetEvaluator();
            var result = await run.EvalAsync(CancellationToken.None, dv.SymbolValues);

            if(isErrorValue.HasValue && isErrorValue.Value)
            {
                Assert.IsAssignableFrom<ErrorValue>(result);
            }
            else if(isErrorValue.HasValue && !isErrorValue.Value)
            {
                var resultRecord = Assert.IsAssignableFrom<RecordValue>(result);
                resultRecord.TryGetPrimaryKey(out var key);
                Assert.Equal(PluginExecutionTests._g2.ToString("D"), key);
            }
            else
            {
                Assert.IsAssignableFrom<BlankValue>(result);
            }
        }
    }
}
