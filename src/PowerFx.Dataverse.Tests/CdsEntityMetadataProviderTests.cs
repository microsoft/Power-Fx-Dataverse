//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using Microsoft.Xrm.Sdk.Metadata;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests
{
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

        private class SwitchMetadataProvider : IXrmMetadataProvider
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
        [Fact]
        public void Clone()
        {
            var provider1 = new SwitchMetadataProvider()
            {
                _inner = new MockXrmMetadataProvider(_trivial)
            };

            var metadataCache = new CdsEntityMetadataProvider(provider1);

            // Will cache
            var ok = metadataCache.TryGetXrmEntityMetadata("local", out var entityMetadata1);

            Assert.True(ok);
            Assert.NotNull(entityMetadata1);

            // Dispose 
            provider1.Dispose();

            Assert.Throws<InvalidOperationException>(() => metadataCache.TryGetXrmEntityMetadata("second", out entityMetadata1));

            // Clone 
            var provider2 = new SwitchMetadataProvider();
            var metadataCache2 = metadataCache.Clone(provider2);
                        

            // hit the cache again
            ok = metadataCache2.TryGetXrmEntityMetadata("local", out entityMetadata1);

            Assert.True(ok);
            Assert.NotNull(entityMetadata1);
        }

        [Fact]
        public void CloneSharesXRMCache()
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

            Assert.True(ok);
            Assert.NotNull(entityMetadata1);

            // Shows up in original because they share a cahce
            ok = metadataCache.TryGetXrmEntityMetadata("local", out entityMetadata1);

            Assert.True(ok);
            Assert.NotNull(entityMetadata1);
        }

        /// <summary>
        /// Clone should not hold on to CDS Cache, because that would mean caching the DType as well and 
        /// that could lead to Problems in multi threaded env.
        /// </summary>
        [Fact]
        public void CloneDoesNotSharesCDSCache()
        {
            var provider = new MockXrmMetadataProvider(_trivial);
            var CDSMetadata = new CdsEntityMetadataProvider(provider);

            var ok = CDSMetadata.TryGetDataSource("local", out var dvSource);
            Assert.True(ok);
            var schema = dvSource.Schema;

            var anotherProvider = new SwitchMetadataProvider();
            var clone = CDSMetadata.Clone(anotherProvider);

            var cloneOk = clone.TryGetDataSource("local", out var clonedDvSource);
            Assert.True(cloneOk);
            var clonedSchema = clonedDvSource.Schema;

            Assert.NotSame(schema, clonedSchema);
        }
    }
}
