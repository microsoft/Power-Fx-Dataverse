using Microsoft.AppMagic.Authoring.Importers.DataDescription;
using Microsoft.AppMagic.Authoring.Importers.ServiceConfig;
using Microsoft.Crm.Sdk.Messages;
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
            IRTransform t =  (IntermediateNode node, ICollection<ExpressionError> errors) => 
                node.Accept(new DelegationVisitor
                {
                    _retrieveSingle = retrieveSingle,
                    _errors = errors
                }, new DelegationVisitor.Context())._node;
            engine._irTransforms.Add(t);
        }
    }


    // Rewrite the tree inject delegation 
    internal class DelegationVisitor : RewritingIRVisitor<DelegationVisitor.RetVal, DelegationVisitor.Context>
    {
        // IDeally, this would just be in Dataverse.Eval nuget, but 
        // Only Dataverse nuget has InternalsVisisble access to implement an IR walker. 
        // So implement the walker in lower layer, and have callbacks into Dataverse.Eval layer as needed. 
        public Func<TableValue, Guid, CancellationToken, Task<DValue<RecordValue>>> _retrieveSingle;

        // $$$ Make this a member of the visitor, not the context.
        public ICollection<ExpressionError> _errors;

        public void AddError(ExpressionError error)
        {
            _errors.Add(error);
        }

        public DelegationVisitor()
        {
        }


        public class RetVal
        {
            // Non-delegating path 
            public RetVal(IntermediateNode node)
            {
                _node = node;
            }

            // Delegating path 
            public RetVal(IntermediateNode node, EntityMetadata metadata, ResolvedObjectNode tableIRNode, TableType tableType)
                : this(node)
            {
                _metadata = metadata;
                _table = tableIRNode;
                _tableType = tableType;
            }

            // Non-delegating path 
            public readonly IntermediateNode _node;

            // If any fields below are set, we're in the middle of building a delegation 
            public bool IsDelegating => _metadata != null;

            // $$$ DVC Connection? CDS
            public readonly EntityMetadata _metadata;

            // IR node that will resolve to the TableValue at runtime. 
            // From here, we can downcast at get the services. 
            public readonly ResolvedObjectNode _table;

            public readonly TableType _tableType;

            // $$$$ NOOOOOO - needs to be built at runtime. Fill in the holes.
            // We have the start of a query. 
            //public QueryExpression _query;            
        }

        public class Context
        {
        }
                        

        protected override IntermediateNode Materialize(RetVal ret)
        {
            if (ret.IsDelegating)
            {
                // Failed to delegate. 
                var reason = new ExpressionError
                {
                    Message = $"Delegating this operation on table '{ret._metadata.LogicalName}' is not supported.",
                    Span = ret._table.IRContext.SourceContext,
                    Severity = ErrorSeverity.Warning
                };
                this.AddError(reason);

                /*
                var q = ret._query;
                if (q.TopCount.HasValue || q.Criteria.Conditions.Count > 0)
                {
                    // We have an actionable filter. 
                    // Return custom node to execute that filter. 
                    // $$$$
                    
                }
                */
                // Error! Attempting to access a table, but not delegatable. 
                // $$$
            }

            return ret._node;            
        }

        protected override RetVal Ret(IntermediateNode node)
        {
            return new RetVal(node);
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

        // Let F = Filter(Accounts, Age > 30);
        // LookUp(F, Id=Guid);   $$$ 

        // With({ t: Accounts}, LookUp(T, Id=Guid));   $$$ 

        // LookUp(Accounts, Id=Guid)
        public override RetVal Visit(ResolvedObjectNode node, Context context)
        {
            if (node.IRContext.ResultType is TableType aggType)
            {
                // DType holds onto the metadata provider. 
                //var tableLogicalName = aggType.TableSymbolName; // VariableName/DisplayName (for multi-org policy)

                // $$$ Enure it's directly the table, and not some other expression.

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
                            var ret = new RetVal(node, metadata, node, aggType);

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
                if (arg0b.IsDelegating)
                {
                    var arg1 = node.Args[1];

                    ExpressionError reason = new ExpressionError
                    {
                        Message = $"Can't delegate LookUp: only support delegation for lookup on primary key field '{arg0b._metadata.PrimaryIdAttribute}'",
                        Span = arg1.IRContext.SourceContext,
                        Severity = ErrorSeverity.Warning
                    };

                    if (arg1 is LazyEvalNode arg1b && arg1b.Child is BinaryOpNode binOp)
                    {
                        var i1 = binOp.Left.IRContext.SourceContext.Min;
                        var i2 = binOp.Right.IRContext.SourceContext.Lim;
                        var span = new Span(i1, i2);
                        reason.Span = span;

                        // Pattern match to see if predicate is delegable.
                        // - Lookup(Table, Id=Guid) 
                        // - Lookup(Table, Guid=Id) 
                        // - Lookup(Table, Id=  If(ThisRecord.Test > Rand(), G1, G2)) ) // NO!!!!
                        if (binOp.Op == BinaryOpKind.EqGuid)
                        {
                            var left = binOp.Left;
                            var right = binOp.Right;

                            // $$$ Normalize order?

                            if (left is ScopeAccessNode left1)
                            {
                                if (left1.Value is ScopeAccessSymbol s)
                                {
                                    var fieldName = s.Name;
                                    if (fieldName == arg0b._metadata.PrimaryIdAttribute)
                                    {

                                        // $$$ Verify 2nd arg is a guid, does not depend on ThisRecord. 
                                        // This also means loop-invariant code motion.... 
                                        // So once we have LICM, this check will be easy 

                                        // Left = PrimaryKey on metadata? 
                                        // Right = Guid? We can evaluate this. 

                                        // Call 

                                        // We may have nested delegation. 
                                        // Although LICM would also have hoisted this. 
                                        // Also catch any delegation errors in nested. 
                                        var retVal2 = right.Accept(this, context);

                                        right = Materialize(retVal2);

                                        var x = ThisRecordVisitor.FindThisRecordUsage(node, right);
                                        if (x == null)
                                        {


                                            // $$$ May need to fallback if we can't delegate. 
                                            // __lookup(table, guid) ?? node;

                                            // __lookup(table, guid);
                                            var newNode = RetrieveSingle(arg0b, right);
                                            return Ret(newNode);
                                        }

                                        reason = new ExpressionError
                                        {
                                            Message = "Can't delegate LookUp: Id expression refers to ThisRecord",
                                            Span = x.Span,
                                            Severity = ErrorSeverity.Warning
                                        };
                                    }
                                }
                            }
                        }
                    }
                                        
                    // Failed to delegate. Add the warning and continue. 
                    this.AddError(reason);
                    return this.Ret(node);
                }            
            }
            // Other delegating functions, continue to compose...
            // - First, FirstN, 
            // - Filter
            // - Sort            

            return base.Visit(node, context, arg0b);

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