//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse.Tests
{
    public class CachedServiceClient : IOrganizationService, IOrganizationServiceAsync2, IOrganizationServiceAsync, IDisposable
    {
        private bool _cached;
        private string _cacheFolder;
        private ServiceClient _svcClient;
        private bool _disposed;
        private long _index;
        private ConcurrentDictionary<string, ConcurrentBag<(string, object)>> _inCache = new();

        public bool EnableAffinityCookie
        {
            get => _svcClient.EnableAffinityCookie;
            set => _svcClient.EnableAffinityCookie = value;
        }

        public CachedServiceClient(string dvConnectionString, bool cached, string cacheFolder = null)
        {
            _svcClient = new ServiceClient(dvConnectionString);
            _cached = cached;
            _cacheFolder = cacheFolder;

            if (_cached)
            {
                Console.WriteLine($"Cache Folder {_cacheFolder}");

                if (string.IsNullOrEmpty(_cacheFolder))
                {
                    throw new ArgumentNullException(nameof(cacheFolder));
                }

                if (!Directory.Exists(_cacheFolder))
                {
                    Directory.CreateDirectory(_cacheFolder);
                }
                else
                {
                    Regex rex = new Regex(@"(?<file>\d{4})_(?<method>[A-Za-z]+)_(?<type>[A-Za-z]+)_In\.json$");

                    foreach (string file in Directory.GetFiles(_cacheFolder, "*_In.json"))
                    {
                        Match m = rex.Match(file);

                        if (m.Success)
                        {
                            string method = m.Groups["method"].Value;
                            string type = m.Groups["type"].Value;
                            string fileNumber = m.Groups["file"].Value;

                            object obj = ObjectSerializer.Deserialize(File.ReadAllText(file), GetFullType(type));

                            if (_inCache.TryGetValue(method, out ConcurrentBag<(string, object)> bag))
                            {
                                bag.Add((fileNumber, obj));
                            }
                            else
                            {
                                _inCache[method] = new ConcurrentBag<(string, object)>() { (fileNumber, obj) };
                            }
                        }
                    }

                    string lastFile = Directory.GetFiles(_cacheFolder, "????_*.json").LastOrDefault();

                    if (lastFile != null)
                    {
                        _index = (long.Parse(Path.GetFileNameWithoutExtension(lastFile).Split('_')[0]) + 100) % 100;
                    }
                }

                Console.WriteLine($"Cache Folder Index {_index}");
            }
        }

        private Type GetFullType(string type)
        {            
            return Type.GetType($"Microsoft.Xrm.Sdk.Messages.{type}, Microsoft.Xrm.Sdk, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35")
                ?? Type.GetType($"Microsoft.Xrm.Sdk.Query.{type}, Microsoft.Xrm.Sdk, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35")
                ?? Type.GetType($"Microsoft.Xrm.Sdk.{type}, Microsoft.Xrm.Sdk, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35")
                ?? throw new NotImplementedException($"Unknown type {type}");
        }

        private long GetNextIndex()
        {
            return Interlocked.Increment(ref _index);
        }

        private TResponse Cache<T, TResponse>(string method, T request, Func<T, TResponse> dvRequest)
        {
            if (!_cached)
            {
                return dvRequest(request);
            }

            if (_inCache.TryGetValue(method, out ConcurrentBag<(string f, object o)> objs))
            {
                IEnumerable<(string f, T o)> cachedObjects = objs.Where(o => o.o is T).Select((o) => (o.f, (T)o.o));

                if (request is OrganizationRequest or)
                {
                    cachedObjects = cachedObjects.Where(o => o.o is OrganizationRequest co && co.RequestName == or.RequestName);

                    if (request is RetrieveEntityRequest rer)
                        cachedObjects = cachedObjects.Where(o => o.o is RetrieveEntityRequest rerco && rerco.EntityFilters == rer.EntityFilters && rerco.LogicalName == rer.LogicalName);
                }
                else if (request is QueryExpression qe)
                    // Needs proper comparison
                    cachedObjects = cachedObjects.Where(o => o.o is QueryExpression cqe && cqe.ColumnSet.AllColumns == qe.ColumnSet.AllColumns && cqe.EntityName == qe.EntityName && cqe.PageInfo.Count == qe.PageInfo.Count && cqe.TopCount == qe.TopCount);

                if (cachedObjects.Count() == 1)                
                    return DeserializeOutput<T, TResponse>(cachedObjects);                
            }

            long index = GetNextIndex();
            WriteRequestParameters(index, method, new object[] { request });
            return ExecuteRequest(index, method, () => dvRequest(request));
        }

        private TResponse DeserializeOutput<T, TResponse>(IEnumerable<(string f, T o)> cachedObjects)
        {
            Regex rex = new Regex(@"(?<file>\d{4})_(?<method>[A-Za-z]+)_(?<type>[A-Za-z]+)_Out\.json$");
            string file = Directory.GetFiles(_cacheFolder, $"{cachedObjects.First().f}_*_Out.json").First();
            Match m = rex.Match(file);

            if (m.Success)
            {
                string type = m.Groups["type"].Value;
                return (TResponse)ObjectSerializer.Deserialize(File.ReadAllText(file), GetFullType(type));
            }

            return default;
        }

        private TResponse Cache<T1, T2, TResponse>(string method, T1 reqParam1, T2 reqParam2, Func<T1, T2, TResponse> dvRequest)
        {
            if (!_cached)
            {
                return dvRequest(reqParam1, reqParam2);
            }

            long index = GetNextIndex();
            WriteRequestParameters(index, method, new object[] { reqParam1, reqParam2 });
            return ExecuteRequest(index, method, () => dvRequest(reqParam1, reqParam2));
        }

        private TResponse Cache<T1, T2, T3, TResponse>(string method, T1 reqParam1, T2 reqParam2, T3 reqParam3, Func<T1, T2, T3, TResponse> dvRequest)
        {
            if (!_cached)
            {
                return dvRequest(reqParam1, reqParam2, reqParam3);
            }

            long index = GetNextIndex();
            WriteRequestParameters(index, method, new object[] { reqParam1, reqParam2, reqParam3 });
            return ExecuteRequest(index, method, () => dvRequest(reqParam1, reqParam2, reqParam3));
        }

        private TResponse Cache<T1, T2, T3, T4, TResponse>(string method, T1 reqParam1, T2 reqParam2, T3 reqParam3, T4 reqParam4, Func<T1, T2, T3, T4, TResponse> dvRequest)
        {
            if (!_cached)
            {
                return dvRequest(reqParam1, reqParam2, reqParam3, reqParam4);
            }

            long index = GetNextIndex();
            WriteRequestParameters(index, method, new object[] { reqParam1, reqParam2, reqParam3, reqParam4 });
            return ExecuteRequest(index, method, () => dvRequest(reqParam1, reqParam2, reqParam3, reqParam4));
        }


        private TResponse Cache<T1, T2, T3, T4, T5, TResponse>(string method, T1 reqParam1, T2 reqParam2, T3 reqParam3, T4 reqParam4, T5 reqParam5, Func<T1, T2, T3, T4, T5, TResponse> dvRequest)
        {
            if (!_cached)
            {
                return dvRequest(reqParam1, reqParam2, reqParam3, reqParam4, reqParam5);
            }

            long index = GetNextIndex();
            WriteRequestParameters(index, method, new object[] { reqParam1, reqParam2, reqParam3, reqParam4, reqParam5 });
            return ExecuteRequest(index, method, () => dvRequest(reqParam1, reqParam2, reqParam3, reqParam4, reqParam5));
        }

        private void Cache<T>(string method, T reqParam1, Action<T> dvRequest)
        {
            if (!_cached)
            {
                dvRequest(reqParam1);
                return;
            }

            long index = GetNextIndex();
            WriteRequestParameters(index, method, new object[] { reqParam1 });
            ExecuteRequest(index, method, () => dvRequest(reqParam1));
            return;
        }

        private void Cache<T1, T2>(string method, T1 reqParam1, T2 reqParam2, Action<T1, T2> dvRequest)
        {
            if (!_cached)
            {
                dvRequest(reqParam1, reqParam2);
                return;
            }

            long index = GetNextIndex();
            WriteRequestParameters(index, method, new object[] { reqParam1, reqParam2 });
            ExecuteRequest(index, method, () => dvRequest(reqParam1, reqParam2));
            return;
        }

        private void Cache<T1, T2, T3, T4>(string method, T1 reqParam1, T2 reqParam2, T3 reqParam3, T4 reqParam4, Action<T1, T2, T3, T4> dvRequest)
        {
            if (!_cached)
            {
                dvRequest(reqParam1, reqParam2, reqParam3, reqParam4);
                return;
            }

            long index = GetNextIndex();
            WriteRequestParameters(index, method, new object[] { reqParam1, reqParam2, reqParam3, reqParam4 });
            ExecuteRequest(index, method, () => dvRequest(reqParam1, reqParam2, reqParam3, reqParam4));
            return;
        }

        private void WriteRequestParameters(long index, string method, object[] parameters)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                string prefix = parameters.Length == 1 ? $"{index:0000}_{method}" : $"{index:0000}_{method}_{i + 1}";
                string fileNameIn = Path.Combine(_cacheFolder, $"{prefix}_{ObjectConverter.GetTypeName(parameters[i].GetType())}_In.json");
                string requestText = ObjectSerializer.Serialize(parameters[i]);
                File.WriteAllText(fileNameIn, requestText);
            }
        }

        private T ExecuteRequest<T>(long index, string method, Func<T> action)
        {
            try
            {
                T response = action();

                string fileNameOut = Path.Combine(_cacheFolder, $"{index:0000}_{method}_{ObjectConverter.GetTypeName(response.GetType())}_Out.json");
                string responseText = ObjectSerializer.Serialize(response);
                File.WriteAllText(fileNameOut, responseText);

                return response;
            }
            catch (Exception ex)
            {
                string fileNameOutException = Path.Combine(_cacheFolder, $"{index:0000}_{method}_{ObjectConverter.GetTypeName(ex.GetType())}_Out_Exception.json");
                string exceptionText = ObjectSerializer.Serialize(ex);
                File.WriteAllText(fileNameOutException, exceptionText);

                throw;
            }
        }

        private void ExecuteRequest(long index, string method, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                string fileNameOutException = Path.Combine(_cacheFolder, $"{index}_{method}_{ObjectConverter.GetTypeName(ex.GetType())}_Out_Exception.json");
                string exceptionText = ObjectSerializer.Serialize(ex);
                File.WriteAllText(fileNameOutException, exceptionText);

                throw;
            }
        }

        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            Cache(nameof(Associate), entityName, entityId, relationship, relatedEntities, (en, eid, rs, re) => _svcClient.Associate(en, eid, rs, re));
        }

        public Task AssociateAsync(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities, CancellationToken cancellationToken)
        {
            return Cache(nameof(AssociateAsync), entityName, entityId, relationship, relatedEntities, cancellationToken, (en, eid, rs, re, ct) => _svcClient.AssociateAsync(en, eid, rs, re, ct));
        }

        public Task AssociateAsync(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            return Cache(nameof(AssociateAsync), entityName, entityId, relationship, relatedEntities, (en, eid, rs, re) => _svcClient.AssociateAsync(en, eid, rs, re));
        }

        public Guid Create(Entity entity)
        {
            return Cache(nameof(Create), entity, (e) => _svcClient.Create(e));
        }

        public Task<Entity> CreateAndReturnAsync(Entity entity, CancellationToken cancellationToken)
        {
            return Cache(nameof(CreateAndReturnAsync), entity, cancellationToken, (e, ct) => _svcClient.CreateAndReturnAsync(e, ct));
        }

        public Task<Guid> CreateAsync(Entity entity, CancellationToken cancellationToken)
        {
            return Cache(nameof(CreateAsync), entity, cancellationToken, (e, ct) => _svcClient.CreateAsync(e, ct));
        }

        public Task<Guid> CreateAsync(Entity entity)
        {
            return Cache(nameof(CreateAsync), entity, (e) => _svcClient.CreateAsync(e));
        }

        public void Delete(string entityName, Guid id)
        {
            Cache(nameof(Delete), entityName, id, (en, i) => _svcClient.Delete(en, i));
        }

        public Task DeleteAsync(string entityName, Guid id, CancellationToken cancellationToken)
        {
            return Cache(nameof(DeleteAsync), entityName, id, cancellationToken, (en, i, ct) => _svcClient.DeleteAsync(en, i, ct));
        }

        public Task DeleteAsync(string entityName, Guid id)
        {
            return Cache(nameof(DeleteAsync), entityName, id, (en, i) => _svcClient.DeleteAsync(en, i));
        }

        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            Cache(nameof(Disassociate), entityName, entityId, relationship, relatedEntities, (en, eid, rls, re) => _svcClient.Disassociate(en, eid, rls, re));
        }

        public Task DisassociateAsync(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities, CancellationToken cancellationToken)
        {
            return Cache(nameof(DisassociateAsync), entityName, entityId, relationship, relatedEntities, cancellationToken, (en, eid, rls, re, ct) => _svcClient.DisassociateAsync(en, eid, rls, re, ct));
        }

        public Task DisassociateAsync(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            return Cache(nameof(DisassociateAsync), entityName, entityId, relationship, relatedEntities, (en, eid, rls, re) => _svcClient.DisassociateAsync(en, eid, rls, re));
        }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            return Cache(nameof(Execute), request, (req) => _svcClient.Execute(req));
        }

        public Task<OrganizationResponse> ExecuteAsync(OrganizationRequest request, CancellationToken cancellationToken)
        {
            return Cache(nameof(ExecuteAsync), request, cancellationToken, (req, ct) => _svcClient.ExecuteAsync(req, ct));
        }

        public Task<OrganizationResponse> ExecuteAsync(OrganizationRequest request)
        {
            return Cache(nameof(ExecuteAsync), request, (req) => _svcClient.ExecuteAsync(req));
        }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            return Cache(nameof(Retrieve), entityName, id, columnSet, (en, i, cs) => _svcClient.Retrieve(en, i, cs));
        }

        public Task<Entity> RetrieveAsync(string entityName, Guid id, ColumnSet columnSet, CancellationToken cancellationToken)
        {
            return Cache(nameof(RetrieveAsync), entityName, id, columnSet, cancellationToken, (en, i, cs, ct) => _svcClient.RetrieveAsync(en, i, cs, ct));
        }

        public Task<Entity> RetrieveAsync(string entityName, Guid id, ColumnSet columnSet)
        {
            return Cache(nameof(RetrieveAsync), entityName, id, columnSet, (en, i, cs) => _svcClient.RetrieveAsync(en, i, cs));
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            return Cache(nameof(RetrieveMultiple), query, (q) => _svcClient.RetrieveMultiple(q));
        }

        public Task<EntityCollection> RetrieveMultipleAsync(QueryBase query, CancellationToken cancellationToken)
        {
            return Cache(nameof(RetrieveMultipleAsync), query, cancellationToken, (q, ct) => _svcClient.RetrieveMultipleAsync(q, ct));
        }

        public Task<EntityCollection> RetrieveMultipleAsync(QueryBase query)
        {
            return Cache(nameof(RetrieveMultipleAsync), query, (q) => _svcClient.RetrieveMultipleAsync(q));
        }

        public void Update(Entity entity)
        {
            Cache(nameof(Update), entity, (e) => _svcClient.Update(e));
        }

        public Task UpdateAsync(Entity entity, CancellationToken cancellationToken)
        {
            return Cache(nameof(UpdateAsync), entity, cancellationToken, (e, ct) => _svcClient.UpdateAsync(e, ct));
        }

        public Task UpdateAsync(Entity entity)
        {
            return Cache(nameof(UpdateAsync), entity, (e) => _svcClient.UpdateAsync(e));
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _svcClient?.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
