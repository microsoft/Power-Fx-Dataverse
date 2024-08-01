// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

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

#pragma warning disable SA1300 // Elements should begin with uppercase letter
        public bool isPolymorphic { get; }
#pragma warning restore SA1300 // Elements should begin with uppercase letter

        public string ReferencedEntityName { get; }

        public RelationMetadata(string referencingFieldName, bool isPolymorphic, string referencedEntityName)
        {
            ReferencingFieldName = referencingFieldName;
            this.isPolymorphic = isPolymorphic;
            ReferencedEntityName = referencedEntityName;
        }
    }
}
