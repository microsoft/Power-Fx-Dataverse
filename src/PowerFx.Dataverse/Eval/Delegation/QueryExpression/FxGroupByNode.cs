// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.PowerFx.Core.Entities;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression
{
    [Obsolete("preview")]
    public class FxGroupByNode
    {
        private readonly ISet<string> _groupingProperties;

        public ISet<string> GroupingProperties => _groupingProperties;

        public int Count => _groupingProperties.Count;

        public FxGroupByNode(IEnumerable<string> groupingProperties)
            : this(new HashSet<string>(groupingProperties))
        {
        }

        public FxGroupByNode(ISet<string> groupingProperties)
        {
            if (groupingProperties.IsNullOrEmpty())
            {
                throw new ArgumentNullException(nameof(groupingProperties));
            }

            _groupingProperties = groupingProperties;
        }

        public bool Contains(string item)
        {
            return _groupingProperties.Contains(item);
        }
    }
}
