using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Microsoft.PowerFx.Syntax;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation
{
    internal class DelegationUtility
    {

        public static RelationMetadata DeserializeRelatioMetadata(string serializedMetadata)
        {
            var relationshipObj = JsonSerializer.Deserialize<RelationMetadata>(serializedMetadata, DelegationIRVisitor._options);
            return relationshipObj;
        }

        public static string SerializeRelationMetadata(RelationMetadata metadata)
        {
            var serializedMetadata = JsonSerializer.Serialize(metadata, DelegationIRVisitor._options);
            return serializedMetadata;
        }
    }
}
