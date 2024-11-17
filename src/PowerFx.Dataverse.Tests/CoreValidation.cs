// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.PowerFx.Types;
using Xunit;
using Xunit.Sdk;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    // Sanity check assumptions from incoming Microsoft.PowerFx.Core nuget. 
    // If these ever fail - soemthing is fundamentally very wrong. 
    public class CoreValidation
    {
        [Fact]
        public void ConfigTest1()
        {
            // If this fails, we're hitting a bug in .Net static initialization.
            var features = Features.PowerFxV1;
            Assert.True(features.SupportColumnNamesAsIdentifiers);
        }

        [Fact]
        public void ConfigTest2()
        {
            var config = new PowerFxConfig();
            Assert.True(config.Features.SupportColumnNamesAsIdentifiers);
        }
    }
}
