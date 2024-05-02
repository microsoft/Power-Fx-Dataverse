//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-------------------------

using System;
using System.Collections.Generic;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Utils;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class PolicyTests
    {
        [Fact]
        public void TestPolicySimple()
        {
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");

            var policy = new SingleOrgPolicy(map);

            Assert.NotNull(policy);
            Assert.NotNull(policy.AllTables);
        }

        [Fact]
        public void TestPolicy2()
        {
            var map = new MyDisplayNameProvider();
            map.AddField(new DName("a"), new DName("b"));
            map.AddField(new DName("b"), new DName("a"));

            var policy = new SingleOrgPolicy(map);

            Assert.NotNull(policy);
            Assert.NotNull(policy.AllTables);

            Assert.Equal("b (a)", policy.AllTables.TryGetDisplayName(new DName("a"), out var d1) ? d1.Value : "noValue");
            Assert.Equal("a (b)", policy.AllTables.TryGetDisplayName(new DName("b"), out var d2) ? d2.Value : "noValue");
        }

        [Fact]
        public void TestPolicy3()
        {
            var map = new MyDisplayNameProvider();
            map.AddField(new DName("a"), new DName("b"));
            map.AddField(new DName("b"), new DName("a1"));

            var policy = new SingleOrgPolicy(map);

            Assert.NotNull(policy);
            Assert.NotNull(policy.AllTables);

            Assert.Equal("b (a)", policy.AllTables.TryGetDisplayName(new DName("a"), out var d1) ? d1.Value : "noValue");
            Assert.Equal("a1", policy.AllTables.TryGetDisplayName(new DName("b"), out var d2) ? d2.Value : "noValue");
        }


        [Fact]
        public void TestPolicy4()
        {
            var map = new MyDisplayNameProvider();
            map.AddField(new DName("a"), new DName("x"));
            map.AddField(new DName("b"), new DName("x"));

            var policy = new SingleOrgPolicy(map);

            Assert.NotNull(policy);
            Assert.NotNull(policy.AllTables);

            Assert.Equal("x (a)", policy.AllTables.TryGetDisplayName(new DName("a"), out var d1) ? d1.Value : "noValue");
            Assert.Equal("x (b)", policy.AllTables.TryGetDisplayName(new DName("b"), out var d2) ? d2.Value : "noValue");
        }

        [Fact]
        public void TestPolicy5()
        {
            var map = new MyDisplayNameProvider();
            map.AddField(new DName("a"), new DName("a"));
            map.AddField(new DName("c"), new DName("b"));
            map.AddField(new DName("b"), new DName("d"));
            map.AddField(new DName("d"), new DName("c"));

            var policy = new SingleOrgPolicy(map);

            Assert.NotNull(policy);
            Assert.NotNull(policy.AllTables);

            Assert.Equal("a", policy.AllTables.TryGetDisplayName(new DName("a"), out var d1) ? d1.Value : "noValue");
            Assert.Equal("d (b)", policy.AllTables.TryGetDisplayName(new DName("b"), out var d2) ? d2.Value : "noValue");
            Assert.Equal("b (c)", policy.AllTables.TryGetDisplayName(new DName("c"), out var d3) ? d3.Value : "noValue");
            Assert.Equal("c (d)", policy.AllTables.TryGetDisplayName(new DName("d"), out var d4) ? d4.Value : "noValue");
            Assert.Equal("noValue", policy.AllTables.TryGetDisplayName(new DName("e"), out var d5) ? d5.Value : "noValue");
        }
    }

    // Very basic display name provider which allows name conflicts.
    internal class MyDisplayNameProvider : DisplayNameProvider
    {
        public override IEnumerable<KeyValuePair<DName, DName>> LogicalToDisplayPairs => _displayNameMap;

        // <logical, display>
        private readonly Dictionary<DName, DName> _displayNameMap = new Dictionary<DName, DName>();

        public void AddField(DName logical, DName display)
        {
            _displayNameMap.Add(logical, display);
        }

        public override bool TryGetDisplayName(DName logicalName, out DName displayName) => throw new NotImplementedException();

        public override bool TryGetLogicalName(DName displayName, out DName logicalName) => throw new NotImplementedException();
    }
}
