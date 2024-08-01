// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Lookup a Custom API signature from dataverse.
    /// </summary>
    public static class CustomApiLookupExtensions
    {
        /// <summary>
        /// Lookup an API signature given its logical name (aka uniqueName).
        /// </summary>
        /// <param name="reader">reader to access dataverse metadata, which is stored in tables like
        /// customapi, customapirequestparameter, customapiresponseproperty.</param>
        /// <param name="logicalName">logical name of the API. this will include a prefix.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>the signature object. </returns>
        /// <exception cref="InvalidOperationException">.</exception>
        /// <remarks>
        /// See description of https://learn.microsoft.com/en-us/power-apps/developer/data-platform/custom-api-tables?tabs=webapi.
        /// </remarks>
        public static async Task<CustomApiSignature> GetApiSignatureAsync(
            this IDataverseReader reader,
            string logicalName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var api = await reader.RetrieveAsync<CustomApiEntity>(
                nameof(CustomApiEntity.uniquename), logicalName, cancellationToken)
                .ConfigureAwait(false);

            var inputs = await reader.RetrieveMultipleAsync<CustomApiRequestParam>(
                nameof(CustomApiRequestParam.customapiid), api.customapiid, cancellationToken)
                .ConfigureAwait(false);

            var outputs = await reader.RetrieveMultipleAsync<CustomApiResponse>(
                nameof(CustomApiResponse.customapiid), api.customapiid, cancellationToken)
                .ConfigureAwait(false);

            var sig = new CustomApiSignature
            {
                Api = api,
                Inputs = inputs,
                Outputs = outputs
            };

            return sig;
        }

        /// <summary>
        /// Give a <see cref="CustomApiEntity"/>, lookup the parameters to get a complete <see cref="CustomApiSignature"/>.
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="api"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static async Task<CustomApiSignature> GetApiSignatureAsync(
            this IDataverseReader reader,
            CustomApiEntity api,
            CancellationToken cancellationToken = default)
        {
            return await reader.GetApiSignatureAsync(api.uniquename, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Helper to get list of lowcode plugins.
        /// There are 1000s of custom apis in an org, most are private and not interesting.
        /// LowCode plugins have the fxexpressionid column set.
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static async Task<CustomApiEntity[]> GetLowCodeApiNamesAsync(
            this IDataverseReader reader,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Lowcode plugins have the fxexpressionid set.
            FilterExpression filter = new FilterExpression();
            filter.AddCondition(nameof(CustomApiEntity.fxexpressionid), ConditionOperator.NotNull);

            var results = await reader.RetrieveMultipleAsync<CustomApiEntity>(
                               filter, cancellationToken).ConfigureAwait(false);

            return results;
        }

        /// <summary>
        /// Retrieve the locale and timezone settings for the current user. If settings are not found, returns null.
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">.</exception>
        public static async Task<(CultureInfo, TimeZoneInfo)> GetUserLocaleTimeZoneSettingsAsync(this IDataverseReader reader, CancellationToken cancellationToken = default)
        {
            var query = new QueryExpression(UserSettingsEntity.TableName)
            {
                ColumnSet = new ColumnSet(nameof(UserSettingsEntity.localeid), nameof(UserSettingsEntity.timezonecode)),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                            new ConditionExpression("systemuserid", ConditionOperator.EqualUserId)
                    }
                }
            };

            var currentUserSettingsResponse = await reader.RetrieveMultipleAsync(query, cancellationToken);

            if (currentUserSettingsResponse.Response.Entities.Count == 0)
            {
                return (null, null);
            }
            else if (currentUserSettingsResponse.Response.Entities.Count > 1)
            {
                throw new InvalidOperationException("More than one User settings found, please report a bug");
            }

            var currentUserSettings = currentUserSettingsResponse.Response.Entities.First().ToObject<UserSettingsEntity>();

            var localeId = currentUserSettings.localeid;
            var culture = new CultureInfo(localeId);

            var timeZoneCode = currentUserSettings.timezonecode;
            var timeZoneInfo = await reader.GetUserTimeZoneInfoAsync(timeZoneCode);

            return (culture, timeZoneInfo);
        }

        private static async Task<TimeZoneInfo> GetUserTimeZoneInfoAsync(this IDataverseReader reader, int timeZoneCode, CancellationToken cancellationToken = default)
        {
            var timezoneQuery = new QueryExpression("timezonedefinition")
            {
                ColumnSet = new ColumnSet("standardname")
            };

            timezoneQuery.Criteria.AddCondition("timezonecode", ConditionOperator.Equal, timeZoneCode);

            var timeZoneDefResponse = await reader.RetrieveMultipleAsync(timezoneQuery, cancellationToken);

            if (timeZoneDefResponse.Response.Entities.Count == 0)
            {
                return null;
            }
            else if (timeZoneDefResponse.Response.Entities.Count > 1)
            {
                throw new InvalidOperationException("More than one timezone definition found, please report a bug");
            }

            var timeZoneDef = timeZoneDefResponse.Response.Entities.First();
            var windowsTimeZoneId = timeZoneDef.Attributes["standardname"].ToString();
            return TimeZoneInfo.FindSystemTimeZoneById(windowsTimeZoneId);
        }
    }
}
