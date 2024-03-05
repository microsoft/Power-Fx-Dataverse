//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------


using Microsoft.AppMagic.Authoring;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Types;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Microsoft.PowerFx.Dataverse
{
    internal class DataverseOptionSet : ICdsOptionSetInfo
    {
        private string _relatedEntityName;
        private List<DName> _optionNames;
        private DType _invariantType = DType.Invalid;

        public DataverseOptionSet(string invariantName, string datasetName, string entityName, string columnName, string metadataId, string optionSetName, string optionSetId, string optionSetMetadataName, string attributeTypeName, Dictionary<int, string> optionSetValues, bool isGlobal, bool isBooleanValued)
        {
            Name = optionSetName;
            OptionSetId = string.IsNullOrEmpty(optionSetId) ? Guid.Empty : new Guid(optionSetId);
            IsBooleanValued = isBooleanValued;
            _relatedEntityName = entityName;
            RelatedColumnInvariantName = columnName;
            IsGlobal = isGlobal;
            var options = DisplayNameUtility.MakeUnique(optionSetValues.Select(kvp => new KeyValuePair<string, string>(kvp.Key.ToString(), kvp.Value)));
            _optionNames = options.LogicalToDisplayPairs.Select(kvp => kvp.Key).ToList();
            DisplayNameProvider = options;
            InvariantName = invariantName;
            _invariantType = DType.CreateOptionSetType(this);
        }

        /// <summary>
        /// The unique display name for the option set as managed by the document
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// The display collection name for the entity.  Will be null for a global option set
        /// </summary>
        public string EntityDisplayCollectionName { get; set; }

        public string Name { get; }

        public Guid OptionSetId { get; }

        public bool IsBooleanValued { get; }

        public string RelatedEntityName => _relatedEntityName;

        public string RelatedColumnInvariantName { get; }

        public bool IsGlobal { get; }

        public DName EntityName => new DName((IsGlobal ? "global" : RelatedEntityName) + "_" + InvariantName);

        public string InvariantName { get; }

        public DisplayNameProvider DisplayNameProvider { get; }

        public IEnumerable<DName> OptionNames => _optionNames;

        public IReadOnlyDictionary<int, string> Options => throw new System.NotImplementedException();

        public DType Type => _invariantType;

        public DKind BackingKind => IsBooleanValued ? DKind.Boolean : DKind.Number;

        bool IExternalOptionSet.IsConvertingDisplayNameMapping => false;

        public bool TryGetValue(DName fieldName, out OptionSetValue optionSetValue)
        {
            if (!OptionNames.Contains(fieldName))
            {
                optionSetValue = null;
                return false;
            }

            var osft = new OptionSetValueType(_invariantType.OptionSetInfo);
            if (IsBooleanValued)
            {
                // Dataverse registers boolean option sets with "1" and "0" as the field names for true and false values           
                optionSetValue = new OptionSetValue(fieldName.Value, osft, fieldName.Value == "1");
                return true;
            }
            else
            {
                optionSetValue = new OptionSetValue(fieldName.Value, osft);
                return true;
            }
        }
    }
}
