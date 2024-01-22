//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Text;
using Microsoft.PowerFx.Dataverse.Eval.Core;
using Microsoft.Xrm.Sdk;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Schemas for Custom APIs. 
    /// See https://learn.microsoft.com/en-us/power-apps/developer/data-platform/custom-api-tables?tabs=webapi.
    /// </summary>
    [DebuggerDisplay("{DebuggerToString()}")]
    public class CustomApiSignature
    {
        public CustomApiEntity Api;

        public CustomApiRequestParam[] Inputs;

        public CustomApiResponse[] Outputs;

        // Just a debugging hint. 
        private string DebuggerToString()
        {
            if (this.Api == null)
            {
                return "<null>";
            }

            StringBuilder sb = new StringBuilder();

            sb.Append(this.Api.uniquename);
            sb.Append("(");
            DebugAppend(sb, Inputs);
            sb.Append(") -->");
            DebugAppend(sb, Outputs);

            return sb.ToString();
        }

        private static void DebugAppend(StringBuilder sb, IParameterType[] parameters)
        {
            string dil = "";
            sb.Append("{");
            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    sb.Append(dil);
                    sb.Append(param.uniquename);
                    sb.Append(":");

                    var typeName = Enum.GetName(typeof(CustomApiParamType), param.type);
                    sb.Append(typeName);
                    dil = ", ";
                }
            }
            sb.Append('}');
        }
    }

    [DebuggerDisplay("CustomApi: {uniquename}")]
    [DataverseEntity(TableName)]
    [DataverseEntityPrimaryId(nameof(customapiid))]
    public class CustomApiEntity
    {
        public const string TableName = "customapi";

        public Guid customapiid { get; set; }

        public string uniquename { get; set; }
        public string name { get; set; }
        public string displayname { get; set; }
        public string description { get; set; }
        public bool isfunction { get; set; }
        public bool isprivate { get; set; }
        public EntityReference plugintypeid { get; set; }

        // fxexpression table.
        // If non-null, then this is a low-code plugin. 
        public EntityReference fxexpressionid { get; set; }
    }


    [DataverseEntity(TableName)]
    public class CustomApiRequestParam : IParameterType
    {
        public const string TableName = "customapirequestparameter";

        public string uniquename { get; set; }
        public string name { get; set; }
        public string displayname { get; set; }
        public string description { get; set; }
        public bool isoptional { get; set; }
        public CustomApiParamType type { get; set; } // Option set. 
        public EntityReference customapiid { get; set; }

        // For type = EntityReference, name of the entity.
        // entity's id is in the value (similar to a guid) 
        // Might be null. - not supported in Power Fx. 
        public string logicalentityname { get; set; }
    }

    [DataverseEntity(TableName)]
    public class CustomApiResponse : IParameterType
    {
        public const string TableName = "customapiresponseproperty";

        public string uniquename { get; set; }
        public string name { get; set; }
        public string displayname { get; set; }
        public string description { get; set; }
        public CustomApiParamType type { get; set; } // Option set. 
        public EntityReference customapiid { get; set; }
        public string logicalentityname { get; set; }
    }

    // Common properties between Request and Response parameters types.
    public interface IParameterType
    {
        public string name { get; set; }
        public string uniquename { get; set; }
        public string displayname { get; set; }
        CustomApiParamType type { get; }
        string logicalentityname { get; }
    }

    /// <summary>
    /// Different kinds of parameters supported by Custom API.
    /// Name is the Label, Numeric value is the Value.
    /// </summary>
    public enum CustomApiParamType
    {
        Bool = 0,
        DateTime = 1,
        Decimal = 2,
        Entity = 3, // Pass Entity object. 
        EntityCollection = 4,
        EntityReference = 5, // Pass as EntityReference object. 
        Float = 6,
        Integer = 7,
        Money = 8,
        Picklist = 9,
        String = 10,
        StringArray = 11,
        Guid = 12
    }
}
