﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
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

            private readonly Func<Guid> _aliasGUIDSuffixProvider;

            // aliasGUIDSuffixProvider should only be provided to make test deterministic.
            internal DelegationHooksImpl(Func<Guid> aliasGUIDSuffixProvider = null)
            {
                if (aliasGUIDSuffixProvider == null)
                {
                    _aliasGUIDSuffixProvider = () => Guid.NewGuid();
                }
                else
                {
                    _aliasGUIDSuffixProvider = aliasGUIDSuffixProvider;
                }
            }

            public override async Task<DValue<RecordValue>> RetrieveAsync(TableValue table, Guid id, string partitionId, FxColumnMap columnMap, CancellationToken cancellationToken)
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
                        var filter = new FxFilterExpression();
                        filter.AddCondition(t2._entityMetadata.PrimaryIdAttribute, FxConditionOperator.Equal, id);
#pragma warning disable CS0618 // Type or member is obsolete
                        var rows = await t2.RetrieveMultipleAsync(new DataverseDelegationParameters(t2.Type.ToRecord()) { FxFilter = filter, Top = 1, ColumnMap = columnMap }, cancellationToken).ConfigureAwait(false);
#pragma warning restore CS0618 // Type or member is obsolete
                        result = rows.FirstOrDefault();
                    }
                    else
                    {
                        // If the table is elastic and partitionId is not null we need to use RetrieveAsync api to point retrieve using guid and partitionId which is faster.
                        result = await t2.RetrieveAsync(id, partitionId, columnMap, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    result = await t2.RetrieveAsync(id, columnMap, cancellationToken).ConfigureAwait(false);
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
            public override async Task<IEnumerable<DValue<RecordValue>>> RetrieveMultipleAsync(IServiceProvider services, IDelegatableTableValue table, DelegationParameters delegationParameters, TableDelegationInfo capabilities, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                delegationParameters.EnsureOnlyFeatures(table.SupportedFeatures);
                IEnumerable<DValue<RecordValue>> result = await table.GetRowsAsync(services, delegationParameters, cancellationToken).ConfigureAwait(false);

                if (result.Any(err => err.IsError))
                {
                    return result.Where(err => err.IsError);
                }

                if (table is not DataverseTableValue dvTable)
                {
                    result = MayAddAliasingWrapper(delegationParameters, capabilities, result);
                }

                return result;
            }

            private static IEnumerable<DValue<RecordValue>> MayAddAliasingWrapper(DelegationParameters delegationParameters, TableDelegationInfo capabilities, IEnumerable<DValue<RecordValue>> innerRVs)
            {
                var dp = (DataverseDelegationParameters)delegationParameters;

                if (dp?.ColumnMap?.ExistsAliasing == true
                    && !IsAliasingSupported(capabilities))
                {
                    return innerRVs.Select(innerRv => DValue<RecordValue>.Of(new AliasedRecordValue(dp.ColumnMap, (RecordType)dp.ExpectedReturnType, innerRv)));
                }

                return innerRVs;
            }

            private class AliasedRecordValue : RecordValue
            {
                private readonly FxColumnMap _fxColumns;

                private readonly DValue<RecordValue> _innerRecordValue;

                public AliasedRecordValue(FxColumnMap fxColumns, RecordType type, DValue<RecordValue> innerRecordValue)
                    : base(type)
                {
                    _fxColumns = fxColumns ?? throw new ArgumentNullException(nameof(fxColumns));
                    _innerRecordValue = innerRecordValue ?? throw new ArgumentNullException(nameof(innerRecordValue));
                }

                protected override async Task<(bool Result, FormulaValue Value)> TryGetFieldAsync(FormulaType fieldType, string fieldName, CancellationToken cancellationToken)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_fxColumns.TryGetColumnInfo(fieldName, out var columnInfo))
                    {
                        FormulaValue fieldValue;
                        if (columnInfo.AggregateMethod == SummarizeMethod.None)
                        {
                            fieldValue = await _innerRecordValue.Value.GetFieldAsync(columnInfo.RealColumnName, cancellationToken);
                        }
                        else
                        {
                            // For aggregate methods, we need to use the alias column name, since it can't have the logical name.
                            fieldValue = _innerRecordValue.Value.GetField(columnInfo.AliasColumnName);
                        }

                        if (fieldValue is not BlankValue)
                        {
                            return (true, fieldValue);
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"Report a bug! Field {fieldName} not found in FxColumnMap.");
                    }

                    return (false, null);
                }

                protected override bool TryGetField(FormulaType fieldType, string fieldName, out FormulaValue result)
                {
                    var (success, value) = TryGetFieldAsync(fieldType, fieldName, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
                    result = value;
                    return success;
                }
            }

            private static bool IsAliasingSupported(TableDelegationInfo capabilities)
            {
                if (capabilities?.ColumnAliasingCapabilities?.IsColumnAliasingSupported() == true)
                {
                    return true;
                }

                return false;
            }

            public override async Task<FormulaValue> ExecuteQueryAsync(IServiceProvider services, IDelegatableTableValue table, DelegationParameters delegationParameters, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                delegationParameters.EnsureOnlyFeatures(table.SupportedFeatures);
                var count = await table.ExecuteQueryAsync(services, delegationParameters, cancellationToken);

                // if returned type is not number or decimal, throw exception.
                if (count.Type != ((DataverseDelegationParameters)delegationParameters).ExpectedReturnType)
                {
                    throw new InvalidOperationException($"Expected return type is {((DataverseDelegationParameters)delegationParameters).ExpectedReturnType} but received {count.Type} from {nameof(table.ExecuteQueryAsync)}");
                }

                return count;
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

            internal override FxJoinNode RetrieveManyToOneRelation(TableValue table, string link)
            {
                var dvTable = (DataverseTableValue)table;

                var relationMetadata = DelegationUtility.DeserializeRelatioMetadata(link);

                OneToManyRelationshipMetadata relation;
                if (relationMetadata.isPolymorphic)
                {
                    dvTable._entityMetadata.TryGetManyToOneRelationship(relationMetadata.ReferencingFieldName, relationMetadata.ReferencedEntityName, out relation);
                }
                else
                {
                    dvTable._entityMetadata.TryGetManyToOneRelationship(relationMetadata.ReferencingFieldName, out relation);
                }

                // Aliasing entity is important to avoid collision with the main entity when it comes to self join.
                // Add '_N1' (LinkEntityN1RelationSuffix) suffix to identify relation as N-1 relationship
                var foreignEntityAlias = relation.ReferencedEntity + "_" + _aliasGUIDSuffixProvider.Invoke().ToString("N") + DelegationEngineExtensions.LinkEntityN1RelationSuffix;

                var joinNode = new FxJoinNode(
                    sourceTable: relation.ReferencingEntity,
                    foreignTable: relation.ReferencedEntity,
                    fromAttribute: relation.ReferencingAttribute,
                    toAttribute: relation.ReferencedAttribute,
                    joinType: FxJoinType.Left,
                    foreignTableAlias: foreignEntityAlias,
                    rightMap: new FxColumnMap(dvTable.Type.ToRecord()));

                return joinNode;
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

        [Obsolete("Only for test purposes")]
        internal static void EnableTestDelegation(this Engine engine, int maxRows)
        {
            engine.EnableDelegationCore(new DelegationHooksImpl(() => new Guid("382c74c3-721d-4f34-80e5-57657b6cbc27")), maxRows);
        }
    }
}
