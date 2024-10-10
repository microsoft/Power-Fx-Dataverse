// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Interpreter;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Extensions for enabling Dataverse features in config.
    /// </summary>
    public static class ConfigExtensions
    {
        public static void EnableAIFunctions(this PowerFxConfig config)
        {
            config.SymbolTable.EnableAIFunctions();
        }

        /// <summary>
        /// Add AI functions (like AISummarize) to the symbol table.
        /// This will also require a runtime call to add a <see cref="IDataverseExecute"/> so these functions can make the call at runtime.
        /// </summary>
        /// <param name="symbolTable"></param>
        public static void EnableAIFunctions(this SymbolTable symbolTable)
        {
            symbolTable.AddFunction(new AISummarizeFunction());
            symbolTable.AddFunction(new AIReplyFunction());
            symbolTable.AddFunction(new AISentimentFunction());
            symbolTable.AddFunction(new AITranslateFunction());
            symbolTable.AddFunction(new AITranslateFunctionWithLanguage());
            symbolTable.AddFunction(new AIClassifyFunction());
            symbolTable.AddFunction(new AIExtractFunction());
            symbolTable.AddFunction(new AISummarizeRecordFunction());
        }

        public static void AddDataverseExecute(this RuntimeConfig config, IOrganizationService client)
        {
            var clientExecute = new DataverseService(client);
            config.AddDataverseExecute(clientExecute);
        }

        /// <summary>
        /// Add a runtime service for calling Dataverse messages.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="clientExecute"></param>
        public static void AddDataverseExecute(this RuntimeConfig config, IDataverseExecute clientExecute)
        {
            config.AddService<IDataverseExecute>(clientExecute);
        }

        public static async Task<RecordValue> GetEnvironmentVariablesAsync(this IDataverseReader reader)
        {
            var filter = new FilterExpression();

            // Data source and Secret types are not supported.
            filter.FilterOperator = LogicalOperator.Or;
            filter.AddCondition("type", ConditionOperator.Equal, (int)EnvironmentVariableType.Decimal);
            filter.AddCondition("type", ConditionOperator.Equal, (int)EnvironmentVariableType.String);
            filter.AddCondition("type", ConditionOperator.Equal, (int)EnvironmentVariableType.JSON);
            filter.AddCondition("type", ConditionOperator.Equal, (int)EnvironmentVariableType.Boolean);

            var definitions = await reader.RetrieveMultipleAsync<EnvironmentVariableDefinitionEntity>(filter, CancellationToken.None);
            var logicalToDisplayNames = new List<KeyValuePair<string, string>>();

            foreach (var definition in definitions)
            {
                logicalToDisplayNames.Add(new KeyValuePair<string, string>(definition.schemaname, definition.displayname));
            }

            return new DataverseEnvironmentVariablesRecordValue(
                new DataverseEnvironmentVariablesRecordType(DisplayNameUtility.MakeUnique(logicalToDisplayNames), definitions), reader);
        }
    }
}
