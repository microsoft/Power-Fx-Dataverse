using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.PowerFx.Dataverse.CdsUtilities
{
    internal class LinkEntityComparer : IEqualityComparer<LinkEntity>
    {
        public bool Equals(LinkEntity x, LinkEntity y)
        {
            if (x == null || y == null)
            {
                return false;
            }

            return x.LinkFromEntityName == y.LinkFromEntityName
                && x.LinkFromAttributeName == y.LinkFromAttributeName
                && x.LinkToEntityName == y.LinkToEntityName
                && x.LinkToAttributeName == y.LinkToAttributeName
                && x.JoinOperator == y.JoinOperator;
        }

        public int GetHashCode(LinkEntity obj)
        {
            return Tuple.Create(obj.LinkFromEntityName, obj.LinkFromAttributeName, obj.LinkToEntityName, obj.LinkToAttributeName, obj.JoinOperator).GetHashCode();
        }
    }
}
