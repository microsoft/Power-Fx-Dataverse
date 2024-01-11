//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    public static class EngineExtensions
    {
        // Provides adapter for Dataverse project to call back into Dataverse.Eval types, like DataverseTableValue.
        private class DelegationHooksImpl : DelegationHooks
        {
            public override int DefaultMaxRows => DataverseConnection.DefaultMaxRows;

            public override async Task<DValue<RecordValue>> RetrieveAsync(TableValue table, Guid id, IEnumerable<string> columns, CancellationToken cancel)
            {
                // Binder should have enforced that this always succeeds.
                var t2 = (DataverseTableValue)table;

                var result = await t2.RetrieveAsync(id, columns, cancel).ConfigureAwait(false);
                return result;
            }

            public override async Task<IEnumerable<DValue<RecordValue>>> RetrieveMultipleAsync(TableValue table, ISet<LinkEntity> relation, FilterExpression filter, int? count, IEnumerable<string> columnSet, CancellationToken cancellationToken)
            {
                var t2 = (DataverseTableValue)table;
                var result = await t2.RetrieveMultipleAsync(filter, relation, count, columnSet, cancellationToken).ConfigureAwait(false);
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

                dvTable._entityMetadata.TryGetManyToOneRelationship(links.First(), out var relation);

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
