using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.OpenApi.Models;
using Microsoft.PowerFx.Connectors;
using Microsoft.Xrm.Sdk;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Extensions for enabling Dataverse features in config.
    /// </summary>
    public static class ConfigExtensions
    {
        public static void EnableAIFunctions(this PowerFxConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            config.SymbolTable.EnableAIFunctions();
        }

        /// <summary>
        /// Add AI functions (like AISummarize) to the symbol table. 
        /// This will also require a runtime call to add a <see cref="IDataverseExecute"/> so these functions can make the call at runtime. 
        /// </summary>
        /// <param name="symbolTable"></param>
        public static void EnableAIFunctions(this SymbolTable symbolTable)
        {
            if (symbolTable == null)
            {
                throw new ArgumentNullException(nameof(symbolTable));
            }

            symbolTable.AddFunction(new AISummarizeFunction());
            symbolTable.AddFunction(new AIReplyFunction());
            symbolTable.AddFunction(new AISentimentFunction());
            symbolTable.AddFunction(new AITranslateFunction());
            symbolTable.AddFunction(new AIClassifyFunction());
            symbolTable.AddFunction(new AIExtractFunction());
            symbolTable.AddFunction(new AISummarizeRecordFunction());
        }

        public static void AddDataverseExecute(this RuntimeConfig config, IOrganizationService client)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

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
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            config.AddService(clientExecute);
        }

        public static async Task<RuntimeConfig> AddPlugInAsync(this RuntimeConfig config, PowerFxConfig pfxConfig, string @namespace, string plugInName, CancellationToken cancellationToken = default)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            cancellationToken.ThrowIfCancellationRequested();
            DataverseConnection dvConnection = (DataverseConnection)config.ServiceProvider.GetService(typeof(DataverseConnection)) ?? throw new ArgumentException("Cannot add a plugIn with no DataverseConnection in the config.");
            CustomApiSignature plugIn = await dvConnection.DataverseService.GetPlugInAsync(plugInName, cancellationToken).ConfigureAwait(false);

            return await config.AddPlugInAsync(pfxConfig, @namespace, plugIn, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<RuntimeConfig> AddPlugInAsync(this RuntimeConfig runtimeConfig, PowerFxConfig pfxConfig, string @namespace, CustomApiSignature plugIn, CancellationToken cancellationToken = default)
        {
            if (runtimeConfig == null)
            {
                throw new ArgumentNullException(nameof(runtimeConfig));
            }

            cancellationToken.ThrowIfCancellationRequested();
            DataverseConnection dvConnection = (DataverseConnection)runtimeConfig.ServiceProvider.GetService(typeof(DataverseConnection)) ?? throw new ArgumentException("Cannot add a plugIn with no DataverseConnection in the config.");
            dvConnection.DataverseService.ValidateHasPlugInOrThrow(plugIn);

            OpenApiDocument swagger = DataverseService.GetSwagger(plugIn);
            ConnectorFunction function = DataverseService.GetPlugInFunction(@namespace, swagger, plugIn.Api.name, plugIn.Api.uniquename);

            pfxConfig.AddFunction(function);

            BaseRuntimeConnectorContext runtimeContext = (BaseRuntimeConnectorContext)runtimeConfig.ServiceProvider.GetService(typeof(BaseRuntimeConnectorContext));

            if (runtimeContext != null && runtimeContext is not PlugInRuntimeContext)
            {
                throw new InvalidOperationException($"Adding plugins is only supported on BaseRuntimeConnectorContext of type PlugInRuntimeContext.");
            }

            PlugInRuntimeContext plugInContext = (PlugInRuntimeContext)runtimeContext;

            if (plugInContext == null)
            {
                plugInContext = new PlugInRuntimeContext(runtimeConfig, dvConnection.DataverseService);
                plugInContext.AddPlugIn(plugIn);
                runtimeConfig.AddService<BaseRuntimeConnectorContext>(plugInContext);
            }
            else
            {
                plugInContext.AddPlugIn(plugIn);
            }

            return runtimeConfig;
        }
    }
}
