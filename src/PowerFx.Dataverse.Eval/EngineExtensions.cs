//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.PowerFx.Dataverse.DelegationEngineExtensions;

namespace Microsoft.PowerFx.Dataverse
{
    public static class EngineExtensions
    {
        // Provides adapter for Dataverse project to call back into Dataverse.Eval types, like DataverseTableValue.
        private class DelegationHooksImpl :  DelegationHooks
        {
            public override async Task<DValue<RecordValue>> RetrieveAsync(TableValue table, FilterExpression filter, CancellationToken cancel)
            {
                // Binder should have enforced that this always succeeds.
                var t2 = (DataverseTableValue)table;

                var result = await t2.RetrieveAsync(filter, cancel);
                return result;
            }

            public override async Task<IEnumerable<DValue<RecordValue>>> RetrieveMultipleAsync(TableValue table, FilterExpression filter, int? count, CancellationToken cancel)
            {
                // Binder should have enforced that this always succeeds.
                var t2 = (DataverseTableValue)table;

                var result = await t2.RetrieveMultipleAsync(filter, count, cancel);
                return result;
            }

            public override bool IsDelegableSymbolTable(ReadOnlySymbolTable symbolTable)
            {
                bool isRealTable = 
                    symbolTable.DebugName == SingleOrgPolicy.SymTableName || 
                    symbolTable.DebugName == DVSymbolTable.SymTableName;

                return isRealTable;
            }
        }

        /// <summary>
        /// Public facing API to enable delegation.
        /// </summary>
        /// <param name="engine"></param>
        public static void EnableDelegation(this Engine engine)
        {
            engine.EnableDelegationCore(new DelegationHooksImpl());
        }
    }
}