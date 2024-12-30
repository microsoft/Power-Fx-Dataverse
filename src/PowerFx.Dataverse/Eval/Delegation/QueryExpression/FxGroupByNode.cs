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

        private readonly IEnumerable<FxAggregateExpression> _fxAggregateExpressions;

        public IEnumerable<FxAggregateExpression> FxAggregateExpressions => _fxAggregateExpressions;

        public FxGroupByNode(IEnumerable<string> groupingProperties, IEnumerable<FxAggregateExpression> fxAggregateExpressions)
        {
            _groupingProperties = groupingProperties;
            _fxAggregateExpressions = fxAggregateExpressions;
        }
    }

    [Obsolete("preview")]
    public class FxAggregateExpression
    {
        private readonly string _propertyName;

        public string PropertyName => _propertyName;

        private readonly SummarizeMethod _aggregateMethod;

        public SummarizeMethod AggregateMethod => _aggregateMethod;

        private readonly string _alias;

        public string Alias => _alias;

        public FxAggregateExpression(string propertyName, SummarizeMethod aggregateMethod, string alias)
        {
            _propertyName = propertyName;
            _aggregateMethod = aggregateMethod;
            _alias = alias;
        }
    }
}
