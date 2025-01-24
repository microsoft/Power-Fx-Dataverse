// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Dataverse.Eval.Delegation;
using Microsoft.PowerFx.Dataverse.Eval.Delegation.QueryExpression;
using Microsoft.PowerFx.Types;
using Newtonsoft.Json.Linq;

namespace Microsoft.PowerFx.Dataverse.Eval.Core
{
    internal class DependencyVisitorDataverse : DependencyVisitor
    {
        public CdsEntityMetadataProvider MetadataCache;
        public FxColumnMap ColumnMap;
        private readonly IRResult _irResult;

        public DependencyVisitorDataverse(IRResult irResult, CdsEntityMetadataProvider metadataCache)
        {
            MetadataCache = metadataCache ?? throw new ArgumentNullException(nameof(metadataCache));
            _irResult = irResult;
        }

        public DependencyInfo Scan()
        {
            _irResult.TopNode.Accept(this, new DependencyContext());
            return Info;
        }

        public override string Translate(string tableLogicalName, string fieldLogicalName)
        {
            if (MetadataCache.TryGetXrmEntityMetadata(tableLogicalName, out var entityMetadata))
            {
                // Normal case.
                if (entityMetadata.TryGetAttribute(fieldLogicalName, out _))
                {
                    return fieldLogicalName;
                }

                // Relationship
                else if (entityMetadata.TryGetRelationship(fieldLogicalName, out var realName))
                {
                    return realName;
                }

                // It can be Navigation property in case of dot walking.
                else
                {
                    var navigationRelation = entityMetadata.ManyToOneRelationships.FirstOrDefault(r => r.ReferencedEntityNavigationPropertyName == fieldLogicalName);

                    if (navigationRelation != null)
                    {
                        return navigationRelation.ReferencingAttribute;
                    }
                }
            }

            throw new InvalidOperationException($"Can't resolve {tableLogicalName}.{fieldLogicalName}");
        }

        protected override void CheckResolvedObjectNodeValue(ResolvedObjectNode node, DependencyContext context)
        {
            base.CheckResolvedObjectNodeValue(node, context);

            if (node.Value is GroupByObjectFormulaValue groupByFV && groupByFV.GroupBy != null)
            {
                var groupByNode = groupByFV.GroupBy;
                var tableName = ((AggregateType)node.IRContext.ResultType).TableSymbolName;
                foreach (var tableField in groupByNode.GroupingProperties)
                {
                    AddFieldRead(tableName, tableField);
                }

                foreach (var aggregateExpr in groupByNode.FxAggregateExpressions)
                {
                    AddFieldRead(tableName, aggregateExpr.PropertyName);
                }
            }
        }
    }
}
