using Microsoft.PowerFx;
using Microsoft.PowerFx.Dataverse;
using Microsoft.PowerFx.Types;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Helpers for marshalling between Custom API parameters and Power Fx. 
    /// (client) Call a custom API from Fx:
    ///   Fx2Inputs --> run custom API --> Outputs2Fx.
    /// 
    /// (server) Implement a custom API in Fx:
    ///   Inputs2Fx --> Eval Fx --> Fx2Outputs 
    /// 
    /// Also compute Fx types for Custom API signature.
    /// These match the marshalling layers. 
    ///   GetInputType:  Fx2Inputs + Inputs2Fx
    ///   GetOutputType: Outputs2Fx + Fx2Outputs
    ///   
    /// </summary>
    public static class CustomApiMarshaller
    {
        // Get a record, with each field correspdonding to an input. Matched by name. 
        private static RecordType GetRecordType(IParameterType[] inputs, ICustomApiParameterMarshaller parameterMarshaller)
        {
            if (parameterMarshaller == null)
            {
                parameterMarshaller = new CustomApiParameterMarshaller(null);
            }

            // Inputs are always as a record. Enables named input parameters. 
            var inRecord = RecordType.Empty();
            foreach (var input in inputs)
            {
                var name = input.uniquename;
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidOperationException($"Bad name");
                }

                inRecord = inRecord.Add(new NamedFormulaType(
                    name, parameterMarshaller.ToType(input)
                     ));
            }

            return inRecord;
        }

        // Given CustomAPI inputs, calculate the power fx type. 
        // Record, with each field correspdonding to an input. Matched by name. 
        public static RecordType GetInputType(CustomApiRequestParam[] inputs, ICustomApiParameterMarshaller parameterMarshaller)
        {
            // Handle optional:
            // https://github.com/microsoft/Power-Fx-Dataverse/issues/393
            foreach (var input in inputs)
            {
                if (input.isoptional)
                {   
                    throw new NotSupportedException($"Optional parameters are not supported. {input.uniquename}");
                }
            }

            return GetRecordType(inputs, parameterMarshaller);
        }

        // Get runtime values, correpsonding to GetInputType.
        public static RecordValue Inputs2Fx(ParameterCollection inputs, DataverseConnection dvc)
        {
            var thisRecord = RecordValue.Empty();

            // Ignore Target entity passed for bounded actions, target entity will be marshalled separately as done for automated plugins
            var fields = inputs
                .Where(kv => kv.Key != "Target")
                .Select(
                kv => new NamedValue(kv.Key, ToPowerFxValue(kv.Value, dvc))
                );

            return FormulaValue.NewRecordFromFields(fields);
        }

        // Convert individual input to Power Fx value. 
        // Handle Entity,EntityRef which may require marshalling. 
        private static FormulaValue ToPowerFxValue(object obj, DataverseConnection dvc)
        {
            if (obj == null)
            {
                // $$$ Specify type
                return FormulaValue.NewBlank();
            }

            if (obj is EntityReference er)
            {
                // For entity Reference, the lookup could be deferred.
                var record = dvc.RetrieveAsync(er.LogicalName, er.Id, columns: null).GetAwaiter().GetResult();
                return record;
            }
            else if (obj is Entity entity)
            {
                var record = dvc.Marshal(entity);
                return record;
            }
            else if (obj is EntityCollection inputEntityCollection)
            {
                var records = new List<RecordValue>();
                foreach (Entity input in inputEntityCollection.Entities)
                {
                    records.Add(dvc.Marshal(input));
                }
                // Handle empty input entityCollection
                var tableValue = (records.Count != 0) ?
                                    FormulaValue.NewTable(records[0].Type, records.ToArray()) :
                                    FormulaValue.NewTable(RecordType.Empty(), new RecordValue[0]);
                return tableValue;
            }

            return PrimitiveValueConversions.Marshal(obj, obj.GetType());
        }

        // For Fx calling a custom API.
        // Builds a Parameter collection that can then be decoded by GetInputValues.
        // Fx --> Objects. 
        public static void Fx2Inputs(ParameterCollection inputs, RecordValue fxInputValues, CustomApiRequestParam[] inputMetadata)
        {
            Fx2Record(inputs, fxInputValues, inputMetadata);
        }

        // Common marshalling for both input and output parameters. 
        // If input is null or has extra inputs, fx engine returns blankValue which is ignored and not added to parameters [UnitTest: IgnoreExtraFieldsWithInputs() | InvokeInstanActionWithEntityDataTypeAsInputParameter()]
        private static void Fx2Record(ParameterCollection parameters, RecordValue fxValues, IParameterType[] inputMetadata)
        {
            // Marshalling must be exact. If CDS expects an Int32, can't pass a Double.
            // So we need to know the target type. 
            foreach (var input in inputMetadata)
            {
                string name = input.uniquename;
                var fxValue = fxValues.GetField(name);
                if (fxValue is not BlankValue)
                {
                    object value = ToCustomApiObject(fxValue, input, name);
                    parameters[name] = value;
                }
            }
        }

        /// <summary>
        /// Returns a RecordValue where each field matches an output parameter. 
        /// Unless there is 1 output parameter, named "Value" - in which case we return just that. 
        /// </summary>
        /// <param name="outputMetadata"></param>
        /// <returns></returns>
        public static bool IsOutputTypeSingle(params CustomApiResponse[] outputMetadata)
        {
            return outputMetadata.Length == 1 && outputMetadata[0].name == TableValue.ValueName;
        }

        public static FormulaType GetOutputType(CustomApiResponse[] outputs, ICustomApiParameterMarshaller parameterMarshaller)
        {
            if (parameterMarshaller == null)
            {
                parameterMarshaller = new CustomApiParameterMarshaller(null);
            }

            FormulaType outType;
            if (IsOutputTypeSingle(outputs))
            {
                outType = parameterMarshaller.ToType(outputs[0]);
            }
            else
            {
                outType = GetRecordType(outputs, parameterMarshaller);
            }

            return outType;
        }

        // Apply Power Fx result from Custom API back to a dataverse collection. 
        public static void Fx2Outputs(ParameterCollection outputs, FormulaValue result, CustomApiResponse[] outputMetadata)
        {
            ThrowIfErrorValue("", result);

            if (outputMetadata.Length == 0)
            {
                return;
            }

            if (IsOutputTypeSingle(outputMetadata))
            {
                var x = ToCustomApiObject(result, outputMetadata[0], "");
                outputs[TableValue.ValueName] = x;
            }
            else
            {
                var record = (RecordValue)result;
                Fx2Record(outputs, record, outputMetadata);
            }
        }

        // Type should match GetOutputType.
        public static FormulaValue Outputs2Fx(ParameterCollection outputs, CustomApiResponse[] outputMetadata, DataverseConnection dataverseConnection)
        {
            if (outputMetadata.Length == 0)
            {
                return FormulaValue.NewBlank(RecordType.Empty());
            }

            if (IsOutputTypeSingle(outputMetadata))
            {
                var x = outputs.Values.First();

                var fv = ToPowerFxValue(x, dataverseConnection);
                return fv;
            }
            else
            {
                var record = Inputs2Fx(outputs, dataverseConnection);
                return record;
            }
        }

        /// <summary>
        /// Convert primitive parameters to Power Fx. 
        /// For non-primitives, use <see cref="ICustomApiParameterMarshaller"/>.
        /// </summary>
        /// <param name="typeCode"></param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        public static FormulaType ToPrimitivePowerFxType(this CustomApiParamType typeCode)
        {
            switch (typeCode)
            {
                case CustomApiParamType.Float:
                    return FormulaType.Number;

                case CustomApiParamType.Integer:
                case CustomApiParamType.Decimal:
                    return FormulaType.Decimal;

                case CustomApiParamType.Bool:
                    return FormulaType.Boolean;

                case CustomApiParamType.String:
                    return FormulaType.String;

                case CustomApiParamType.DateTime:
                    return FormulaType.DateTime;

                case CustomApiParamType.Guid:
                    return FormulaType.Guid;

                default:
                    throw new NotSupportedException($"Unsupported param type: {typeCode}");
            }
        }

        // Called when a Custom API returns a FxValue and we need to marshal it back to Custom API type. 		
        private static object ToCustomApiObject(FormulaValue fxValue, IParameterType parameterType, string hintName)
        {
            ThrowIfErrorValue(hintName, fxValue);

            if (fxValue is BlankValue)
            {
                return null;
            }

            // Use Power Fx coercions to handle the corner cases like Decimal / Number /Float. 
            // Don't use 'var' here, clearly specify .Net type we get back and are returning to Dataverse.
            var typeCode = parameterType.type;
            switch (typeCode)
            {
                case CustomApiParamType.Bool:
                    return ((BooleanValue)fxValue).Value;

                case CustomApiParamType.DateTime:
                    if (fxValue.TryCoerceTo(out DateTimeValue dtv))
                    {

                        DateTime result = dtv.GetConvertedValue(TimeZoneInfo.Utc);
                        return result;
                    }
                    break;

                case CustomApiParamType.Decimal:
                    if (fxValue.TryCoerceTo(out DecimalValue dec))
                    {
                        decimal result = dec.Value;
                        return result;
                    }
                    break;

                case CustomApiParamType.Float:
                    if (fxValue.TryCoerceTo(out NumberValue num))
                    {
                        double result = num.Value;
                        return result;
                    }
                    break;

                case CustomApiParamType.Integer:
                    if (fxValue.TryCoerceTo(out DecimalValue num2))
                    {
                        int result = (int)num2.Value;
                        return result;
                    }
                    break;

                case CustomApiParamType.String:
                    if (fxValue.TryCoerceTo(out StringValue str))
                    {
                        string result = str.Value;
                        return result;
                    }
                    break;

                case CustomApiParamType.Guid:
                    return ((GuidValue)fxValue).Value;

                case CustomApiParamType.Entity:
                    {
                        return ToCdsEntity(fxValue, parameterType);
                    }

                case CustomApiParamType.EntityReference:
                    {
                        return ToCdsEntity(fxValue, parameterType).ToEntityReference();
                    }

                case CustomApiParamType.EntityCollection:
                    {
                        EntityCollection outputEntityCollection = new EntityCollection();
                        TableValue outputTable = fxValue as TableValue;
                        foreach (DValue<RecordValue> rows in outputTable?.Rows)
                        {
                            if (rows.Value is RecordValue recordVal)
                            {
                                Entity entityVal = ToCdsEntity(recordVal, parameterType);
                                outputEntityCollection.Entities.Add(entityVal);
                            }
                        }

                        return outputEntityCollection;
                    }

                    // TODO: Picklist, StringArray
            }

            // We shouldn't land here at runtime, since this 
            // should have been caught by intellisense at design time. 
            throw new NotSupportedException($"Unsupported param type: {typeCode}. Fx type {fxValue.GetType().Name}");
        }

        /// <summary>
        /// Converts output formulaValue to cds entity
        /// </summary>
        private static Entity ToCdsEntity(FormulaValue fxValue, IParameterType paramType)
        {
            var fxOutputObject = fxValue.ToObject();
            if (fxOutputObject is Entity entity)
            {
                return entity;
            }
            var fxOutputs = (IDictionary<string, object>)(fxOutputObject);
            if (!fxOutputs.TryGetValue(paramType.uniquename, out var outputValue))
            {
                throw new InvalidPluginExecutionException($"Unable to extract value of output from pfx result");
            }

            return (Entity)outputValue;
        }

        /// <summary>
        /// Throw an InvalidPluginExecutionException excpetion if value is an error.
        /// </summary>
        /// <param name="field">name of field containing error, used for error message.</param>
        /// <param name="value"></param>
        /// <exception cref="InvalidPluginExecutionException"></exception>
        private static void ThrowIfErrorValue(string field, FormulaValue value)
        {
            if (value is ErrorValue error)
            {
                var errorMessage = ErrorToString(error);

                // Must specifically by InvalidPluginExecutionException for plugins. 
                throw new InvalidPluginExecutionException($"CustomAPI parameter `${field}` failed with error {errorMessage}");
            }
        }

        private static string ErrorToString(ErrorValue error)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var x in error.Errors)
            {
                sb.Append(x.ToString());
                sb.Append(';');
            }
            return sb.ToString();
        }
    }
}
