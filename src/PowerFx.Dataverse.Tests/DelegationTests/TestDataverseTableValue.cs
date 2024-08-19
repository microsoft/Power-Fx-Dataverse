// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    internal class TestDataverseTableValue : DataverseTableValue
    {
        internal DataverseDelegationParameters DelegationParameters;

        internal TestDataverseTableValue(RecordType recordType, IConnectionValueContext connection, EntityMetadata metadata)
            : base(recordType, connection, metadata)
        {
        }

        public override Task<IReadOnlyCollection<DValue<RecordValue>>> GetRowsAsync(IServiceProvider services, DelegationParameters parameters, CancellationToken cancellationToken)
        {
            DelegationParameters = (DataverseDelegationParameters)parameters;

            return base.GetRowsAsync(services, parameters, cancellationToken);
        }
    }
}
