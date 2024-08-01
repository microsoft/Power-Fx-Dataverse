// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Xrm.Sdk.Metadata;

namespace Microsoft.PowerFx.Dataverse
{
    public class SqlCompileOptions
    {
        // IF null, pick a random guid
        // Should include any qualifiers, outer brackets. Ex:
        //   [dbo].[udf1_text]
        public string UdfName;

        public Mode CreateMode;

        public enum Mode
        {
            Create,

            Alter
        }

        public TypeDetails TypeHints;

        public class TypeDetails
        {
            public AttributeTypeCode TypeHint;

            public int Precision;

            public double MinValue;

            public double MaxValue;

            public IntegerFormat IntegerFormatValue;
        }
    }
}
