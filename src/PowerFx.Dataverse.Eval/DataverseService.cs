//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.OpenApi.Models;
using Microsoft.PowerFx.Connectors;
using Microsoft.PowerFx.Connectors.Execution;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse
{
    public class DataverseService : IDataverseServices, IDataverseRefresh, IDataverseExecute, IDataversePlugInContext
    {
        private IOrganizationService _organizationService { get; }

        private Dictionary<string, CustomApiSignature> _plugIns { get; } = new Dictionary<string, CustomApiSignature>();

        public DataverseService(IOrganizationService service)
        {
            _organizationService = service ?? throw new ArgumentNullException(nameof(service));

            // Patch of Decimal values does not work properly with Microsoft.PowerPlatform.Dataverse.Client and UseWebApi=true. 
            // Version 1.0.23 and higher changed the default to false, but for those who have an older version
            // or have set it to true we catch it here.  We don't need to check this for NumberIsFloat operation, however,
            // we should start enforcing it there too for the day that those hosts enable Decimal.
            // https://www.nuget.org/packages/Microsoft.PowerPlatform.Dataverse.Client#release-body-tab
            if ((bool?)service.GetType().GetProperty("UseWebApi")?.GetValue(service, null) == true)
            {
                throw new ArgumentException("Use of ServiceClient with UseWebApi=true is not supported. Upgrade to a newer version of ServiceClient or set UseWebApi to false.");
            }
        }

        public virtual async Task<DataverseResponse<Entity>> RetrieveAsync(string logicalName, Guid id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => _organizationService.Retrieve(logicalName, id, new ColumnSet(true)), $"Retrieve '{logicalName}':{id}");
        }

        public virtual async Task<DataverseResponse<Guid>> CreateAsync(Entity entity, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => _organizationService.Create(entity), $"Create '{entity.LogicalName}'");
        }

        public virtual async Task<DataverseResponse> UpdateAsync(Entity entity, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => { this._organizationService.Update(entity); return true; }, $"Update '{entity.LogicalName}':{entity.Id}");
        }

        public virtual async Task<DataverseResponse<EntityCollection>> RetrieveMultipleAsync(QueryBase query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => _organizationService.RetrieveMultiple(query), $"Query {query} returned nothing");
        }

        public virtual async Task<DataverseResponse> DeleteAsync(string entityName, Guid id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DataverseExtensions.DataverseCall(() => { this._organizationService.Delete(entityName, id); return true; }, $"Delete '{entityName}':{id}");
        }

        internal virtual HttpResponseMessage ExecuteWebRequest(HttpMethod method, string queryString, string body, Dictionary<string, List<string>> customHeaders, string contentType = null, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public void Refresh(string logicalTableName)
        {
        }

        public virtual async Task<DataverseResponse<OrganizationResponse>> ExecuteAsync(OrganizationRequest request, CancellationToken cancellationToken = default)
        {
            return DataverseExtensions.DataverseCall(
                () => _organizationService.Execute(request),
                $"Execute '{request.RequestName}'");
        }

        public async Task<CustomApiSignature> GetPlugInAsync(string name, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await this.GetDataverseObjectAsync<CustomApiSignature>(name, cancellationToken).ConfigureAwait(false);
        }

        public void AddPlugIn(CustomApiSignature signature)
        {
            string key = signature.Api.uniquename;

            if (_plugIns.ContainsKey(key))
            {
                throw new ArgumentException(@"Plugin already declared with the same name.");
            }

            _plugIns.Add(key, signature);
        }

        public async Task<FormulaValue> ExecutePlugInAsync(RuntimeConfig config, string name, RecordValue arguments, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_plugIns.ContainsKey(name))
            {
                FormulaValue.NewError(new ExpressionError() { Kind = ErrorKind.InvalidArgument, Severity = ErrorSeverity.Critical, Message = $"Plugin {name} not found." });
            }

            CustomApiSignature plugin = _plugIns[name];
            OpenApiDocument swagger = plugin.GetSwagger();

            IEnumerable<ConnectorFunction> functions = OpenApiParser.GetFunctions(new ConnectorSettings("__plugin__") { Compatibility = ConnectorCompatibility.PowerAppsCompatibility }, swagger);

            if (functions == null || !functions.Any())
            {
                return FormulaValue.NewError(new ExpressionError() { Kind = ErrorKind.InvalidArgument, Severity = ErrorSeverity.Critical, Message = $"Plugin {name} has no functions." });
            }

            if (functions.Count() > 1)
            {
                return FormulaValue.NewError(new ExpressionError() { Kind = ErrorKind.InvalidArgument, Severity = ErrorSeverity.Critical, Message = $"Plugin {name} has more than one function." });
            }

            ConnectorFunction function = functions.First();
            PlugInRuntimeContext runtimeContext = new PlugInRuntimeContext(config, this, plugin);

            return await function.InvokeAsync(arguments.Fields.Select(field => field.Value).ToArray(), runtimeContext, cancellationToken).ConfigureAwait(false);
        }
    }

    public class PlugInRuntimeContext : BaseRuntimeConnectorContext
    {
        internal CustomApiSignature _plugin { get; }

        internal IDataversePlugInContext _pluginContext { get; }

        internal DataverseConnection _dvConnection { get; }

        public PlugInRuntimeContext(RuntimeConfig runtimeConfig, IDataversePlugInContext plugInContext, CustomApiSignature plugin)
            : base(runtimeConfig)
        {
            _plugin = plugin;
            _pluginContext = plugInContext;
            _dvConnection = runtimeConfig.GetService<DataverseConnection>();
        }

        public override FunctionInvoker GetInvoker(ConnectorFunction function, bool returnsRawResult = false)
        {
            return new PlugInInvoker(function, this);
        }
    }

    public class PlugInInvoker : FunctionInvoker
    {
        public new PlugInRuntimeContext Context => (PlugInRuntimeContext)base.Context;

        public PlugInInvoker(ConnectorFunction function, PlugInRuntimeContext runtimeContext)
            : base(function, runtimeContext)
        {
        }

        public override async Task<FormulaValue> SendAsync(InvokerParameters invokerParameters, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OrganizationRequest instantActionRequest = new OrganizationRequest(Context._plugin.Api.uniquename);
            ParameterCollection parameters = new ParameterCollection();
                        
            foreach (CustomApiRequestParam carp in Context._plugin.Inputs)
            {
                if (TryGetParameter(carp.name, invokerParameters, out FormulaValue val))
                {
                    parameters.Add(carp.uniquename, val.ToCustomApiObject(carp));
                }
            }

            instantActionRequest.Parameters = parameters;

            // Call plugin now
            DataverseResponse<OrganizationResponse> response = await Context._pluginContext.ExecuteAsync(instantActionRequest, cancellationToken).ConfigureAwait(false);
            response.ThrowEvalExOnError();

            ParameterCollection output = response.Response.Results;
            
            return DataverseEvalHelpers.Outputs2Fx(output, Context._plugin.Outputs, Context._dvConnection);
        }

        public bool TryGetParameter(string name, InvokerParameters ip, out FormulaValue val)
        {
            InvokerParameter param = ip.QueryParameters.FirstOrDefault(ip => ip.Name == name)
                                  ?? ip.PathParameters.FirstOrDefault(ip => ip.Name == name)
                                  ?? ip.HeaderParameters.FirstOrDefault(ip => ip.Name == name)
                                  ?? ip.BodyParameters.FirstOrDefault(ip => ip.Name == name);

            if (param == null)
            {
                val = null;
                return false;
            }

            val = param.Value;
            return val is not BlankValue;
        }
    }
}
