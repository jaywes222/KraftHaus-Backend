using System.Text.Json;
using System.Text.Json.Serialization;
using Common.Domain.StronglyTypedIds;

namespace Common.Application.JsonConverters;

public sealed class StronglyTypedIdJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsValueType
               && typeof(IStronglyTypedId).IsAssignableFrom(typeToConvert);
    }

    public override JsonConverter CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (!typeof(IStronglyTypedId).IsAssignableFrom(typeToConvert))
        {
            throw new InvalidOperationException(
                $"Type '{typeToConvert}' does not implement {nameof(IStronglyTypedId)}.");
        }

        var converterType =
            typeof(StronglyTypedIdJsonConverter<>)
                .MakeGenericType(typeToConvert);

        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}
