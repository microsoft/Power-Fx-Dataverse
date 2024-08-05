// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    public static class EngineExtensions
    {
        // Provides adapter for Dataverse project to call back into Dataverse.Eval types, like DataverseTableValue.
        private class DelegationHooksImpl : DelegationHooks
        {
            public override int DefaultMaxRows => DataverseConnection.DefaultMaxRows;

            public override async Task<DValue<RecordValue>> RetrieveAsync(TableValue table, Guid id, string partitionId, IEnumerable<string> columns, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Binder should have enforced that this always succeeds.
                var t2 = (DataverseTableValue)table;

                DValue<RecordValue> result;

                // Elastic tables need to be handled differently.
                if (t2._entityMetadata.IsElasticTable())
                {
                    // If the table is elastic and partitionId is null we need to use RetrieveMultipleAsync api to retrieve using guid.
                    if (partitionId == null)
                    {
                        var filter = new FilterExpression();
                        filter.AddCondition(t2._entityMetadata.PrimaryIdAttribute, ConditionOperator.Equal, id);
#pragma warning disable CS0618 // Type or member is obsolete
                        var rows = await t2.RetrieveMultipleAsync(new DataverseDelegationParameters() { Filter = filter, Top = 1, ColumnMap = ColumnMap.GetColumnMap(columns) }, cancellationToken).ConfigureAwait(false);
#pragma warning restore CS0618 // Type or member is obsolete
                        result = rows.FirstOrDefault();
                    }
                    else
                    {
                        // If the table is elastic and partitionId is not null we need to use RetrieveAsync api to point retrieve using guid and partitionId which is faster.
                        result = await t2.RetrieveAsync(id, partitionId, columns, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    result = await t2.RetrieveAsync(id, columns, cancellationToken).ConfigureAwait(false);
                }

                return result;
            }

            /// <summary>
            /// Retrieves multiple records from the dataverse table.
            /// </summary>
            /// <param name="table">Source table.</param>
            /// <param name="relation">Set of relation needed to fetch.</param>
            /// <param name="filter">Filter that will be applied in query.</param>
            /// <param name="partitionId">Provide partitionId in case of elastic table. else keep it null.</param>
            /// <param name="count">count of record that will be fetched from top.</param>
            /// <param name="columnSet">Fetches only provided columns. If kept null, fetches all the columns.</param>
            /// <param name="isDistinct">Decides if Distinct needs be applied.</param>
            /// <param name="cancellationToken"></param>
            /// <returns></returns>
            public override async Task<IEnumerable<DValue<RecordValue>>> RetrieveMultipleAsync(IServiceProvider services, IDelegatableTableValue table, DelegationParameters delegationParameters, CancellationToken cancellationToken)
            {                
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyCollection<DValue<RecordValue>> result = await table.GetRowsAsync(services, delegationParameters, cancellationToken).ConfigureAwait(false);
                
                IReadOnlyDictionary<string, string> columnMap = ((DataverseDelegationParameters)delegationParameters).ColumnMap?.AsStringDictionary();

                if (columnMap != null && result.Any())
                {
                    RecordType recordType = ColumnMapRecordValue.ApplyMap(result.First().Value.Type, columnMap);
                    List<DValue<RecordValue>> list = new List<DValue<RecordValue>>();

                    foreach (DValue<RecordValue> record in result)
                    {
                        list.Add(DValue<RecordValue>.Of(new ColumnMapRecordValue(record.Value, recordType, columnMap)));
                    }

                    result = list;
                }

                return result;
            }

            // This gets back the attribute in a way that is strictly typed to table's underlying datasources's fieldName's type.
            public override object RetrieveAttribute(TableValue table, string fieldName, FormulaValue value)
            {
                // Binder should have enforced that this always succeeds.
                if (table is DataverseTableValue t2)
                {
                    if (t2._entityMetadata.TryGetAttribute(fieldName, out var amd))
                    {
                        return amd.ToAttributeObject(value, true);
                    }

                    throw new Exception($"Field {fieldName} not found on table {t2._entityMetadata.DisplayName}");
                }

                // We don't have any strong type information.
                if (value is OptionSetValue osv)
                {
                    // Workaround for https://github.com/microsoft/Power-Fx/issues/2403
                    // For delegation, Option set should return execution value.
                    // ToObject() / TryGetPrimitiveValue() will return display name.
                    return osv.Option;
                }

                if (value.TryGetPrimitiveValue(out var primitiveValue))
                {
                    return primitiveValue;
                }

                // Binder should ensure we never get here.
                throw new Exception($"Expected primitive for field {fieldName}, type={value.Type}.");
            }

            internal override object RetrieveRelationAttribute(TableValue table, LinkEntity relation, string field, FormulaValue value)
            {
                var t2 = (DataverseTableValue)table;
                var metadata = t2.Connection.GetMetadataOrThrow(relation.LinkToEntityName);
                if (metadata.TryGetAttribute(field, out var amd))
                {
                    return amd.ToAttributeObject(value, true);
                }

                throw new NotImplementedException();
            }

            public override bool IsDelegableSymbolTable(ReadOnlySymbolTable symbolTable)
            {
                bool isRealTable =
                    symbolTable.DebugName == SingleOrgPolicy.SymTableName ||
                    symbolTable.DebugName == DVSymbolTable.SymTableName ||

                    // https://github.com/microsoft/Power-Fx-Dataverse/issues/478
                    symbolTable.DebugName.StartsWith("Delegable_");

                return isRealTable;
            }

            internal override LinkEntity RetrieveManyToOneRelation(TableValue table, IEnumerable<string> links)
            {
                var dvTable = (DataverseTableValue)table;

                var relationMetadata = DelegationUtility.DeserializeRelatioMetadata(links.First());

                OneToManyRelationshipMetadata relation;
                if (relationMetadata.isPolymorphic)
                {
                    dvTable._entityMetadata.TryGetManyToOneRelationship(relationMetadata.ReferencingFieldName, relationMetadata.ReferencedEntityName, out relation);
                }
                else
                {
                    dvTable._entityMetadata.TryGetManyToOneRelationship(relationMetadata.ReferencingFieldName, out relation);
                }

                var linkEntity = new LinkEntity()
                {
                    Columns = new ColumnSet(true),
                    LinkFromEntityName = relation.ReferencingEntity,
                    LinkToEntityName = relation.ReferencedEntity,
                    LinkFromAttributeName = relation.ReferencingAttribute,
                    LinkToAttributeName = relation.ReferencedAttribute,

                    // Aliasing entity is important to avoid collision with the main entity when it comes to self join.
                    EntityAlias = relation.ReferencedEntity + "_" + Guid.NewGuid().ToString("N"),
                    JoinOperator = JoinOperator.LeftOuter
                };

                return linkEntity;
            }
        }

        /// <summary>
        /// Public facing API to enable delegation.
        /// </summary>
        /// <param name="engine"></param>
        public static void EnableDelegation(this Engine engine)
        {
            engine.EnableDelegationCore(new DelegationHooksImpl(), 1000);
        }

        /// <summary>
        /// Public facing API to enable delegation.
        /// </summary>
        /// <param name="engine"></param>
        /// <param name="maxRows">Max number of rows delegation can handle.</param>
        public static void EnableDelegation(this Engine engine, int maxRows)
        {
            engine.EnableDelegationCore(new DelegationHooksImpl(), maxRows);
        }
    }
}
