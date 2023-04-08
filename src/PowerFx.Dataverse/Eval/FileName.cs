using Microsoft.AppMagic.Authoring.Importers.DataDescription;
using Microsoft.AppMagic.Authoring.Importers.ServiceConfig;
using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Texl.Builtins;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Dataverse.CdsUtilities;
using Microsoft.PowerFx.Dataverse.DataSource;
using Microsoft.PowerFx.Dataverse.Functions;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using BuiltinFunctionsCore = Microsoft.PowerFx.Core.Texl.BuiltinFunctionsCore;
using Span = Microsoft.PowerFx.Syntax.Span;

namespace Microsoft.PowerFx.Dataverse
{
#if false

    - can we get metadata without a DVC?
        from TableType , buried in Dtype?


    - at runtime, can we get OrgService from TableValue?

#endif


    public static class DelegationHelpers
    {
        public static void EnableDelegation2(this Engine engine, Func<TableValue, Guid, CancellationToken, Task<DValue<RecordValue>>> retrieveSingle)
        {
            var v = new DelegationVisitor
            {
                _retrieveSingle = retrieveSingle
            };
            
            IRTransform t =  (IntermediateNode node, ICollection<ExpressionError> errors) => node.Accept(v, new DelegationVisitor.Context { _errors = errors })._node;
            engine._irTransforms.Add(t);
        }
    }


    // Rewrite the tree inject delegation 
    internal class DelegationVisitor : IdentityIRVisitor<DelegationVisitor.RetVal, DelegationVisitor.Context>
    {
        // IDeally, this would just be in Dataverse.Eval nuget, but 
        // Only Dataverse nuget has InternalsVisisble access to implement an IR walker. 
        // So implement the walker in lower layer, and have callbacks into Dataverse.Eval layer as needed. 
        public Func<TableValue, Guid, CancellationToken, Task<DValue<RecordValue>>> _retrieveSingle;

        public DelegationVisitor()
        {
        }


        public class RetVal
        {
            public IntermediateNode _node;

            // IR node that will resolve to the TableValue at runtime. 
            // From here, we can downcast at get the services. 
            public ResolvedObjectNode _table;

            public TableType _tableType;

            // We have the start of a query. 
            public QueryExpression _query;

            // $$$ DVC Connection? CDS
            public EntityMetadata _metadata;
        }
        public class Context
        {
            public ICollection<ExpressionError> _errors;

            public void AddError(ExpressionError error)
            {
                _errors.Add(error);
            }
        }

        protected override IntermediateNode Materialize(RetVal ret)
        {
            if (ret._node != null)
            {
                return ret._node;
            }

            var q = ret._query;
            if (q != null)
            {
                if (q.TopCount.HasValue || q.Criteria.Conditions.Count > 0)
                {
                    // We have an actionable filter. 
                    // Return custom node to execute that filter. 
                    // $$$$
                    
                }

                // Error! Attempting to access a table, but not delegatable. 
                // $$$
            }

            // Binder should ensure this never happens.
            throw new InvalidOperationException();
        }

        protected override RetVal Ret(IntermediateNode node)
        {
            return new RetVal { _node = node};
        }


        /*
private bool TryGetEntityMetadata(IntermediateNode node, out EntityMetadata metadata)
{
    // DataverseConnection
    // - public bool TryGetVariableName(string logicalName, out string variableName)
    // - internal EntityMetadata GetMetadataOrThrow(string tableLogicalName)

    metadata = null;
    return false;
}
*/

        // public Func<string, EntityMetadata> _tryGetEntityMetadata;

        public override RetVal Visit(ResolvedObjectNode node, Context context)
        {
            if (node.IRContext.ResultType is TableType aggType)
            {
                // DType holds onto the metadata provider. 
                //var tableLogicalName = aggType.TableSymbolName; // VariableName/DisplayName (for multi-org policy)

                var type = aggType._type;

                var ads = type.AssociatedDataSources.FirstOrDefault();
                if (ads != null)
                {
                    var tableLogicalName = ads.TableMetadata.Name; // logical name

                    if (ads.DataEntityMetadataProvider is CdsEntityMetadataProvider m2)
                    {
                        if (m2.TryGetXrmEntityMetadata(tableLogicalName, out var metadata))
                        {
                            // It's a delegatable table. 
                            var ret = new RetVal
                            {
                                _tableType = aggType,
                                _table = node,
                                _query = new QueryExpression(tableLogicalName),
                                _metadata = metadata
                            };
                            return ret;
                        }
                    }
                }

                if (type.IsExpandEntity)
                {
                    /*
                    var expandInfo = type.ExpandInfo;
                    var metadataProvider = expandInfo.ParentDataSource.DataEntityMetadataProvider;

                    var m2 = (CdsEntityMetadataProvider)metadataProvider;

                    if (m2.TryGetXrmEntityMetadata(tableLogicalName, out var metadata))
                    {
                        // It's a delegatable table. 
                        var ret = new RetVal
                        {
                            _tableType = aggType,
                            _table = node,
                            _query = new QueryExpression(tableLogicalName),
                            _metadata = metadata
                        };
                        return ret;
                    }*/
                };
            }   

            return Ret(node);
        }

