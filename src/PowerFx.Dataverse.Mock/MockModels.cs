//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

namespace Microsoft.Dataverse.EntityMock
{
    /// <summary>
    /// Models used to run relationship tests
    /// </summary>
    public static class MockModels
    {
        /// <summary>
        /// Simulates an entity with some major column types.
        /// </summary>
        public static readonly EntityMetadataModel LocalModel = new EntityMetadataModel
        {
            LogicalName = "local",
            DisplayCollectionName = "Locals",
            PrimaryIdAttribute = "localid",
            Attributes = new AttributeMetadataModel[]
            {
                AttributeMetadataModel.NewString("new_name", "Name"),
                AttributeMetadataModel.NewDecimal("conflict1", "Conflict"),
                AttributeMetadataModel.NewDecimal("conflict2", "Conflict"),
                AttributeMetadataModel.NewDecimal("new_price", "Price"),
                AttributeMetadataModel.NewDecimal("old_price", "Old_Price"),
                AttributeMetadataModel.NewDateTime("new_date", "Date", DateTimeBehavior.DateOnly, DateTimeFormat.DateOnly),
                AttributeMetadataModel.NewDateTime("new_datetime", "DateTime", DateTimeBehavior.TimeZoneIndependent, DateTimeFormat.DateAndTime),
                AttributeMetadataModel.NewMoney("new_currency", "Currency"),
                AttributeMetadataModel.NewDecimal("new_quantity", "Quantity"),
                AttributeMetadataModel.NewLookup("otherid", "Other", new string[] { "remote" }),
                AttributeMetadataModel.NewLookup("elastic_ref", "Elastic Ref", new string[] { "elastictable" }),
                AttributeMetadataModel.NewLookup("selfid", "Self Reference", new string[] { "local" }),
                AttributeMetadataModel.NewLookup("virtualid", "Virtual Lookup", new string[] { "virtualremote" }),
                AttributeMetadataModel.NewLookup("logicalid", "Logical Lookup", new string[] { "remote" }).SetLogical(),
                AttributeMetadataModel.NewLookup("new_polyfield", "PolymorphicLookup", new string[] { "remote", "local" }),
                AttributeMetadataModel.NewGuid("localid", "LocalId"),
                AttributeMetadataModel.NewDouble("float", "Float"),
                AttributeMetadataModel.NewBoolean("new_bool", "Boolean", "true", "false"),
                AttributeMetadataModel.NewInteger("new_int", "Integer"),
                AttributeMetadataModel.NewString("new_string", "String"),
                AttributeMetadataModel.NewString("fullname", "Full name").SetReadOnly(), // 'fullname' is a DV read-only virtual field.
                AttributeMetadataModel.NewGuid("some_id", "SomeId"),
                AttributeMetadataModel.NewPicklist("rating", "Rating", new OptionMetadataModel[]
                {
                    new OptionMetadataModel { Label = "Hot", Value = 1 },
                    new OptionMetadataModel { Label = "Warm", Value = 2 },
                    new OptionMetadataModel { Label = "Cold", Value = 3 }
                }),
                AttributeMetadataModel.NewPicklist("global_pick", "Global Picklist", new OptionMetadataModel[]
                {
                    new OptionMetadataModel { Label = "High", Value = 1 },
                    new OptionMetadataModel { Label = "Medium", Value = 2 },
                    new OptionMetadataModel { Label = "Low", Value = 3 }
                },
                isGlobal: true),
                AttributeMetadataModel.NewPicklist("new_status", "Status", new OptionMetadataModel[]
                {
                    new OptionMetadataModel { Label = "New", Value = 1 },
                    new OptionMetadataModel { Label = "Active", Value = 2 },
                    new OptionMetadataModel { Label = "Resolved", Value = 3 }
                },
                attributeType: AttributeTypeCode.Status,
                isGlobal: true),
                AttributeMetadataModel.NewPicklist("new_state", "State", new OptionMetadataModel[]
                {
                    new OptionMetadataModel { Label = "In Active", Value = 0 },
                    new OptionMetadataModel { Label = "Active", Value = 1 }
                },
                attributeType: AttributeTypeCode.State),
            },
            ManyToOneRelationships = new OneToManyRelationshipMetadataModel[]
            {
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "remoteid",
                    ReferencedEntity = "remote",
                    ReferencingAttribute = "otherid",
                    ReferencingEntity = "local",
                    ReferencedEntityNavigationPropertyName = "refd",
                    ReferencingEntityNavigationPropertyName = "refg",
                    SchemaName = "local_remote"
                },
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "localid",
                    ReferencedEntity = "local",
                    ReferencingAttribute = "selfid",
                    ReferencingEntity = "local",
                    ReferencedEntityNavigationPropertyName = "self_refd",
                    ReferencingEntityNavigationPropertyName = "self",
                    SchemaName = "self"
                },
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "virtualremoteid",
                    ReferencedEntity = "virtualremote",
                    ReferencingAttribute = "virtualid",
                    ReferencingEntity = "local",
                    ReferencedEntityNavigationPropertyName = "virtual_refd",
                    ReferencingEntityNavigationPropertyName = "virtual",
                    SchemaName = "virtual"
                },
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "remoteid",
                    ReferencedEntity = "remote",
                    ReferencingAttribute = "logicalid",
                    ReferencingEntity = "local",
                    ReferencedEntityNavigationPropertyName = "logical_refd",
                    ReferencingEntityNavigationPropertyName = "logical",
                    SchemaName = "logical"
                },
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "remoteid",
                    ReferencedEntity = "remote",
                    ReferencingAttribute = "new_polyfield",
                    ReferencingEntity = "local",
                    ReferencedEntityNavigationPropertyName = "logical_refd",
                    ReferencingEntityNavigationPropertyName = "new_polyfield_t2_t1", /* ideally instead of t2_t1, it should be local names of it */
                    SchemaName = "logical"
                },
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "localid",
                    ReferencedEntity = "local",
                    ReferencingAttribute = "new_polyfield",
                    ReferencingEntity = "local",
                    ReferencedEntityNavigationPropertyName = "logical_refd",
                    ReferencingEntityNavigationPropertyName = "new_polyfield_t1_t1",
                    SchemaName = "logical"
                },
                 new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "etid",
                    ReferencedEntity = "elastictable",
                    ReferencingAttribute = "elastic_ref",
                    ReferencingEntity = "local",
                    ReferencedEntityNavigationPropertyName = "logical_refd",
                    ReferencingEntityNavigationPropertyName = "elastic_ref_local_et",
                    SchemaName = "logical"
                }
            },
            OneToManyRelationships = new OneToManyRelationshipMetadataModel[]
            {
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "localid",
                    ReferencedEntity = "local",
                    ReferencingAttribute = "selfid",
                    ReferencingEntity = "local",
                    ReferencedEntityNavigationPropertyName = "self_refd",
                    ReferencingEntityNavigationPropertyName = "self",
                    SchemaName = "self"
                }
            }
        };

        /// <summary>
        ///  Simple model used to simulate a lookup column.
        /// </summary>
        public static readonly EntityMetadataModel RemoteModel = new EntityMetadataModel
        {
            LogicalName = "remote",
            DisplayCollectionName = "Remotes",
            PrimaryIdAttribute = "remoteid",
            Attributes = new AttributeMetadataModel[]
            {
                AttributeMetadataModel.NewDecimal("data", "Data"),
                AttributeMetadataModel.NewGuid("remoteid", "RemoteId"),
                AttributeMetadataModel.NewDecimal("calc", "Calculated Data").SetCalculated(),
                AttributeMetadataModel.NewDecimal("float", "Float"),
                AttributeMetadataModel.NewDouble("actual_float", "Actual Float"),
                AttributeMetadataModel.NewPicklist("rating", "Rating", new OptionMetadataModel[]
                {   new OptionMetadataModel { Label = "Small", Value = 1},
                    new OptionMetadataModel { Label = "Medium", Value = 2 },
                    new OptionMetadataModel { Label = "Large", Value = 3 }
                }),
                AttributeMetadataModel.NewLookup("otherotherid", "Other Other", new string[] { "doubleremote" }),
                AttributeMetadataModel.NewDouble("other", "Other")
            },
            ManyToOneRelationships = new OneToManyRelationshipMetadataModel[]
            {
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "doubleremoteid",
                    ReferencedEntity = "doubleremote",
                    ReferencingAttribute = "otherotherid",
                    ReferencingEntity = "remote",
                    ReferencedEntityNavigationPropertyName = "doublerefd",
                    ReferencingEntityNavigationPropertyName = "doublerefg",
                    SchemaName = "remote_doubleremote"
                }
            }
        };

        /// <summary>
        ///  Simple model used to simulate a double level lookup column (entity.remote.doubleremote).
        /// </summary>
        public static readonly EntityMetadataModel DoubleRemoteModel = new EntityMetadataModel
        {
            LogicalName = "doubleremote",
            DisplayCollectionName = "Double Remotes",
            PrimaryIdAttribute = "doubleremoteid",
            Attributes = new AttributeMetadataModel[]
            {
                AttributeMetadataModel.NewDecimal("data2", "Data Two"),
                AttributeMetadataModel.NewGuid("doubleremoteid", "DoubleRemoteId"),
                AttributeMetadataModel.NewLookup("otherotherotherid", "Other Other Other", new string[] { "tripleremote" })
            },
            ManyToOneRelationships = new OneToManyRelationshipMetadataModel[]
            {
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "tripleremoteid",
                    ReferencedEntity = "tripleremote",
                    ReferencingAttribute = "otherotherotherid",
                    ReferencingEntity = "doubleremote",
                    ReferencedEntityNavigationPropertyName = "triplerefd",
                    ReferencingEntityNavigationPropertyName = "triplerefg",
                    SchemaName = "doubleremote_tripleremote"
                }
            }
        };

        /// <summary>
        ///  Simple model used to simulate a triple level lookup column (entity.remote.doubleremote.tripleremote).
        /// </summary>
        public static readonly EntityMetadataModel TripleRemoteModel = new EntityMetadataModel
        {
            LogicalName = "tripleremote",
            DisplayCollectionName = "Triple Remotes",
            PrimaryIdAttribute = "tripleremoteid",
            Attributes = new AttributeMetadataModel[]
            {
                AttributeMetadataModel.NewDecimal("data3", "Data Three"),
                AttributeMetadataModel.NewGuid("tripleremoteid", "TripleRemoteId"),
                AttributeMetadataModel.NewMoney("currencyField", "Currency Field"),
                AttributeMetadataModel.NewPicklist("optionsetField", "Optionset Field", new OptionMetadataModel[]
                {
                    new OptionMetadataModel { Label = "One", Value = 1 },
                    new OptionMetadataModel { Label = "Two", Value = 2 },
                })
            }
        };

        /// <summary>
        ///  Simulates a virtual entity metadata.
        /// </summary>
        public static readonly EntityMetadataModel VirtualRemoteModel = new EntityMetadataModel
        {
            LogicalName = "virtualremote",
            DisplayCollectionName = "Virtual Remotes",
            PrimaryIdAttribute = "virtualremoteid",
            DataProviderId = Guid.NewGuid(),
            Attributes = new AttributeMetadataModel[]
            {
                AttributeMetadataModel.NewDecimal("vdata", "Virtual Data"),
                AttributeMetadataModel.NewGuid("virtualremoteid", "VirtualRemoteId")
            }
        };

        /// <summary>
        ///  Simple model used to simulate a lookup column.
        /// </summary>
        public static readonly EntityMetadataModel ElasticTableModel = new EntityMetadataModel
        {
            LogicalName = "elastictable",
            DisplayCollectionName = "Elastic Table",
            PrimaryIdAttribute = "etid",

            // below makes this metadata elastic table.
            DataProviderId = Guid.Parse("1d9bde74-9ebd-4da9-8ff5-aa74945b9f74"),

            Attributes = new AttributeMetadataModel[]
            {
                AttributeMetadataModel.NewGuid("etid", "etid"),
                AttributeMetadataModel.NewDecimal("field1", "Field1"),

                // Below is a key attribute in elastic table.
                AttributeMetadataModel.NewString("partitionid", "Partition Id"),
            },
            ManyToOneRelationships = new OneToManyRelationshipMetadataModel[]
            {
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "localid",
                    ReferencedEntity = "local",
                    ReferencingAttribute = "selfid",
                    ReferencingEntity = "local",
                    ReferencedEntityNavigationPropertyName = "self_refd",
                    ReferencingEntityNavigationPropertyName = "self",
                    SchemaName = "self"
                },
            }
        };

        /// <summary>
        ///  Array with multiple models pre-loaded.
        /// </summary>
        public static readonly EntityMetadataModel[] RelationshipModels = new EntityMetadataModel[] { LocalModel, RemoteModel, DoubleRemoteModel, TripleRemoteModel, VirtualRemoteModel, ElasticTableModel };

        /// <summary>
        /// Idealy contains all columns types.
        /// </summary>
        public static readonly EntityMetadataModel AllAttributeModel = new EntityMetadataModel
        {
            LogicalName = "allattributes",
            DisplayCollectionName = "All Attributes",
            PrimaryIdAttribute = "allid",
            Attributes = new AttributeMetadataModel[]
            {
                AttributeMetadataModel.NewDecimal("new_field", "field"),
                AttributeMetadataModel.NewDouble("double", "Double"),
                AttributeMetadataModel.NewInteger("int", "Int"),
                AttributeMetadataModel.NewLookup("new_lookup", "Lookup", new [] { "tripleremote" }),
                AttributeMetadataModel.NewLookup("selfid", "Self Reference", new [] { "allattributes" }),
                AttributeMetadataModel.NewMoney("money", "Money"),
                AttributeMetadataModel.NewGuid("guid", "Guid"),
                AttributeMetadataModel.NewGuid("allid", "AllId"),
                AttributeMetadataModel.NewString("string", "String"),
                AttributeMetadataModel.NewString("new_test", "Test"),
                AttributeMetadataModel.NewString("hyperlink", "Hyperlink", StringFormat.Url),
                AttributeMetadataModel.NewString("email", "Email", StringFormat.Email),
                AttributeMetadataModel.NewString("ticker", "Ticker", StringFormat.TickerSymbol),
                AttributeMetadataModel.NewInteger("timezone", "TimeZone", IntegerFormat.TimeZone),
                AttributeMetadataModel.NewInteger("duration", "Duration", IntegerFormat.Duration),
                AttributeMetadataModel.NewDateTime("userlocaldatetime", "UserLocal DateTime", DateTimeBehavior.UserLocal, DateTimeFormat.DateAndTime),
                AttributeMetadataModel.NewDateTime("userlocaldateonly", "UserLocal DateOnly", DateTimeBehavior.UserLocal, DateTimeFormat.DateOnly),
                AttributeMetadataModel.NewDateTime("dateonly", "DateOnly", DateTimeBehavior.DateOnly, DateTimeFormat.DateOnly),
                AttributeMetadataModel.NewDateTime("timezoneindependentdatetime", "TimeZoneIndependent DateTime", DateTimeBehavior.TimeZoneIndependent, DateTimeFormat.DateAndTime),
                AttributeMetadataModel.NewDateTime("timezoneindependentdateonly", "TimeZoneIndependent DateOnly", DateTimeBehavior.TimeZoneIndependent, DateTimeFormat.DateOnly),
                AttributeMetadataModel.NewString("fullname", "Full name").SetReadOnly(), // 'fullname' is a DV read-only virtual field.
                new AttributeMetadataModel
                {
                    LogicalName= "bigint",
                    DisplayName = "BigInt",
                    AttributeType = AttributeTypeCode.BigInt
                },
                AttributeMetadataModel.NewBoolean("boolean", "Boolean", "Yes", "No"),
                AttributeMetadataModel.NewLookup("customerid", "Customer", new [] { "tripleremote" }, AttributeTypeCode.Customer),
                new AttributeMetadataModel
                {
                    LogicalName = "EntityName",
                    DisplayName = "EntityName",
                    AttributeType = AttributeTypeCode.EntityName
                },
                new AttributeMetadataModel
                {
                    LogicalName = "Memo",
                    DisplayName = "Memo",
                    AttributeType = AttributeTypeCode.Memo
                },
                AttributeMetadataModel.NewLookup("ownerid", "Owner", new [] { "tripleremote" }, AttributeTypeCode.Owner),
                AttributeMetadataModel.NewPicklist(
                    "statecode",
                    "State",
                    new OptionMetadataModel[]
                    {
                        new OptionMetadataModel
                        {
                            Label = "Active",
                            Value = 1
                        },
                        new OptionMetadataModel
                        {
                            Label = "Inactive",
                            Value = 2
                        }
                    },
                    AttributeTypeCode.State),
                AttributeMetadataModel.NewPicklist(
                    "statuscode",
                    "Status",
                    new OptionMetadataModel[]
                    {
                        new OptionMetadataModel
                        {
                            Label = "Active",
                            Value = 1
                        },
                        new OptionMetadataModel
                        {
                            Label = "Inactive",
                            Value = 2
                        }
                    },
                    AttributeTypeCode.Status),
                AttributeMetadataModel.NewPicklist(
                    "picklist",
                    "Picklist",
                    new OptionMetadataModel[]
                    {
                        new OptionMetadataModel
                        {
                            Label = "One",
                            Value = 1
                        },
                        new OptionMetadataModel
                        {
                            Label = "Two",
                            Value = 2
                        },
                        new OptionMetadataModel
                        {
                            Label = "Three",
                            Value = 3
                        }
                    }),
                AttributeMetadataModel.NewPicklist(
                    "multiSelect",
                    "MultiSelect",
                    new OptionMetadataModel[]
                    {
                        new OptionMetadataModel
                        {
                            Label = "Eight",
                            Value = 8
                        },
                        new OptionMetadataModel
                        {
                            Label = "Nine",
                            Value = 9
                        },
                        new OptionMetadataModel
                        {
                            Label = "Ten",
                            Value = 10
                        }
                    },
                    typeName: AttributeTypeDisplayName.MultiSelectPicklistType,
                    attributeType: AttributeTypeCode.Virtual),
                AttributeMetadataModel.NewImage("image", "Image"),
                AttributeMetadataModel.NewFile("file", "File")
            },
            ManyToOneRelationships = new OneToManyRelationshipMetadataModel[]
            {
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "tripleremoteid",
                    ReferencedEntity = "tripleremote",
                    ReferencingAttribute = "new_lookup",
                    ReferencingEntity = "allattributes",
                    ReferencedEntityNavigationPropertyName = "lookup_refd",
                    ReferencingEntityNavigationPropertyName = "lookup",
                    SchemaName = "all_tripleremote"
                },
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "allid",
                    ReferencedEntity = "allattributes",
                    ReferencingAttribute = "selfid",
                    ReferencingEntity = "allattributes",
                    ReferencedEntityNavigationPropertyName = "self_refd",
                    ReferencingEntityNavigationPropertyName = "self",
                    SchemaName = "self"
                },
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "ownerid",
                    ReferencedEntity = "tripleremote",
                    ReferencingAttribute = "ownerid",
                    ReferencingEntity = "allattributes",
                    ReferencedEntityNavigationPropertyName = "owner_allattributes",
                    ReferencingEntityNavigationPropertyName = "ownerid",
                    SchemaName = "owner_allattributes"
                },
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "accountid",
                    ReferencedEntity = "tripleremote",
                    ReferencingAttribute = "customerid",
                    ReferencingEntity = "allattributes",
                    ReferencedEntityNavigationPropertyName = "allattributes_customer_account",
                    ReferencingEntityNavigationPropertyName = "customerid_account",
                    SchemaName = "allattributes_customer_account"
                },
            }
        };

        public static readonly EntityMetadataModel TestAttributeModel = new EntityMetadataModel
        {
            LogicalName = "allattributes",
            DisplayCollectionName = "All Attributes",
            PrimaryIdAttribute = "allid",
            Attributes = new AttributeMetadataModel[]
            {
                AttributeMetadataModel.NewDecimal("new_field", "field"),
                AttributeMetadataModel.NewLookup("new_testlookup", "TestLookup", new [] { "tripleremote" })
            },
            ManyToOneRelationships = new OneToManyRelationshipMetadataModel[]
            {
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "tripleremoteid",
                    ReferencedEntity = "tripleremote",
                    ReferencingAttribute = "new_testlookup",
                    ReferencingEntity = "allattributes",
                    ReferencedEntityNavigationPropertyName = "lookup_refd",
                    ReferencingEntityNavigationPropertyName = "testLookupNavName",
                    SchemaName = "all_tripleremote"
                }
            }
        };

        /// <summary>
        /// Array with multiple models pre-loaded.
        /// </summary>
        public static readonly EntityMetadataModel[] AllAttributeModels = new EntityMetadataModel[] { AllAttributeModel, TripleRemoteModel };

        public static readonly EntityMetadataModel Account = new EntityMetadataModel
        {
            LogicalName = "account",
            DisplayCollectionName = "Accounts",
            PrimaryIdAttribute = "accountid",
            Attributes = new AttributeMetadataModel[]
            {
                AttributeMetadataModel.NewLookup("new_tasklookup", "TaskLookup", new [] { "task" }),
                AttributeMetadataModel.NewGuid("accountid", "AccountId"),
            },
            ManyToOneRelationships = new OneToManyRelationshipMetadataModel[]
            {
                new OneToManyRelationshipMetadataModel
                {
                    ReferencedAttribute = "activitypointerid",
                    ReferencedEntity = "task",
                    ReferencingAttribute = "new_tasklookup",
                    ReferencingEntity = "account",
                    ReferencedEntityNavigationPropertyName = "tasklookup_refd",
                    ReferencingEntityNavigationPropertyName = "tasklookup",
                    SchemaName = "account_task"
                }
            }
        };

        public static readonly EntityMetadataModel Task = new EntityMetadataModel
        {
            LogicalName = "task",
            DisplayCollectionName = "Tasks",
            PrimaryIdAttribute = "activitypointerid",
            Attributes = new AttributeMetadataModel[]
            {
                AttributeMetadataModel.NewDecimal("subject", "Subject"),
                AttributeMetadataModel.NewGuid("activitypointerid", "ActivitypointerId"),
                AttributeMetadataModel.NewDecimal("fieldnotstoredonprimarytable", "FieldNotStoredOnPrimaryTable"),
                AttributeMetadataModel.NewDecimal("category", "Category")
            }
        };

        public static readonly EntityMetadataModel[] TestAllAttributeModels = new EntityMetadataModel[] { Account, Task};

        public static readonly List<OptionSetMetadata> GlobalOptionSets = new()
        {
            new OptionSetMetadata(new OptionMetadataCollection(new List<OptionMetadata>(
                new OptionMetadata[]
                {
                    new OptionMetadata { Label = new Label(new LocalizedLabel("One", 1033), new LocalizedLabel[0]), Value = 1 },
                    new OptionMetadata { Label = new Label(new LocalizedLabel("Two", 1033), new LocalizedLabel[0]), Value = 2 },
                }
            )))
            {
                IsGlobal = true,
                Name = "global1",
                DisplayName = new Label(new LocalizedLabel("Global1", 1033), new LocalizedLabel[0]),
                MetadataId = Guid.NewGuid()
            },
            new OptionSetMetadata(new OptionMetadataCollection(new List<OptionMetadata>(
                new OptionMetadata[]
                {
                    new OptionMetadata { Label = new Label(new LocalizedLabel("Three", 1033), new LocalizedLabel[0]), Value = 1 },
                    new OptionMetadata { Label = new Label(new LocalizedLabel("Four", 1033), new LocalizedLabel[0]), Value = 2 },
                }
            )))
            {
                IsGlobal = true,
                Name = "global2",
                DisplayName = new Label(new LocalizedLabel("Global2", 1033), new LocalizedLabel[0]),
                MetadataId = Guid.NewGuid()
            }
        };
    }
}
