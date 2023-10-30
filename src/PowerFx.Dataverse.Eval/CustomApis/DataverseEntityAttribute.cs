//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;

namespace Microsoft.PowerFx.Dataverse
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field)]
    public class DataverseEntityAttribute : Attribute
    {
        public string LogicalName { get; private set; }
        public DataverseEntityAttribute(string name)
        {
            this.LogicalName = name;
        }
    }
}
