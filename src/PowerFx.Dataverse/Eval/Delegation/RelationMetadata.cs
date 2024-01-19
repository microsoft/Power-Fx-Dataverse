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
        public string FieldName { get; }

        public bool isPolymorphic { get; }

        public string TargetEntityName { get; }

        public RelationMetadata(string fieldName, bool isPolymorphic, string targetEntityName)
        {
            FieldName = fieldName;
            this.isPolymorphic = isPolymorphic;
            TargetEntityName = targetEntityName;
        }
    }
}
