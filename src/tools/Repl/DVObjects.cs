// <copyright file="Program.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using Microsoft.Xrm.Sdk;

namespace Repl
{
    public class CustomApiSignature
    {
        [DataverseEntity("customapi")]
        public CustomApiEntity Api;
        
        public CustomApiRequestParam[] Inputs;

        public CustomApiResponse[] Outputs;
    }

    [DataverseEntity("customapi")]
    public class CustomApiEntity
    {
        public string uniquename { get; set; }
        public string name { get; set; }
        public string displayname { get; set; }
        public string description { get; set; }
        public bool isfunction { get; set; }
        public bool isprivate { get; set; }
        public EntityReference plugintypeid { get; set; }
    }

    [DataverseEntity("customapirequestparameter")]
    public class CustomApiRequestParam : IParameterType
    {
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

    [DataverseEntity("customapiresponseproperty")]
    public class CustomApiResponse : IParameterType
    {
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
        public string displayname { get; set; }
        CustomApiParamType type { get; }
        string logicalentityname { get; }
    }

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
