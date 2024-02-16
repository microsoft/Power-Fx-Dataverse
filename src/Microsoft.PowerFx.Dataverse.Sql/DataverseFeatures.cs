namespace Microsoft.PowerFx.Dataverse
{
    public sealed class DataverseFeatures
    {
        // This FCB is to enable/disable Floating Point Feature, when this flag is enabled, Power Fx Core Number Data type will be treated as Double/Float and
        // Formula Columns will be able to produce/consume float data type columns
        // when this flag is disabled, Number Data type will act as Power Fx Core Decimal Data type and all Number Expressions would be treated as decimal 
        // and Formula columns will not be able to produce/consumer float type columns
        public bool IsFloatingPointEnabled { get; set;  }
    }
}
