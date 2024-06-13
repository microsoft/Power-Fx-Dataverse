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
    /// Lookup a Custom API signature from dataverse
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
        /// <exception cref="InvalidOperationException"></exception>
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

        public static (CultureInfo, TimeZoneInfo) GetUserLocaleTimeZoneSettings(this IDataverseReader reader)
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

            var currentUserSettings = reader.RetrieveMultipleAsync(query, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult().Response.Entities.First().ToObject<UserSettingsEntity>();

            var localeId = currentUserSettings.localeid;
            var culture = new CultureInfo(localeId);

            var timeZoneCode = currentUserSettings.timezonecode;
            var timezoneQuery = new QueryExpression("timezonedefinition")
            {
                ColumnSet = new ColumnSet("standardname")
            };
            timezoneQuery.Criteria.AddCondition("timezonecode", ConditionOperator.Equal, timeZoneCode);
            var timeZoneDef = reader.RetrieveMultipleAsync(query).ConfigureAwait(false).GetAwaiter().GetResult().Response;
            var windowsTimeZoneId = timeZoneDef.Entities[0].Attributes["standardname"].ToString();
            var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(windowsTimeZoneId);

            return (culture, timeZoneInfo);
        }
    }
}
