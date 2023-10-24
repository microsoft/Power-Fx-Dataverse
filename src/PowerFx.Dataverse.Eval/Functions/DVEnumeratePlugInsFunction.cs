//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse
{
    public class DVEnumeratePlugInsFunction : ReflectionFunction
    {
        public DVEnumeratePlugInsFunction()
            : base("DVEnumeratePlugIns", TableType.Empty())
        {
            ConfigType = typeof(IDataverseReader);
        }

        public async Task<FormulaValue> Execute(IDataverseReader dvReader, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (dvReader == null)
            {
                return FormulaValue.NewError(new ExpressionError() { Kind = ErrorKind.InvalidArgument, Severity = ErrorSeverity.Critical, Message = @"No active Dataverse connection. Use ""DVConnect()"" to connector to Dataverse." });
            }

            QueryExpression query = new QueryExpression("customapi") { ColumnSet = new ColumnSet(true) };
            DataverseResponse<EntityCollection> list = await dvReader.RetrieveMultipleAsync(query).ConfigureAwait(false);

            if (list.HasError)
            {
                return FormulaValue.NewError(new ExpressionError() { Kind = ErrorKind.InvalidArgument, Severity = ErrorSeverity.Critical, Message = $"Error: {list.Error}" });
            }

            RecordType recordType = RecordType.Empty().Add(new NamedFormulaType("Value", FormulaType.String));

            if (list.Response.Entities.Count == 0)
            {
                return TableValue.NewTable(recordType);
            }

            List<string> pluginNames = list.Response.Entities.Select(entity => entity.Attributes["name"].ToString()).OrderBy(x => x).ToList();

            return TableValue.NewTable(recordType, pluginNames.Select(name => RecordValue.NewRecordFromFields(new NamedValue("Value", FormulaValue.New(name)))));
        }
    }
}
