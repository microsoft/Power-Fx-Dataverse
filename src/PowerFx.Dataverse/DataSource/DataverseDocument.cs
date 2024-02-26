//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.AppMagic.Authoring;
using Microsoft.PowerFx.Core.App;
using System;
using System.Collections.Generic;
using Microsoft.PowerFx.Core.App.Controls;
using Microsoft.PowerFx.Core.Entities;
using Microsoft.PowerFx.Core.Errors;
using Microsoft.PowerFx.Core.Utils;

namespace Microsoft.PowerFx.Dataverse
{
    internal class DataverseDocument : IExternalDocument, IExternalOptionSetDocument
    {
        private IExternalEntityScope _globalScope;
        private DataverseDocumentProperties _properties;

        public DataverseDocument(CdsEntityMetadataProvider provider)
        {
            _globalScope = provider;
            _properties = new DataverseDocumentProperties();
        }

        public IExternalDocumentProperties Properties => _properties;

        public IExternalEntityScope GlobalScope => _globalScope;

        public IEnumerable<IDocumentError> Errors => new IDocumentError[0];

        public IExternalControl AppInfoControl => throw new NotImplementedException();


        public bool TryGetControlByUniqueId(string name, out IExternalControl control)
        {
            throw new NotImplementedException();
        }

        public bool TryGetServiceInfo(DName namespaceName, out IExternalEntity serviceInfo)
        {
            throw new NotImplementedException();
        }

        IExternalOptionSet IExternalOptionSetDocument.RegisterOrRefreshOptionSet(string invariantName, string datasetName, string entityName, string columnName, string metadataId, string optionSetName, string optionSetId, string optionSetMetadataName, string attributeTypeName, Dictionary<int, string> optionSetValues, bool isGlobal, bool isBooleanValued, List<OptionSetInfoMapping> optionSetInfoMappings)
        {
            bool shouldUseNumericBackingKindForOptionSets = (_globalScope is CdsEntityMetadataProvider mp) && mp._shouldUseNumericBackingKindForOptionSetsInFormulaFields;
            return new DataverseOptionSet(invariantName, datasetName, entityName, columnName, metadataId, optionSetName, optionSetId, optionSetMetadataName, attributeTypeName, optionSetValues, isGlobal, isBooleanValued, shouldUseNumericBackingKindForOptionSets);
        }
    }
}
