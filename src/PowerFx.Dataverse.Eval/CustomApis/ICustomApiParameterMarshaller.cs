using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Dataverse
{
    /// <summary>
    /// Convert a Custom API parameter type to a Power Fx type. 
    /// </summary>
    public interface ICustomApiParameterMarshaller
    {
        FormulaType ToType(IParameterType parameterType);
    }

    /// <summary>
    /// Default implementation.
    /// </summary>
    public class CustomApiParameterMarshaller : ICustomApiParameterMarshaller
    {
        // Optional - if missing, can only handle primitives
        private readonly CdsEntityMetadataProvider _metadataCache;

        public CustomApiParameterMarshaller(CdsEntityMetadataProvider metadataCache)
        {
            _metadataCache = metadataCache;
        }

        public FormulaType ToType(IParameterType parameterType)
        {
            parameterType = parameterType ?? throw new NotSupportedException($"parameterType is null.");

            var typeCode = parameterType.type;
            if (typeCode == CustomApiParamType.EntityReference || typeCode == CustomApiParamType.Entity || typeCode == CustomApiParamType.EntityCollection)
            {
                var logicalName = parameterType.logicalentityname;
                if (string.IsNullOrWhiteSpace(logicalName))
                {
                    // We normally need a logical name so we can create a strongly typed Fx type.
                    // But if we're just calling (not implementing) an existing Custom API - we can loosen. 
                    if (parameterType is CustomApiRequestParam)
                    {
                        return RecordType.Empty();
                    }

                    throw new NotSupportedException($"Type {typeCode} requires ${nameof(IParameterType.logicalentityname)} to be set");
                }
                var metadataCache = _metadataCache ?? throw new NotSupportedException($"Can't resolve {typeCode}:{logicalName}  - Metadatacache is not set");

                var type = metadataCache.GetRecordType(logicalName);

                if (typeCode == CustomApiParamType.EntityCollection)
                {
                    return type?.ToTable();
                }

                return type;
            }

            return typeCode.ToPrimitivePowerFxType(); // throws if not handled 
        }
    }
}
