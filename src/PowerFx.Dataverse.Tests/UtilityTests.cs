// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Dataverse.EntityMock;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class UtilityTests
    {
        [Fact]
        public void IsFieldPolymorphicTest()
        {
            var map = new AllTablesDisplayNameProvider();
            map.Add("local", "t1");
            var policy = new SingleOrgPolicy(map);

            (DataverseConnection dv, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModels(numberIsFloat: true, policy: policy);

            var entityMetadata = dv.GetMetadataOrThrow("local");

            // OData name.
            Assert.True(entityMetadata.IsFieldPolymorphic("new_polyfield"));

            // Logical name.
            Assert.True(entityMetadata.IsFieldPolymorphic("_new_polyfield_value"));
        }
    }
}
