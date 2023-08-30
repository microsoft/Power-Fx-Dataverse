//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Reflection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

namespace Microsoft.Dataverse.EntityMock
{
    // Helpers for converting from the model classes to the real Xrm classes.
    // The only reason we need this is that Xrm classes have private setters and
    // can't be serialized / mocked. 
    public static class ModelExtensions
    {
        public static EntityMetadata ToXrm(this EntityMetadataModel model)
        {
            return Copy<EntityMetadataModel, EntityMetadata>(model);
        }

        public static EntityMetadata ToXrm(this EntityMetadataModel model, AttributeMetadataModel[] array)
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
