// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Xrm.Sdk.Query;
using static Microsoft.PowerFx.Dataverse.DelegatedOperatorFunction;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression
{
    internal class FxConditionExpression
    {
        private readonly string _attributeName;

        public string AttributeName => _attributeName;

        private readonly object[] _values;

        public object[] Values => _values;

        private readonly FxConditionOperator _fxConditionOperato;

        public FxConditionOperator Operator => _fxConditionOperato;

        private readonly IEnumerable<FieldFunction> _fieldFunctions;

        public IEnumerable<FieldFunction> FieldFunctions => _fieldFunctions;

        public string TableName;

        public FxConditionExpression(string attributeName, FxConditionOperator conditionOperator, FieldFunction fieldFunction = default)
           : this(attributeName: attributeName, conditionOperator: conditionOperator, value: Array.Empty<object>(), fieldFunctions: new[] { fieldFunction })
        {
        }

        public FxConditionExpression(string attributeName, FxConditionOperator conditionOperator, IEnumerable<FieldFunction> fieldFunctions)
            : this(attributeName, conditionOperator, Array.Empty<object>(), fieldFunctions)
        {
        }

        public FxConditionExpression(string attributeName, FxConditionOperator conditionOperator, object value, FieldFunction fieldFunction = default)
            : this(attributeName, conditionOperator, new object[] { value }, new[] { fieldFunction })
        {
        }

        public FxConditionExpression(string attributeName, FxConditionOperator conditionOperator, object value, IEnumerable<FieldFunction> fieldFunctions)
            : this(attributeName, conditionOperator, new object[] { value }, fieldFunctions)
        {
        }

        public FxConditionExpression(string attributeName, FxConditionOperator conditionOperator, object[] value, IEnumerable<FieldFunction> fieldFunctions)
        {
            _attributeName = attributeName;
            _fxConditionOperato = conditionOperator;
            _values = value;
            _fieldFunctions = fieldFunctions;
        }

        public ConditionExpression GetDataverseConditionExpression()
        {
            var condition = new ConditionExpression(AttributeName, DelegationUtility.ConvertToXRMConditionOperator(Operator), Values);
            condition.EntityName = TableName;

            return condition;
        }
    }

    /// <summary>
    /// Do not change enums values as they are used in serialization and name as it needs to match with Fx Function.
    /// </summary>
    internal enum FieldFunction
    {
        None = 0,
        StartsWith = 1,
        EndsWith = 2,
        Year = 3,
        Month = 4,
        Hour = 5,
    }
}
