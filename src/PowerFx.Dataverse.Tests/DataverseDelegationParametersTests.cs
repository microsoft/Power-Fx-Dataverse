// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Functions.Delegation;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Types;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class DataverseDelegationParametersTests
    {
        [Fact]
        public void DelegationParam_NullFilter()
        {
            DataverseDelegationParameters ddp = new DataverseDelegationParameters(FormulaType.String);

            string filter = ddp.GetOdataFilter();
            Assert.Null(filter);
        }

        [Fact]
        public void DelegationParam_NoFilter()
        {
            DataverseDelegationParameters ddp = new DataverseDelegationParameters(FormulaType.String)
            {
                FxFilter = new FxFilterExpression()
            };

            string filter = ddp.GetOdataFilter();
            Assert.Null(filter);
        }

        [Fact]
        public void DelegationParam_NoAndFilter()
        {
            DataverseDelegationParameters ddp = new DataverseDelegationParameters(FormulaType.String)
            {
                FxFilter = new FxFilterExpression(FxFilterOperator.And)
            };

            string filter = ddp.GetOdataFilter();
            Assert.Null(filter);
        }

        [Fact]
        public void DelegationParam_NoOrFilter()
        {
            DataverseDelegationParameters ddp = new DataverseDelegationParameters(FormulaType.String)
            {
                FxFilter = new FxFilterExpression(FxFilterOperator.Or)
            };

            string filter = ddp.GetOdataFilter();
            Assert.Null(filter);
        }

        [Fact]
        public void DelegationParam_Filter1()
        {
            var fxFilter = new FxFilterExpression(FxFilterOperator.And);
            fxFilter.AddCondition("a", FxConditionOperator.Equal, 17m);            

            DataverseDelegationParameters ddp = new DataverseDelegationParameters(FormulaType.String) { FxFilter = fxFilter };

            string filter = ddp.GetOdataFilter();
            Assert.Equal<object>("a eq 17", filter);
        }

        [Fact]
        public void DelegationParam_Filter2()
        {
            var fxFilter = new FxFilterExpression(FxFilterOperator.And);
            fxFilter.AddCondition("a", FxConditionOperator.Equal, 17m);
            fxFilter.AddCondition("b", FxConditionOperator.Equal, "xyz");

            DataverseDelegationParameters ddp = new DataverseDelegationParameters(FormulaType.String) { FxFilter = fxFilter };

            string filter = ddp.GetOdataFilter();
            Assert.Equal<object>("(a eq 17 and b eq 'xyz')", filter);
        }

        [Fact]
        public void DelegationParam_Filter3()
        {
            var fxFilter = new FxFilterExpression(FxFilterOperator.And);
            fxFilter.AddCondition("a", FxConditionOperator.Equal, 17m);
            fxFilter.AddCondition("b", FxConditionOperator.Equal, "xyz");
            fxFilter.AddCondition("c", FxConditionOperator.LessEqual, new DateTime(2025, 4, 22, 17, 59, 3, DateTimeKind.Unspecified));

            DataverseDelegationParameters ddp = new DataverseDelegationParameters(FormulaType.String) { FxFilter = fxFilter };

            string filter = ddp.GetOdataFilter();
            Assert.Equal<object>("(a eq 17 and b eq 'xyz' and c le 2025-04-22T17:59:03.000Z)", filter);
        }

        [Fact]
        public void DelegationParam_2Filters()
        {
            var fxFilter1 = new FxFilterExpression(FxFilterOperator.And);
            fxFilter1.AddCondition("a", FxConditionOperator.Equal, 17m);

            var fxFilter2 = new FxFilterExpression(FxFilterOperator.And);
            fxFilter2.AddCondition("b", FxConditionOperator.GreaterThan, -4m);
            fxFilter2.AddCondition("c", FxConditionOperator.BeginsWith, "ax");            

            var fxFilter = new FxFilterExpression(FxFilterOperator.Or);
            fxFilter.AddFilter(fxFilter1);
            fxFilter.AddFilter(fxFilter2);

            DataverseDelegationParameters ddp = new DataverseDelegationParameters(FormulaType.String) { FxFilter = fxFilter };            

            string filter = ddp.GetOdataFilter();
            Assert.Equal<object>("(a eq 17 or (b gt -4 and startswith(c,'ax')))", filter);
        }
    }
}
