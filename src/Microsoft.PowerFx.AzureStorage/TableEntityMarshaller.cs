using System.Collections.Generic;
using Azure.Data.Tables;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.AzureStorage
{
    /// <summary>
    /// Convert a TableEntity to a FormulaValue
    /// </summary>
    public class TableEntityMarshaller : IDynamicTypeMarshaller
    {
        public bool TryMarshal(TypeMarshallerCache cache, object value, out FormulaValue result)
        {
            if (value is TableEntity te)
            {
                var namedValues = new List<NamedValue>();

                IDictionary<string, object> dict = te;

                foreach (var kv in dict)
                {
                    string propName = kv.Key;
                    var propType = kv.Value.GetType();
                    FormulaValue propVal = cache.Marshal(kv.Value, propType);

                    namedValues.Add(new NamedValue(propName, propVal));
                }

                result = FormulaValue.NewRecordFromFields(namedValues.ToArray());

                return true;
            }

            result = null;
            return false;
        }
    }
}
