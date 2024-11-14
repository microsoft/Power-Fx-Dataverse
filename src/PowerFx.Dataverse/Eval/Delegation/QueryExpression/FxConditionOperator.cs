// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression
{
    internal enum FxConditionOperator
    {
        /// <summary>
        /// The values are compared for equality. Value = 0.
        /// </summary>
        Equal = 0,

        /// <summary>
        /// The two values are not equal. Value = 1.
        /// </summary>
        NotEqual = 1,

        /// <summary>
        /// The value is greater than the compared value. Value = 2.
        /// </summary>
        GreaterThan = 2,

        /// <summary>
        /// The value is less than the compared value. Value = 3.
        /// </summary>
        LessThan = 3,

        /// <summary>
        /// The value is greater than or equal to the compared value. Value = 4.
        /// </summary>
        GreaterEqual = 4,

        /// <summary>
        /// The value is less than or equal to the compared value. Value = 5.
        /// </summary>
        LessEqual = 5,

        /// <summary>
        /// The character string is matched to the specified pattern. Value = 6.
        /// </summary>
        Like = 6,

        /// <summary>
        /// The character string does not match the specified pattern. Value = 7.
        /// </summary>
        NotLike = 7,

        /// <summary>
        /// The value exists in a list of values. Value = 8.
        /// </summary>
        In = 8,

        /// <summary>
        /// The given value is not matched to a value in a subquery or a list. Value = 9.
        /// </summary>
        NotIn = 9,

        /// <summary>
        /// The value is between two values. Value = 10.
        /// </summary>
        Between = 10,

        /// <summary>
        /// The value is not between two values. Value = 11.
        /// </summary>
        NotBetween = 11,

        /// <summary>
        /// The value is null. Value = 12.
        /// </summary>
        Null = 12,

        /// <summary>
        /// The value is not null. Value = 13.
        /// </summary>
        NotNull = 13,

        /// <summary>
        /// The string contains another string. Value = 14.
        /// </summary>
        Contains = 14,

        /// <summary>
        /// The string does not contain another string. Value = 15.
        /// </summary>
        DoesNotContain = 15,

        /// <summary>
        /// The string occurs at the beginning of another string. Value = 16.
        /// </summary>
        BeginsWith = 16,

        /// <summary>
        /// The string does not begin with another string. Value = 17.
        /// </summary>
        DoesNotBeginWith = 17,

        /// <summary>
        /// The string ends with another string. Value = 18.
        /// </summary>
        EndsWith = 18,

        /// <summary>
        /// The string does not end with another string. Value = 19.
        /// </summary>
        DoesNotEndWith = 19,
    }
}
