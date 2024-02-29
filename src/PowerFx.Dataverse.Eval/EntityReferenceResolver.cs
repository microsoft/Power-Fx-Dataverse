using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

namespace Microsoft.PowerFx.Dataverse
{
    internal class EntityReferenceResolver
    {
        private readonly IConnectionValueContext _connection;

        public EntityReferenceResolver(IConnectionValueContext connection)
        {
            _connection = connection;
        }

        public async Task<DValue<RecordValue>> ResolveEntityReferenceAsync(EntityReference reference, FormulaType fieldType, IEnumerable<string> columns, CancellationToken cancellationToken)
        {
            var entityMetadata = _connection.GetMetadataOrThrow(reference.LogicalName);
            DataverseResponse result;
            if (entityMetadata.IsElasticTable())
            {
                result = await RetrieveEntityFromElasticTableAsync(reference, columns, cancellationToken);
            }
            else
            {
                result = await RetrieveEntityAsync(reference, columns, cancellationToken);
            }

            if (result.HasError)
            {
                return result.DValueError(nameof(ResolveEntityReferenceAsync));
            }

            if (fieldType is not RecordType)
            {
                // Polymorphic case.
                fieldType = RecordType.Polymorphic();
            }

            Entity resultEntity;
            if (result is DataverseResponse<Entity> dvr)
            {
                resultEntity = dvr.Response;
            }
            else if(result is DataverseResponse<EntityCollection> dvrE)
            {
                resultEntity = dvrE.Response.Entities.FirstOrDefault();
            }
            else
            {
                throw new InvalidOperationException($"Invalid response type : {result.GetType()}");
            }

            var resultRecord = new DataverseRecordValue(resultEntity, _connection.GetMetadataOrThrow(reference.LogicalName), (RecordType)fieldType, _connection);
            
            return  DValue<RecordValue>.Of(resultRecord);
        }


        private async Task<DataverseResponse> RetrieveEntityAsync(EntityReference reference, IEnumerable<string> columns, CancellationToken cancellationToken)
        {
            DataverseResponse<Entity> newEntity = await _connection.Services.RetrieveAsync(reference.LogicalName, reference.Id, columns, cancellationToken).ConfigureAwait(false);

            return newEntity;
        }

        private async Task<DataverseResponse> RetrieveEntityFromElasticTableAsync(EntityReference reference, IEnumerable<string> columns, CancellationToken cancellationToken)
        {
            var filter = new FilterExpression();
            filter.AddCondition(_connection.GetMetadataOrThrow(reference.LogicalName).PrimaryIdAttribute, ConditionOperator.Equal, reference.Id);
            var query = DataverseTableValue.CreateQueryExpression(reference.LogicalName, filter, null, 1, columns, false);
            var rows = await _connection.Services.RetrieveMultipleAsync(query, cancellationToken);
            return rows;
        }
    }

}
