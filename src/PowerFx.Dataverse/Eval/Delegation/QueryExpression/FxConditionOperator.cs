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
        Equal,

        /// <summary>
        /// The two values are not equal. Value = 1.
        /// </summary>
        NotEqual,

        /// <summary>
        /// The value is greater than the compared value. Value = 2.
        /// </summary>
        GreaterThan,

        /// <summary>
        /// The value is less than the compared value. Value = 3.
        /// </summary>
        LessThan,

        /// <summary>
        /// The value is greater than or equal to the compared value. Value = 4.
        /// </summary>
        GreaterEqual,

        /// <summary>
        /// The value is less than or equal to the compared value. Value = 5.
        /// </summary>
        LessEqual,

        /// <summary>
        /// The character string is matched to the specified pattern. Value = 6.
        /// </summary>
        Like,

        /// <summary>
        /// The character string does not match the specified pattern. Value = 7.
        /// </summary>
        NotLike,

        /// <summary>
        /// The value exists in a list of values. Value = 8.
        /// </summary>
        In,

        /// <summary>
        /// The given value is not matched to a value in a subquery or a list. Value = 9.
        /// </summary>
        NotIn,

        /// <summary>
        /// The value is between two values. Value = 10.
        /// </summary>
        Between,

        /// <summary>
        /// The value is not between two values. Value = 11.
        /// </summary>
        NotBetween,

        /// <summary>
        /// The value is null. Value = 12.
        /// </summary>
        Null,

        /// <summary>
        /// The value is not null. Value = 13.
        /// </summary>
        NotNull,

        /// <summary>
        /// The value equals yesterday’s date. Value = 14.
        /// </summary>
        Yesterday,

        /// <summary>
        /// The value equals today’s date. Value = 15.
        /// </summary>
        Today,

        /// <summary>
        /// The value equals tomorrow’s date. Value = 16.
        /// </summary>
        Tomorrow,

        /// <summary>
        /// The value is within the last seven days including today. Value = 17.
        /// </summary>
        Last7Days,

        /// <summary>
        /// The value is within the next seven days. Value = 18.
        /// </summary>
        Next7Days,

        /// <summary>
        /// The value is within the previous week including Sunday through Saturday. Value = 19.
        /// </summary>
        LastWeek,

        /// <summary>
        /// The value is within the current week. Value = 20.
        /// </summary>
        ThisWeek,

        /// <summary>
        /// The value is within the next week. Value = 21.
        /// </summary>
        NextWeek,

        /// <summary>
        /// The value is within the last month including first day of the last month and last day of the last month. Value = 22.
        /// </summary>
        LastMonth,

        /// <summary>
        /// The value is within the current month. Value = 23.
        /// </summary>
        ThisMonth,

        /// <summary>
        /// The value is within the next month. Value = 24.
        /// </summary>
        NextMonth,

        /// <summary>
        /// The value is on a specified date. Value = 25.
        /// </summary>
        On,

        /// <summary>
        /// The value is on or before a specified date. Value = 26.
        /// </summary>
        OnOrBefore,

        /// <summary>
        /// The value is on or after a specified date. Value = 27.
        /// </summary>
        OnOrAfter,

        /// <summary>
        /// The value is within the previous year. Value = 28.
        /// </summary>
        LastYear,

        /// <summary>
        /// The value is within the current year. Value = 29.
        /// </summary>
        ThisYear,

        /// <summary>
        /// The value is within the next year. Value = 30.
        /// </summary>
        NextYear,

        /// <summary>
        /// The value is within the last X hours. Value = 31.
        /// </summary>
        LastXHours,

        /// <summary>
        /// The value is within the next X (specified value) hours. Value = 32.
        /// </summary>
        NextXHours,

        /// <summary>
        /// The value is within last X days. Value = 33.
        /// </summary>
        LastXDays,

        /// <summary>
        /// The value is within the next X (specified value) days. Value = 34.
        /// </summary>
        NextXDays,

        /// <summary>
        /// The value is within the last X (specified value) weeks. Value = 35.
        /// </summary>
        LastXWeeks,

        /// <summary>
        /// The value is within the next X weeks. Value = 36.
        /// </summary>
        NextXWeeks,

        /// <summary>
        /// The value is within the last X (specified value) months. Value = 37.
        /// </summary>
        LastXMonths,

        /// <summary>
        /// The value is within the next X (specified value) months. Value = 38.
        /// </summary>
        NextXMonths,

        /// <summary>
        /// The value is within the last X years. Value = 39.
        /// </summary>
        LastXYears,

        /// <summary>
        /// The value is within the next X years. Value = 40.
        /// </summary>
        NextXYears,

        /// <summary>
        /// The value is equal to the specified user ID. Value = 41.
        /// </summary>
        EqualUserId,

        /// <summary>
        /// The value is not equal to the specified user ID. Value = 42.
        /// </summary>
        NotEqualUserId,

        /// <summary>
        /// The value is equal to the specified business ID. Value = 43.
        /// </summary>
        EqualBusinessId,

        /// <summary>
        /// The value is not equal to the specified business ID. Value = 44.
        /// </summary>
        NotEqualBusinessId,

        /// <summary>
        /// For internal use only. Value = 45.
        /// </summary>
        ChildOf,

        /// <summary>
        /// The value is found in the specified bit-mask value. Value = 46.
        /// </summary>
        Mask,

        /// <summary>
        /// The value is not found in the specified bit-mask value. Value = 47.
        /// </summary>
        NotMask,

        /// <summary>
        /// For internal use only. Value = 48.
        /// </summary>
        MasksSelect,

        /// <summary>
        /// The string contains another string. Value = 49.
        /// </summary>
        Contains,

        /// <summary>
        /// The string does not contain another string. Value = 50.
        /// </summary>
        DoesNotContain,

        /// <summary>
        /// The value is equal to the language for the user. Value = 51.
        /// </summary>
        EqualUserLanguage,

        /// <summary>
        /// For internal use only. Value = 52.
        /// </summary>
        NotOn,

        /// <summary>
        /// The value is older than the specified number of months. Value = 53.
        /// </summary>
        OlderThanXMonths,

        /// <summary>
        /// The string occurs at the beginning of another string. Value = 54.
        /// </summary>
        BeginsWith,

        /// <summary>
        /// The string does not begin with another string. Value = 55.
        /// </summary>
        DoesNotBeginWith,

        /// <summary>
        /// The string ends with another string. Value = 56.
        /// </summary>
        EndsWith,

        /// <summary>
        /// The string does not end with another string. Value = 57.
        /// </summary>
        DoesNotEndWith,

        /// <summary>
        /// The value is within the current fiscal year. Value = 58.
        /// </summary>
        ThisFiscalYear,

        /// <summary>
        /// The value is within the current fiscal period. Value = 59.
        /// </summary>
        ThisFiscalPeriod,

        /// <summary>
        /// The value is within the next fiscal year. Value = 60.
        /// </summary>
        NextFiscalYear,

        /// <summary>
        /// The value is within the next fiscal period. Value = 61.
        /// </summary>
        NextFiscalPeriod,

        /// <summary>
        /// The value is within the last fiscal year. Value = 62.
        /// </summary>
        LastFiscalYear,

        /// <summary>
        /// The value is within the last fiscal period. Value = 63.
        /// </summary>
        LastFiscalPeriod,

        /// <summary>
        /// The value is within the last X (specified value) fiscal periods. Value = 64.
        /// </summary>
        LastXFiscalYears,

        /// <summary>
        /// The value is within the next X fiscal periods. Value =
    }
}
