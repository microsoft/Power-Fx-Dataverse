//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.Types;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using OptionSetValue = Microsoft.Xrm.Sdk.OptionSetValue;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    [TestClass]
    public class AttributeUtilityTests
    {
        [DataTestMethod]
        [DataRow("_ownerid_value", "ownerid")]
        [DataRow("__ownerid__value", "_ownerid_")]
        [DataRow("_ownerid_Value", null)]
        [DataRow("__value", null)]
        [DataRow("_value", null)]
        [DataRow("", null)]
        public void OdataNameTest(string fieldName, string expected)
        {
            bool result = AttributeUtility.TryGetLogicalNameFromOdataName(fieldName, out var actual);

            if (expected == null)
            {
                Assert.IsFalse(result);
                Assert.IsNull(actual);
            }
            else
            {
                Assert.IsTrue(result);
                Assert.AreEqual(expected, actual);
            }
        }
    }
}
