// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Immutable;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Xunit;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class AttributeUtilityTests
    {
        internal readonly IImmutableList<FormulaValue> ExampleValues = ImmutableList.Create<FormulaValue>(
            FormulaValue.New(1),
            FormulaValue.New(1m),
            FormulaValue.New("test"),
            FormulaValue.New(true),
            FormulaValue.New(Guid.NewGuid()),
            FormulaValue.New(new DateTime(2023, 07, 06)),
            FormulaValue.NewDateOnly(new DateTime(2023, 07, 06)),
            FormulaValue.NewBlank(),
            FormulaValue.NewError(new ExpressionError(), FormulaType.Blank),
            FormulaValue.NewVoid());

        [Theory]
        [InlineData("_ownerid_value", "ownerid")]
        [InlineData("__ownerid__value", "_ownerid_")]
        [InlineData("_ownerid_Value", null)]
        [InlineData("__value", null)]
        [InlineData("_value", null)]
        [InlineData("", null)]
        public void OdataNameTest(string fieldName, string expected)
        {
            bool result = AttributeUtility.TryGetLogicalNameFromOdataName(fieldName, out var actual);

            if (expected == null)
            {
                Assert.False(result);
                Assert.Null(actual);
            }
            else
            {
                Assert.True(result);
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void AttributeSerializeTest()
        {
            (DataverseConnection dv, IDataverseServices ds, EntityLookup el) = PluginExecutionTests.CreateMemoryForRelationshipModelsInternal();
            var entityMetadata = dv.GetMetadataOrThrow("local");
            foreach (var attribute in entityMetadata.Attributes)
            {
                foreach (var fv in ExampleValues)
                {
                    var fieldName = attribute.LogicalName;
                    Assert.True(entityMetadata.TryGetAttribute(fieldName, out var attributeMetadata));
                    var attrType = attributeMetadata.AttributeType.Value;
                    Func<object> value = () => attributeMetadata.ToAttributeObject(fv);

                    if (attrType == AttributeTypeCode.Boolean && fv.Type == FormulaType.Boolean)
                    {
                        Assert.True(value() is bool);
                    }
                    else if (attrType == AttributeTypeCode.DateTime && (fv.Type == FormulaType.DateTime || fv.Type == FormulaType.Date))
                    {
                        Assert.True(value() is DateTime);
                    }
                    else if (attrType == AttributeTypeCode.Decimal && (fv.Type == FormulaType.Number || fv.Type == FormulaType.Decimal))
                    {
                        Assert.True(value() is decimal);
                    }
                    else if (attrType == AttributeTypeCode.Double && fv.Type == FormulaType.Number)
                    {
                        Assert.True(value() is double);
                    }
                    else if (attrType == AttributeTypeCode.Integer && (fv.Type == FormulaType.Number || fv.Type == FormulaType.Decimal))
                    {
                        Assert.True(value() is int);
                    }
                    else if (attrType == AttributeTypeCode.String && fv.Type == FormulaType.String)
                    {
                        Assert.True(value() is string);
                    }
                    else if (attrType == AttributeTypeCode.BigInt && (fv.Type == FormulaType.Number || fv.Type == FormulaType.Decimal))
                    {
                        Assert.True(value() is long);
                    }
                    else if (attrType == AttributeTypeCode.Uniqueidentifier && fv.Type == FormulaType.Guid)
                    {
                        Assert.True(value() is Guid);
                    }
                    else if (attrType == AttributeTypeCode.Money && (fv.Type == FormulaType.Number || fv.Type == FormulaType.Decimal))
                    {
                        Assert.True(value() is Money);
                    }
                    else if (attrType == AttributeTypeCode.Lookup && fv.Type is RecordType)
                    {
                        Assert.True(value() is EntityReference);
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
