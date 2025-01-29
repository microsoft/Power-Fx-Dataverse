// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.PowerFx.Core.Entities;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression
{
    [Obsolete("preview")]
    public class FxGroupByNode
    {
        private readonly IEnumerable<string> _groupingProperties;

        public IEnumerable<string> GroupingProperties => _groupingProperties;

        public FxGroupByNode(IEnumerable<string> groupingProperties)
        {
            if (groupingProperties.IsNullOrEmpty())
            {
                throw new ArgumentNullException(nameof(groupingProperties));
            }

            _groupingProperties = groupingProperties;
        }
    }
}
