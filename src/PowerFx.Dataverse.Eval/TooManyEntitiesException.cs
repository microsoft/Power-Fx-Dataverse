using System;
using System.Runtime.Serialization;

namespace Microsoft.PowerFx.Dataverse
{
    public class TooManyEntitiesException : Exception
    {
        public string LogicalName { get; private set; }
        public int MaxRows { get; private set; }

        public override string Message => $"Too many entities in table {LogicalName}, more than {MaxRows} rows";

        public TooManyEntitiesException(string logicalName, int maxRows)
            : base()
        {
            LogicalName = logicalName;
            MaxRows = maxRows;
        }
    }
}