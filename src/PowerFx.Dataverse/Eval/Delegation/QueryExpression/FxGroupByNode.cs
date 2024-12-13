// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression
{
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

    public class FxAggregateExpression
    {
        private readonly string _propertyName;

        public string PropertyName => _propertyName;

        private readonly FxAggregateType _aggregateType;

        public FxAggregateType AggregateType => _aggregateType;

        private readonly string _alias;

        public string Alias => _alias;

        public FxAggregateExpression(string propertyName, FxAggregateType aggregateType, string alias)
        {
            _propertyName = propertyName;
            _aggregateType = aggregateType;
            _alias = alias;
        }
    }

    public enum FxAggregateType
    {
        None,
        Sum,
        Average,
        Min,
        Max,
        Count
    }
}
