//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

namespace Microsoft.Dataverse.EntityMock
{
    // Serializable form of EntityMetadata
    // Useful for testing. 
    public class EntityMetadataModel
    {
        public string LogicalName { get; set; }
        public string EntitySetName { get; set; }
        public string DisplayName { get; set; }
        public string DisplayCollectionName { get; set; }

        private string _schemaName;
        public string SchemaName => _schemaName ?? LogicalName;

        public string PrimaryIdAttribute { get; set; }
        public string PrimaryNameAttribute { get; set; }

        public bool IsPrivate { get; set; }
        public bool IsIntersect { get; set; }
        public bool IsLogicalEntity { get; set; }
        public int ObjectTypeCode { get; set; }

        public Guid? DataProviderId { get; set; }

        public AttributeMetadataModel[] Attributes { get; set; }

        public OneToManyRelationshipMetadataModel[] ManyToOneRelationships { get; set; }

        public OneToManyRelationshipMetadataModel[] OneToManyRelationships { get; set; }
        public EntityMetadataModel() : this("placeholder") { }
        public EntityMetadataModel(string name)
        {
            LogicalName = name;
            EntitySetName = name;
            DisplayCollectionName = name;
            PrimaryIdAttribute = name;
            PrimaryNameAttribute = name;
            IsPrivate = false;
            IsIntersect = false;
            ObjectTypeCode = 1;
            IsLogicalEntity = false;
            Attributes = new AttributeMetadataModel[] { new AttributeMetadataModel { LogicalName = name, DisplayName = name, AttributeType = AttributeTypeCode.String } };
        }

        public EntityMetadataModel SetSchemaName(string schemaName)
        {
            if (schemaName != null)
            {
                _schemaName = schemaName;
            }
            return this;
        }
    }

    public class AttributeMetadataModel
    {
        public string LogicalName { get; set; }
        public string DisplayName { get; set; }

        private string _schemaName;
        public string SchemaName => _schemaName ?? LogicalName;

        public int? SourceType { get; set; }

        public AttributeTypeCode? AttributeType { get; set; }

        public bool IsValidForRead { get; set; }

        public string[] Targets { get; set; }

        // format can be a StringFormat for strings, or a DateTimeFormat for dates
        public object Format { get; set; }

        public DateTimeBehavior DateTimeBehavior { get; set; }

        public OptionSetMetadataModel OptionSet { get; set; }

        public AttributeTypeDisplayName AttributeTypeName { get; set; }

        public bool? IsLogical { get; set; }

        public AttributeMetadataModel()
        {
            IsValidForRead = true;
        }

        public AttributeMetadataModel SetSchemaName(string schemaName)
        {
            if (schemaName != null)
            {
                _schemaName = schemaName;
            }
            return this;
        }

        public static AttributeMetadataModel NewDecimal(string logicalName, string displayName, string schemaName = null)
        {
            return new AttributeMetadataModel
            {
                LogicalName = logicalName,
                DisplayName = displayName,
                AttributeType = AttributeTypeCode.Decimal
            }.SetSchemaName(schemaName);
        }



        public static AttributeMetadataModel NewDouble(string logicalName, string displayName)
        {
            return new AttributeMetadataModel
            {
                LogicalName = logicalName,
                DisplayName = displayName,
                AttributeType = AttributeTypeCode.Double
            };
        }

        public static AttributeMetadataModel NewInteger(string logicalName, string displayName, IntegerFormat format = IntegerFormat.None)
        {
            return new AttributeMetadataModel
            {
                LogicalName = logicalName,
                DisplayName = displayName,
                Format = format,
                AttributeType = AttributeTypeCode.Integer
            };
        }

        public static AttributeMetadataModel NewMoney(string logicalName, string displayName)
        {
            return new AttributeMetadataModel
            {
                LogicalName = logicalName,
                DisplayName = displayName,
                AttributeType = AttributeTypeCode.Money
            };
        }

        public static AttributeMetadataModel NewString(string logicalName, string displayName, StringFormat format = StringFormat.Text)
        {
            return new AttributeMetadataModel
            {
                LogicalName = logicalName,
                DisplayName = displayName,
                AttributeType = AttributeTypeCode.String,
                Format = format,
            };
        }

