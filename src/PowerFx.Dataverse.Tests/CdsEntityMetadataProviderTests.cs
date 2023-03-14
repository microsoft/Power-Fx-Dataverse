//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    [TestClass]
    public class CdsEntityMetadataProviderTests
    {

        private static readonly EntityMetadataModel _trivial = new EntityMetadataModel
        {
            LogicalName = "local",
            PrimaryIdAttribute = "localid",
            Attributes = new AttributeMetadataModel[]
            {
                    new AttributeMetadataModel
                     {
                         LogicalName= "new_field",
                         DisplayName = "field",
                         AttributeType = AttributeTypeCode.Decimal
                     },
            }
        };

        class SwitchMetadataProvider : IXrmMetadataProvider
        {
            public Func<string, EntityMetadata> _func;
            public IXrmMetadataProvider _inner;

            public void Dispose()
            {
                _func = null;
                _inner = null;
            }

            public bool TryGetEntityMetadata(string logicalOrDisplayName, out EntityMetadata entity)
            {
                if (_inner != null)
                {
                    return _inner.TryGetEntityMetadata(logicalOrDisplayName, out entity);
                }

                if (_func != null)
                {
                    entity = _func(logicalOrDisplayName);
                    return entity != null;
                }

                throw new InvalidOperationException($"failure");
            }
        }

        // Create a cloned CdsEntityMetadataProvider against a new provider.
        [TestMethod]
        public void Clone()
        {
            var provider1 = new SwitchMetadataProvider()
            {
                _inner = new MockXrmMetadataProvider(_trivial)
            };

            var metadataCache = new CdsEntityMetadataProvider(provider1);

            // Will cache
            var ok = metadataCache.TryGetXrmEntityMetadata("local", out var entityMetadata1);

            Assert.IsTrue(ok);
            Assert.IsNotNull(entityMetadata1);

            // Dispose 
            provider1.Dispose();

            Assert.ThrowsException<InvalidOperationException>(() => metadataCache.TryGetXrmEntityMetadata("second", out entityMetadata1));

            // Clone 
            var provider2 = new SwitchMetadataProvider();
            var metadataCache2 = metadataCache.Clone(provider2);
                        

            // hit the cache again
            ok = metadataCache2.TryGetXrmEntityMetadata("local", out entityMetadata1);

            Assert.IsTrue(ok);
            Assert.IsNotNull(entityMetadata1);
        }

        [TestMethod]
        public void CloneSharesCache()
        {
            var provider1 = new SwitchMetadataProvider();
            var metadataCache = new CdsEntityMetadataProvider(provider1);

            // Clone 
            var provider2 = new SwitchMetadataProvider()
            {
                _inner = new MockXrmMetadataProvider(_trivial)
            };
            var metadataCache2 = metadataCache.Clone(provider2);

            // hit the cache again
            var ok = metadataCache2.TryGetXrmEntityMetadata("local", out var entityMetadata1);

            Assert.IsTrue(ok);
            Assert.IsNotNull(entityMetadata1);

            // Shows up in original because they share a cahce
            ok = metadataCache.TryGetXrmEntityMetadata("local", out entityMetadata1);

            Assert.IsTrue(ok);
            Assert.IsNotNull(entityMetadata1);
        }
    }
}
