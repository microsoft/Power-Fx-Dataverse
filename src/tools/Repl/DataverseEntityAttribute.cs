// <copyright file="Program.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Text;

namespace Repl
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
