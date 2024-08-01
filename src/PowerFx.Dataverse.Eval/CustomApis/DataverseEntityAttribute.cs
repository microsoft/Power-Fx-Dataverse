// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Used to decorate pocos so that dataverse Entity marshaller can read them.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field)]
    public class DataverseEntityAttribute : Attribute
    {
        public string LogicalName { get; private set; }

        public DataverseEntityAttribute(string name)
        {
            this.LogicalName = name ?? throw new ArgumentNullException(nameof(name));
        }
    }

    /// <summary>
    /// Used to decorate pocos so that dataverse Entity marshaller can read them.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field)]
    public class DataverseEntityPrimaryIdAttribute : Attribute
    {
        public string PrimeryIdFieldName { get; private set; }

        public DataverseEntityPrimaryIdAttribute(string name)
        {
            this.PrimeryIdFieldName = name ?? throw new ArgumentNullException(nameof(name));
        }
    }
}
