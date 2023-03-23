//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

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