        public static AttributeMetadataModel NewDateTime(string logicalName, string displayName, DateTimeBehavior behavior, DateTimeFormat format)
        {
            return new AttributeMetadataModel
            {
                LogicalName = logicalName,
                DisplayName = displayName,
                AttributeType = AttributeTypeCode.DateTime,
                DateTimeBehavior = behavior,
                Format = format
            };
        }

        public static AttributeMetadataModel NewGuid(string logicalName, string displayName)
        {
            return new AttributeMetadataModel
            {
                LogicalName = logicalName,
                DisplayName = displayName,
                AttributeType = AttributeTypeCode.Uniqueidentifier
            };
        }

        public static AttributeMetadataModel NewLookup(string logicalName, string displayName, string[] targets, AttributeTypeCode attributeType = AttributeTypeCode.Lookup)
        {
            return new AttributeMetadataModel
            {
                LogicalName = logicalName,
                DisplayName = displayName,
                AttributeType = attributeType,
                Targets = targets
            };
        }

        public static AttributeMetadataModel NewBoolean(string logicalName, string displayName, string trueValue, string falseValue)
        {
            return new AttributeMetadataModel
            {
                LogicalName = logicalName,

                DisplayName = displayName,
                AttributeType = AttributeTypeCode.Boolean,
                OptionSet = new OptionSetMetadataModel
                {
                    IsGlobal = false,
                    Name = logicalName + "_optionSet",
                    TrueOption = new OptionMetadataModel
                    {
                        Label = trueValue,
                        Value = 1
                    },
                    FalseOption = new OptionMetadataModel
                    {
                        Label = falseValue,
                        Value = 0
                    }
                }
            };
        }

        public static AttributeMetadataModel NewPicklist(string logicalName, string displayName, OptionMetadataModel[] options, AttributeTypeCode attributeType = AttributeTypeCode.Picklist, bool isGlobal = false, AttributeTypeDisplayName typeName = null)
        {
            return new AttributeMetadataModel
            {
                LogicalName = logicalName,
                DisplayName = displayName,
                AttributeType = attributeType,
                AttributeTypeName = typeName,
                OptionSet = new OptionSetMetadataModel
                {
                    IsGlobal = isGlobal,
                    DisplayName = displayName,
                    Name = logicalName + "_optionSet",
                    Options = options
                }
            };
        }

        public static AttributeMetadataModel NewImage(string logicalName, string displayName)
        {
            return new AttributeMetadataModel
            {
                LogicalName = logicalName,
                DisplayName = displayName,
                AttributeType = AttributeTypeCode.Virtual,
                AttributeTypeName = AttributeTypeDisplayName.ImageType
            };
        }

        public static AttributeMetadataModel NewFile(string logicalName, string displayName)
        {
            return new AttributeMetadataModel
            {
                LogicalName = logicalName,
                DisplayName = displayName,
                AttributeType = AttributeTypeCode.Virtual,
                AttributeTypeName = AttributeTypeDisplayName.FileType
            };
        }

        // Simulate a future unsupported type. 
        // This tests cases where Dataverse adds a type and feeds into an older version of the compiler.
        public static AttributeMetadataModel NewFutureUnsupported(string logicalName, string displayName)
        {
            return new AttributeMetadataModel
            {
                LogicalName = logicalName,
                DisplayName = displayName,
                AttributeType = (AttributeTypeCode)9999,
                AttributeTypeName = AttributeTypeDisplayName.CustomType
            };
        }

        public AttributeMetadataModel SetCalculated()
        {
            this.SourceType = 3;
            return this;
        }

        public AttributeMetadataModel SetLogical(bool value = true)
        {
            this.IsLogical = value;
            return this;
        }

    }

    public class OneToManyRelationshipMetadataModel
    {
        public string ReferencedAttribute { get; set; }
        public string ReferencedEntity { get; set; }
        public string ReferencingAttribute { get; set; }
        public string ReferencingEntity { get; set; }
        public string ReferencedEntityNavigationPropertyName { get; set; }
        public string ReferencingEntityNavigationPropertyName { get; set; }
        public string SchemaName { get; set; }
    }

    public class OptionMetadataModel
    {
        public string Label { get; set; }
        public int? Value { get; set; }
    }

    public class OptionSetMetadataModel
    {
        public bool IsGlobal { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public OptionMetadataModel[] Options { get; set; }
        public OptionMetadataModel TrueOption { get; set; }
        public OptionMetadataModel FalseOption { get; set; }
    }
}
