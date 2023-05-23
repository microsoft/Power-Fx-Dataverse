using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using System.Data;
using System;

namespace Microsoft.PowerFx.Dataverse.Eval.DelegatedFunctions
{
    internal static class DelegationHelper
    {
        public static class CommonException
        {
            public static Exception InvalidInputArg = new InvalidOperationException($"Input arg should alway be of type {nameof(DelegationInfoValue)}");
        }
    }
}