        // To inject custom actions, 
        // Inject a CallNode with a custom function.
        // It still gets args at runtime. 

        class DelegateFunction : TexlFunction, IAsyncTexlFunction
        {
            public Func<TableValue, Guid, CancellationToken, Task<DValue<RecordValue>>> _retrieveSingle;

            public DelegateFunction(string name, FormulaType returnType, params FormulaType[] paramTypes)
              : this(name, returnType._type, Array.ConvertAll(paramTypes, x => x._type))
            {
            }
            public DelegateFunction(string name, DType returnType, params DType[] paramTypes)
            : base(DPath.Root, name, name, SG("Custom func " + name), FunctionCategories.MathAndStat, returnType, 0, paramTypes.Length, paramTypes.Length, paramTypes)
            {
            }

            public static TexlStrings.StringGetter SG(string text)
            {
                return (string locale) => text;
            }

            public override bool IsSelfContained => false;

            public override IEnumerable<TexlStrings.StringGetter[]> GetSignatures()
            {
                yield return new TexlStrings.StringGetter[0];
            }

            // Run the Query
            // TableValue, Guid
            public async Task<FormulaValue> InvokeAsync(FormulaValue[] args, CancellationToken cancellationToken)
            {
                var table = (TableValue) args[0];
                var guid = ((GuidValue)args[1]).Value;

                var result = await  _retrieveSingle(table, guid, cancellationToken);

                // $$$ Error? Throw?
                return result.Value;
            }
        }

        // Generate a lookup call for: Lookup(Table, Id=Guid)  
        private CallNode RetrieveSingle(RetVal query, IntermediateNode argGuid)
        {
            var func = new DelegateFunction("__lookup", query._tableType.ToRecord(), query._tableType, FormulaType.Guid)
            {
                _retrieveSingle = this._retrieveSingle
            };

            var node = new CallNode(IRContext.NotInSource(query._tableType), func, query._table, argGuid);
            return node;
        }

        public override RetVal Visit(CallNode node, Context context)
        {
            // Pattern match
            // - Lookup(Table, Id=Guid)  --> Retrieve
            // - Filter(Table, Key=Value1  && Key=Value2)  -->FetchXml

            RetVal arg0b = node.Args[0].Accept(this, context);

            var func = node.Function.Name;

            if (func == "LookUp")
            {            
                if (arg0b._metadata != null)
                {
                    var arg1 = node.Args[1];
                    if (arg1 is LazyEvalNode arg1b && arg1b.Child is BinaryOpNode binOp)
                    {
                        // Pattern match to see if predicate is delegable.
                        if (binOp.Op == BinaryOpKind.EqGuid)
                        {
                            var left = binOp.Left;
                            if (left is ScopeAccessNode left1)
                            {
                                if (left1.Value is ScopeAccessSymbol s)
                                {
                                    var fieldName = s.Name;
                                    if (fieldName == arg0b._metadata.PrimaryIdAttribute)
                                    {

                                        // $$$ Verify 2nd arg is a guid, does not depend on ThisRecord. 


                                        var right = binOp.Right;
                                        // Left = PrimaryKey on metadata? 
                                        // Right = Guid? We can evaluate this. 

                                        // Call 
                                        IntermediateNode argGuid = right;

                                        // __lookup(table, guid);
                                        var newNode = RetrieveSingle(arg0b, argGuid);
                                        return Ret(newNode);
                                    }
                                }
                            }
                        }

                        // Source span 
                        // !!! Delegation warning. We're operating on a table, but can't delegate. 

                        var error = new ExpressionError
                        {
                            Message = "Can't delegate LookUp",
                            Span = node.IRContext.SourceContext,
                            Severity = ErrorSeverity.Warning
                        };
                        context.AddError(error);
                    }
                }            
            }
            // Other delegating functions, continue to compose...
            // - First, FirstN, 
            // - Filter
            // - Sort            

            return base.Visit(node, context);

            // Other non-delegating supported function....
            



            // Table - 

            // First(Filter(...)) 
            // FirstN(Filter(...), N) 
            // First(Table) 
            //  qe.TopCount


            // FirstN(FirstN(...))  - silly, but compose?
            // Filter(Filter(Table)) - compose?

            // Sort(...)
            //    qe.Orders

            // Distint() 

            // First(Sort(Table))
            // Sort(First(Table))  ???? which one takes precedence. 

            // ColumnSets? - what is retrieved.

            //  Relationships???

            // Paging?

            // Capabilities
            //  CountRows(Table); // not supported

            // Warnings on things we can't delegate?
            //  Last(Table) 
            //  Filter(Table, F(ThisRecord.Field) > 2);

        }

        // First(Table).Field + 3
        // XXX().Field + 3
    }
}