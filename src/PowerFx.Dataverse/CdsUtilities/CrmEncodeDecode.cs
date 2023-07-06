//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Microsoft.PowerFx.Dataverse.CdsUtilities
{
    /// <summary>
    /// Taken from CDS code:
    /// https://dynamicscrm.visualstudio.com/DefaultCollection/OneCRM/_git/CDS?path=%2Fsrc%2FPlatform%2FCore%2FSecurity%2FCrmEncodeDecode.cs&_a=contents&version=GBv9.0_master
    /// Remove if this code is ever merged back into CDS
    /// </summary>
    internal static class CrmEncodeDecode
    {
        /// <summary>
        /// Encodes a string to be used as a sql literal string.
        /// 
        /// IMPORTANT: Please use SQL PARAMETERS whenever possible 
        /// instead of using this function.
        /// </summary>
        /// <param name="input">The string to encode</param>
        /// <returns>
        ///         An encoded string surrounded by single quotes
        ///         Ex:
        ///             SomeText          -> 'SomeText'
        ///             More 'Cool' Text  -> 'More ''Cool'' Text'
        /// </returns>
        /// <remarks>See http://msdn.microsoft.com/en-us/library/ms998271.aspx</remarks>
        public static string SqlLiteralEncode(string input)
        {
            char unicodeQuotation = '\u02BC'; // U+02BC is the quotation in unicode. SQL internally convert it as a regular quotation. Hence it can be used in sql query as SQL Smuggling attack. 
            char ansiQuotation = '\'';
            input = input.Replace(unicodeQuotation, ansiQuotation);
            return "'" + input.Replace("'", "''") + "'";
        }
    }
}
