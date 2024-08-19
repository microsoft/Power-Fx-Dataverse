// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;

namespace Microsoft.PowerFx.Dataverse.Tests.DelegationTests
{
    public class TestSingleOrgPolicy : SingleOrgPolicy
    {
        private List<TestDataverseTableValue> _dvTableValues = new List<TestDataverseTableValue>();

        public TestSingleOrgPolicy(DisplayNameProvider displayNameLookup) 
            : base(displayNameLookup)
        {            
        }

        internal override DataverseTableValue NewDataverseTableValue(RecordType recordType, DataverseConnection dataverseConnection, EntityMetadata entityMetadata)
        {
            TestDataverseTableValue dvTableValue = new TestDataverseTableValue(recordType, dataverseConnection, entityMetadata);
            _dvTableValues.Add(dvTableValue);
            return dvTableValue;
        }

        public IEnumerable<DataverseDelegationParameters> GetDelegationParameters() => _dvTableValues.Select(tdvtv => tdvtv.DelegationParameters).Where(dp => dp != null);        
    }
}
