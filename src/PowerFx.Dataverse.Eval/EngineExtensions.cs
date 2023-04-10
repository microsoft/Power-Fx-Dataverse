//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.PowerFx.Types;
using System;
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
            public override async Task<DValue<RecordValue>> RetrieveAsync(TableValue table, Guid id, CancellationToken cancel)
            {
                // Binder should have enforced that this always succeeds.
                var t2 = (DataverseTableValue)table;

                var result = await t2.RetrieveAsync(id, cancel);
                return result;
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