using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Interpreter;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Extensions for enabling Dataverse features in config.
    /// </summary>
    public static class ConfigExtensions
    {        
        public static void EnableAIFunctions(this PowerFxConfig config)
        {
            config.SymbolTable.EnableAIFunctions();
        }

        /// <summary>
        /// Add AI functions (like AISummarize) to the symbol table. 
        /// This will also require a runtime call to add a <see cref="IDataverseExecute"/> so these functions can make the call at runtime. 
        /// </summary>
        /// <param name="symbolTable"></param>
        public static void EnableAIFunctions(this SymbolTable symbolTable )
        {
            symbolTable.AddFunction(new AISummarizeFunction());
            symbolTable.AddFunction(new AIReplyFunction());
            symbolTable.AddFunction(new AISentimentFunction());
        }

        public static void AddDataverseExecute(this RuntimeConfig config, IOrganizationService client)
        {
            var clientExecute = new DataverseService(client);
            config.AddDataverseExecute(clientExecute);
        }

        /// <summary>
        /// Add a runtime service for calling Dataverse messages.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="clientExecute"></param>
        public static void AddDataverseExecute(this RuntimeConfig config, IDataverseExecute clientExecute)
        {
            config.AddService<IDataverseExecute>(clientExecute);
        }
    }
}
