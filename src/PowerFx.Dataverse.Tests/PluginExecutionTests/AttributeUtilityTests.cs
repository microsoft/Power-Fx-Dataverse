//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Immutable;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    [TestClass]
    public class AttributeUtilityTests
    {

        internal readonly IImmutableList<FormulaValue> exampleValues = ImmutableList.Create<FormulaValue>(
            FormulaValue.New(1),
            FormulaValue.New(1m),
            FormulaValue.New("test"),
            FormulaValue.New(true),
            FormulaValue.New(Guid.NewGuid()),
            FormulaValue.New(new DateTime(2023, 07, 06)),
            FormulaValue.NewDateOnly(new DateTime(2023, 07, 06)),
            FormulaValue.NewBlank(),
            FormulaValue.NewError(new ExpressionError(), FormulaType.Blank),
            FormulaValue.NewVoid()
         );

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

        [TestMethod]
        public void AttributeSerializeTest()
        {
            (DataverseConnection dv, IDataverseServices ds, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModelsInternal();
            var entityMetadata = dv.GetMetadataOrThrow("local");
            foreach (var attribute in entityMetadata.Attributes)
            {
                foreach (var fv in exampleValues)
                {
                    var fieldName = attribute.LogicalName;
                    Assert.IsTrue(entityMetadata.TryGetAttribute(fieldName, out var attributeMetadata));
                    var attrType = attributeMetadata.AttributeType.Value;
                    Func<object> value = () => attributeMetadata.ToAttributeObject(fv);

                    if (attrType == AttributeTypeCode.Boolean && fv.Type == FormulaType.Boolean)
                    {
                        Assert.IsTrue(value() is bool);
                    }
                    else if (attrType == AttributeTypeCode.DateTime && (fv.Type == FormulaType.DateTime || fv.Type == FormulaType.Date))
                    {
                        Assert.IsTrue(value() is DateTime);
                    }
                    else if (attrType == AttributeTypeCode.Decimal && (fv.Type == FormulaType.Number || fv.Type == FormulaType.Decimal))
                    {
                        Assert.IsTrue(value() is decimal);
                    }
                    else if (attrType == AttributeTypeCode.Double && fv.Type == FormulaType.Number)
                    {
                        Assert.IsTrue(value() is double);
                    }
                    else if (attrType == AttributeTypeCode.Integer && (fv.Type == FormulaType.Number || fv.Type == FormulaType.Decimal))
                    {
                        Assert.IsTrue(value() is int);
                    }
                    else if (attrType == AttributeTypeCode.String && fv.Type == FormulaType.String)
                    {
                        Assert.IsTrue(value() is string);
                    }
                    else if (attrType == AttributeTypeCode.BigInt && (fv.Type == FormulaType.Number || fv.Type == FormulaType.Decimal))
                    {
                        Assert.IsTrue(value() is long);
                    }
                    else if (attrType == AttributeTypeCode.Uniqueidentifier && fv.Type == FormulaType.Guid)
                    {
                        Assert.IsTrue(value() is Guid);
                    }
                    else if (attrType == AttributeTypeCode.Money && (fv.Type == FormulaType.Number || fv.Type == FormulaType.Decimal))
                    {
                        Assert.IsTrue(value() is Money);
                    }
                    else if (attrType == AttributeTypeCode.Lookup && fv.Type is RecordType)
                    {
                        Assert.IsTrue(value() is EntityReference);
                    }
                    else
                    {
                        try
                        {
                            value();
                        }
                        catch (NotImplementedException)
                        {
                        }
                        catch (InvalidOperationException)
                        {
                        }
                        catch (InvalidCastException)
                        {
                        }
                    }
                }
            }
        }
    }
}
