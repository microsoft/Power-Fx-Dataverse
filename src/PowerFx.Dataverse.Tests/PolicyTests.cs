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

            NameCollisionException nce = Assert.Throws<NameCollisionException>(() => new SingleOrgPolicy(map));
            Assert.Equal("Name b has a collision with another display or logical name", nce.Message);
        }

        [Fact]
        public void TestPolicy3()
        {
            var map = new MyDisplayNameProvider();
            map.AddField(new DName("a"), new DName("b"));
            map.AddField(new DName("b"), new DName("a1"));

            NameCollisionException nce = Assert.Throws<NameCollisionException>(() => new SingleOrgPolicy(map));
            Assert.Equal("Name b has a collision with another display or logical name", nce.Message);
        }

        [Fact]
        public void TestPolicy4()
        {
            var map = new MyDisplayNameProvider();
            map.AddField(new DName("a"), new DName("x"));
            map.AddField(new DName("b"), new DName("x"));

            NameCollisionException nce = Assert.Throws<NameCollisionException>(() => new SingleOrgPolicy(map));
            Assert.Equal("Name b has a collision with another display or logical name", nce.Message);
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
