//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------


using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    // Serializable form of EntityMetadata
    // Useful for testing. 
    public class EntityMetadataModel
    {
        public EntityMetadataModel() : this("placeholder") { }
        public EntityMetadataModel(string name)
        {
            LogicalName = name;
            EntitySetName = name;
            DisplayCollectionName = name;
            PrimaryIdAttribute = name;
            PrimaryNameAttribute = name;
            Attributes = new AttributeMetadataModel[] { new AttributeMetadataModel { LogicalName = name, DisplayName = name, AttributeType = AttributeTypeCode.String } };
        }

        public string LogicalName { get; set; }
        public string EntitySetName { get; set; }
        public string DisplayName { get; set; }
        public string DisplayCollectionName { get; set; }

        private string _schemaName;
        public string SchemaName => _schemaName ?? LogicalName;

        public string PrimaryIdAttribute { get; set; }
        public string PrimaryNameAttribute { get; set; }

        public Guid? DataProviderId { get; set; }

        public AttributeMetadataModel[] Attributes { get; set; }

        public OneToManyRelationshipMetadataModel[] ManyToOneRelationships { get; set; }
        public OneToManyRelationshipMetadataModel[] OneToManyRelationships { get; set; }

        public EntityMetadataModel SetSchemaName(string schemaName)
        {
            if (schemaName != null)
            {
                _schemaName = schemaName;
            }
            return this;
        }

        public EntityMetadataModel SetVirtual()
        {
            DataProviderId = Guid.NewGuid();
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

        public AttributeMetadataModel() {
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

        public static AttributeMetadataModel NewBigInt(string logicalName, string displayName)
        {
            return new AttributeMetadataModel
            {
                LogicalName = logicalName,
                DisplayName = displayName,
                AttributeType = AttributeTypeCode.BigInt
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

        public static SqlCompileOptions.TypeDetails GetIntegerHint()
        {
            return new SqlCompileOptions.TypeDetails
            {
                TypeHint = AttributeTypeCode.Integer
            };
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

    public class MockXrmMetadataProvider : IXrmMetadataProvider
    {
        private readonly Dictionary<string, EntityMetadata> _entitiesByName;

        public MockXrmMetadataProvider(params EntityMetadataModel[] entityModels)
        {
            _entitiesByName = entityModels.Select(model => model.ToXrm()).ToDictionary(model => model.LogicalName);
        }

        public bool TryGetEntityMetadata(string logicalOrDisplayName, out EntityMetadata entity)
        {
            return _entitiesByName.TryGetValue(logicalOrDisplayName, out entity);
        }
    }

    // Helpers for converting from the model classes to the real Xrm classes.
    // The only reason we need this is that Xrm classes have private setters and
    // can't be serialized / mocked. 
    public static class ModelExtensions
    {
        public static EntityMetadata ToXrm(this EntityMetadataModel model)
        {
            return Copy<EntityMetadataModel, EntityMetadata>(model);
        }

        public static EntityMetadataModel ToModel(this EntityMetadata entity)
        {
            return Copy<EntityMetadata, EntityMetadataModel>(entity);
        }

        public static AttributeMetadata[] ToXrm(AttributeMetadataModel[] array)
        {
            return Array.ConvertAll<AttributeMetadataModel, AttributeMetadata>(array, item =>
            {
                Type type = GetAttributeType(item.AttributeType.Value, item.AttributeTypeName);
                // use reflection to invoke the correct conversion for the specific attribute type
                return (AttributeMetadata)typeof(ModelExtensions).GetMethod("Copy").
                    MakeGenericMethod(typeof(AttributeMetadataModel), type).
                    Invoke(null, new object[] { item });
            });
        }

        private static Type GetAttributeType(AttributeTypeCode typeCode, AttributeTypeDisplayName typeName)
        {
            switch (typeCode)
            {
                case AttributeTypeCode.String:
                    return typeof(StringAttributeMetadata);
                case AttributeTypeCode.Integer:
                    return typeof(IntegerAttributeMetadata);
                case AttributeTypeCode.Double:
                    return typeof(DoubleAttributeMetadata);
                case AttributeTypeCode.Decimal:
                    return typeof(DecimalAttributeMetadata);
                case AttributeTypeCode.Money:
                    return typeof(MoneyAttributeMetadata);
                case AttributeTypeCode.BigInt:
                    return typeof(BigIntAttributeMetadata);
                case AttributeTypeCode.Boolean:
                    return typeof(BooleanAttributeMetadata);
                case AttributeTypeCode.Lookup:
                case AttributeTypeCode.Customer:
                case AttributeTypeCode.Owner:
                    return typeof(LookupAttributeMetadata);
                case AttributeTypeCode.DateTime:
                    return typeof(DateTimeAttributeMetadata);
                case AttributeTypeCode.Memo:
                    return typeof(MemoAttributeMetadata);
                case AttributeTypeCode.EntityName:
                    return typeof(EntityNameAttributeMetadata);
                case AttributeTypeCode.Picklist:
                    return typeof(PicklistAttributeMetadata);
                case AttributeTypeCode.State:
                    return typeof(StateAttributeMetadata);
                case AttributeTypeCode.Status:
                    return typeof(StatusAttributeMetadata);
                case AttributeTypeCode.Uniqueidentifier:
                    return typeof(UniqueIdentifierAttributeMetadata);
                case AttributeTypeCode.Virtual:
                    if (typeName == AttributeTypeDisplayName.ImageType)
                    {
                        return typeof(ImageAttributeMetadata);
                    }
                    else if (typeName == AttributeTypeDisplayName.FileType)
                    {
                        return typeof(FileAttributeMetadata);
                    }
                    else if (typeName == AttributeTypeDisplayName.MultiSelectPicklistType)
                    {
                        return typeof(MultiSelectPicklistAttributeMetadata);
                    }
                    return typeof(AttributeMetadata);
                case AttributeTypeCode.CalendarRules:
                case AttributeTypeCode.ManagedProperty:
                case AttributeTypeCode.PartyList:
                default:
                    // TODO - how to handle these?
                    return typeof(AttributeMetadata);
            }
        }


        public static AttributeMetadataModel[] ToModel(AttributeMetadata[] array)
        {
            return Array.ConvertAll(array, item =>
                Copy<AttributeMetadata, AttributeMetadataModel>(item));
        }

        public static OneToManyRelationshipMetadata[] ToXrm(OneToManyRelationshipMetadataModel[] array)
        {
            return Array.ConvertAll(array, item =>
                Copy<OneToManyRelationshipMetadataModel, OneToManyRelationshipMetadata>(item));
        }

        public static OneToManyRelationshipMetadataModel[] ToModel(OneToManyRelationshipMetadata[] array)
        {
            return Array.ConvertAll(array, item =>
                Copy<OneToManyRelationshipMetadata, OneToManyRelationshipMetadataModel>(item));
        }

        public static OptionMetadataCollection ToXrm(OptionMetadataModel[] array)
        {
            return new OptionMetadataCollection(Array.ConvertAll(array, item =>
                Copy<OptionMetadataModel, OptionMetadata>(item)));
        }

        public static TDest Copy<TSrc, TDest>(TSrc src) where TDest : new()
        {
            var obj = new TDest();

            var typeSrc = typeof(TSrc);
            var typeDest = typeof(TDest);
            foreach (var propDest in typeDest.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propSrc = typeSrc.GetProperty(propDest.Name);
                if (propSrc == null)
                {
                    continue;
                }
                var val = propSrc.GetValue(src);

                // Arrays?
                if (val is AttributeMetadataModel[] a1)
                {
                    val = ToXrm(a1);
                }
                else if (val is AttributeMetadata[] a2)
                {
                    val = ToModel(a2);
                }
                else if (val is OneToManyRelationshipMetadataModel[] a3)
                {
                    val = ToXrm(a3);
                }
                else if (val is OneToManyRelationshipMetadata[] a4)
                {
                    val = ToModel(a4);
                }

                if ((val is Label l1) && propDest.PropertyType == typeof(string))
                {
                    val = l1.UserLocalizedLabel?.Label;
                }
                else if ((val is string s1) && propDest.PropertyType == typeof(Label))
                {
                    var ll = new LocalizedLabel(s1, 1033);
                    val = new Label(ll, new LocalizedLabel[0]);
                }

                if ((val is OptionSetMetadataModel bosm) && propDest.PropertyType == typeof(BooleanOptionSetMetadata))
                {
                    val = Copy<OptionSetMetadataModel, BooleanOptionSetMetadata>(bosm);
                }
                else if (val is OptionSetMetadataModel osm)
                {
                    val = Copy<OptionSetMetadataModel, OptionSetMetadata>(osm);
                }
                else if (val is OptionMetadataModel om)
                {
                    val = Copy<OptionMetadataModel, OptionMetadata>(om);
                }
                else if (val is OptionMetadataModel[] oma)
                {
                    val = ToXrm(oma);
                }
                else if (propDest.PropertyType == typeof(AttributeTypeDisplayName) && val == null)
                {
                    // don't update the type name with a null for optional types, as it will overwrite the default
                    continue;
                }

                propDest.SetValue(obj, val);
            }

            return obj;
        }
    }
}
