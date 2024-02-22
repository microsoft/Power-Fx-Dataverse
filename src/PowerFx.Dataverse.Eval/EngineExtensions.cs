//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;
using static Microsoft.PowerFx.Dataverse.DelegationIRVisitor;

namespace Microsoft.PowerFx.Dataverse
{
    public static class EngineExtensions
    {
        // Provides adapter for Dataverse project to call back into Dataverse.Eval types, like DataverseTableValue.
        private class DelegationHooksImpl : DelegationHooks
        {
            public override int DefaultMaxRows => DataverseConnection.DefaultMaxRows;

            public override async Task<DValue<RecordValue>> RetrieveAsync(TableValue table, Guid id, string partitionId, IEnumerable<string> columns, CancellationToken cancel)
            {
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
                        var rows = await t2.RetrieveMultipleAsync(filter: filter, relation: null, count: 1, columnSet: columns, isDistinct: false, cancel);
                        result  = rows.FirstOrDefault();
                    }
                    else
                    {
                        // If the table is elastic and partitionId is not null we need to use RetrieveAsync api to point retrieve using guid and partitionId which is faster.
                        result = await t2.RetrieveAsync(id, partitionId, columns, cancel).ConfigureAwait(false);
                    }

                }
                else
                {
                    result = await t2.RetrieveAsync(id, columns, cancel).ConfigureAwait(false);
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
            /// <param name="isDistinct">Decides if Distinct needs be applied</param>
            /// <param name="cancellationToken"></param>
            /// <returns></returns>
            public override async Task<IEnumerable<DValue<RecordValue>>> RetrieveMultipleAsync(TableValue table, ISet<LinkEntity> relation, FilterExpression filter, string partitionId, int? count, IEnumerable<string> columnSet, bool isDistinct, CancellationToken cancellationToken)
            {
                var t2 = (DataverseTableValue)table;

                IEnumerable<DValue<RecordValue>> result;

                // Elastic tables need to be handled differently.
                if (t2._entityMetadata.IsElasticTable())
                {
                    result = await t2.RetrieveMultipleAsync(filter, relation, partitionId, count, columnSet, isDistinct, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    result = await t2.RetrieveMultipleAsync(filter, relation, count, columnSet, isDistinct, cancellationToken).ConfigureAwait(false);
                } 

                return result;
            }

            public override object RetrieveAttribute(TableValue table, string fieldName, FormulaValue value)
            {
                // Binder should have enforced that this always succeeds.
                var t2 = (DataverseTableValue)table;
                if (t2._entityMetadata.TryGetAttribute(fieldName, out var amd))
                {
                    return amd.ToAttributeObject(value, true);
                }

                throw new Exception($"Field {fieldName} not found on table {t2._entityMetadata.DisplayName}");
            }

            internal override object RetrieveRelationAttribute(TableValue table, LinkEntity relation, string field, FormulaValue value)
            {
                var t2 = (DataverseTableValue)table;
                var metadata = t2.Connection.GetMetadataOrThrow(relation.LinkToEntityName);
                if(metadata.TryGetAttribute(field, out var amd))
                {
                    return amd.ToAttributeObject(value, true);
                }

                throw new NotImplementedException();
            }

            public override bool IsDelegableSymbolTable(ReadOnlySymbolTable symbolTable)
            {
                bool isRealTable =
                    symbolTable.DebugName == SingleOrgPolicy.SymTableName ||
                    symbolTable.DebugName == DVSymbolTable.SymTableName;

                return isRealTable;
            }

            internal override LinkEntity RetreiveManyToOneRelation(TableValue table, IEnumerable<string> links)
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
