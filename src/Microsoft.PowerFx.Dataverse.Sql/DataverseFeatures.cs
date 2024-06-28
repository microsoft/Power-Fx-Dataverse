namespace Microsoft.PowerFx.Dataverse
{
    public sealed class DataverseFeatures
    {
        // This FCB is to enable/disable Floating Point Feature, when this flag is enabled, Power Fx Core Number Data type will be treated as Double/Float and
        // Formula Columns will be able to produce/consume float data type columns
        // when this flag is disabled, Number Data type will act as Power Fx Core Decimal Data type and all Number Expressions would be treated as decimal 
        // and Formula columns will not be able to produce/consumer float type columns
        public bool IsFloatingPointEnabled { get; set;  }

        // This FCB is to enable/disable Option Set Feature.
        // When this flag is enabled, Formula Field of type Options Set are supported.
        public bool IsOptionSetEnabled { get; set; }

        // This flag when enabled, we use DataverseEngine.MaxInvariantExpressionLength = 1500 as max expression length
        // allowed for invariant formulas during compile, else we use DataverseEngine.MaxExpressionLength = 1000.
        public bool UseMaxInvariantExpressionLength { get; set; }

        public bool UseLookupFieldNameWhenNavPropNameIsDiff { get; set; }
    }
}
