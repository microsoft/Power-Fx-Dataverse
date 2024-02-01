using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation
{
    /// <summary>
    /// used for serializing a relation attribute.
    /// </summary>
    internal class RelationMetadata
    {
        public string ReferencingFieldName { get; }

        public bool isPolymorphic { get; }

        public string ReferencedEntityName { get; }

        public RelationMetadata(string referencingFieldName, bool isPolymorphic, string referencedEntityName)
        {
            ReferencingFieldName = referencingFieldName;
            this.isPolymorphic = isPolymorphic;
            ReferencedEntityName = referencedEntityName;
        }
    }
}
