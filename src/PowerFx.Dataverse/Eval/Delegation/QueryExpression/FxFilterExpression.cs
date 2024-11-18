// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression
{
    [Obsolete("preview")]
    public class FxFilterExpression
    {
        private IList<FxConditionExpression> _conditions;

        public IList<FxConditionExpression> Conditions => _conditions;

        private readonly FxFilterOperator _fxFilterOperator;

        public FxFilterOperator FilterOperator => _fxFilterOperator;

        private IList<FxFilterExpression> _filters;

        public IList<FxFilterExpression> Filters => _filters;

        public FxFilterExpression() 
            : this(FxFilterOperator.And)
        {
        }

        public FxFilterExpression(FxFilterOperator logicalOperator)
        {
            _fxFilterOperator = logicalOperator;
            _conditions = new List<FxConditionExpression>();
            _filters = new List<FxFilterExpression>();
        }

        public void AddCondition(string field, FxConditionOperator op)
        {
            _conditions.Add(new FxConditionExpression(field, op));
        }

        public void AddCondition(FxConditionExpression condition)
        {
            _conditions.Add(condition);
        }

        internal void AddCondition(string field, FxConditionOperator op, object value, FieldFunction fieldFunction = default)
        {
            _conditions.Add(new FxConditionExpression(field, op, value, fieldFunction));
        }

        internal void AddFilter(FxFilterExpression siblingFilter)
        {
            _filters.Add(siblingFilter);
        }

        // $$$ move out to seprate marshaller
        public FilterExpression GetDataverseFilterExpression()
        {
            // Create a new Dataverse FilterExpression with the appropriate logical operator (And/Or)
            FilterExpression dataverseFilter = new FilterExpression(_fxFilterOperator == FxFilterOperator.And ? LogicalOperator.And : LogicalOperator.Or);

            // Add all conditions from FxFilterExpression to the Dataverse FilterExpression
            foreach (var condition in _conditions)
            {
                // Convert FxConditionOperator to ConditionOperator if necessary
                ConditionOperator dataverseConditionOperator = DelegationUtility.ConvertToXRMConditionOperator(condition.Operator);

                var dvCondition = new ConditionExpression(condition.AttributeName, dataverseConditionOperator, condition.Values);
                dvCondition.EntityName = condition.TableName;

                // Create the ConditionExpression and add it to the Dataverse FilterExpression
                dataverseFilter.AddCondition(dvCondition);
            }

            // Add nested filters (if any) to the Dataverse FilterExpression
            foreach (var subFilter in _filters)
            {
                // Recursively call the GetDataverseFilterExpression method for child filters
                dataverseFilter.AddFilter(subFilter.GetDataverseFilterExpression());
            }

            return dataverseFilter;
        }
    }

    [Obsolete("preview")]
    public enum FxFilterOperator
    {
        And,
        Or
    }
}
